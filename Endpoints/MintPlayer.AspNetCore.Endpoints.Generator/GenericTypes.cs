using System.Text;
using Microsoft.CodeAnalysis;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// The type-parameter mechanics issue #34 needs: which types are open, how to name, record and
/// construct them, and how to substitute a closing's type arguments into what a declaration says.
/// </summary>
/// <remarks>
/// "Type parameters" always means <b>all</b> of them, outermost containing type first — the order of
/// <c>Type.GetGenericArguments()</c> at run time, where <c>Outer&lt;T&gt;.Inner</c> is a generic type
/// of arity one. The generator and the runtime's <c>EndpointNameOf</c> must agree on it, since both
/// derive the endpoint name from it (PRD D4).
/// </remarks>
internal static class GenericTypes
{
    /// <summary>True when the type, or a type it is nested in, has type parameters.</summary>
    public static bool IsOpen(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.TypeParameters.Length > 0) return true;
        }

        return false;
    }

    /// <summary>Every type parameter of <paramref name="definition"/>, outermost containing type first.</summary>
    public static List<ITypeParameterSymbol> AllTypeParameters(INamedTypeSymbol definition)
    {
        var chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = definition.OriginalDefinition; current is not null; current = current.ContainingType)
            chain.Add(current);
        chain.Reverse();

        return chain.SelectMany(type => type.TypeParameters).ToList();
    }

    /// <summary>Every type argument of <paramref name="type"/>, outermost containing type first.</summary>
    public static List<ITypeSymbol> AllTypeArguments(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
            chain.Add(current);
        chain.Reverse();

        return chain.SelectMany(level => level.TypeArguments).ToList();
    }

    /// <summary>The metadata name <c>IAssemblySymbol.GetTypeByMetadataName</c> resolves: <c>Ns.Outer`1+Inner</c>.</summary>
    public static string MetadataNameOf(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = type.OriginalDefinition; current is not null; current = current.ContainingType)
            chain.Add(current);
        chain.Reverse();

        var ns = chain[0].ContainingNamespace is { IsGlobalNamespace: false } containing ? containing.ToDisplayString() + "." : "";
        return ns + string.Join("+", chain.Select(level => level.MetadataName));
    }

    /// <summary>The <c>typeof</c> operand of the unbound type: <c>global::Ns.Outer&lt;&gt;.Inner</c>, <c>global::Ns.Pair&lt;,&gt;</c>.</summary>
    public static string UnboundTypeOf(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = type.OriginalDefinition; current is not null; current = current.ContainingType)
            chain.Add(current);
        chain.Reverse();

        var builder = new StringBuilder("global::");
        if (chain[0].ContainingNamespace is { IsGlobalNamespace: false } ns)
            builder.Append(ns.ToDisplayString()).Append('.');

        for (var i = 0; i < chain.Count; i++)
        {
            if (i > 0) builder.Append('.');
            builder.Append(chain[i].Name);
            if (chain[i].Arity > 0)
                builder.Append('<').Append(new string(',', chain[i].Arity - 1)).Append('>');
        }

        return builder.ToString();
    }

    /// <summary>
    /// The suffix a closing adds to an endpoint's name (PRD D4): <c>_AppUser</c>, <c>_List_Int32</c>,
    /// <c>_Int32Array</c>. CLR names, not C# keywords, because the runtime's <c>EndpointNameOf</c>
    /// derives the same name from <c>Type.Name</c>.
    /// </summary>
    public static string NameSuffix(IEnumerable<ITypeSymbol> typeArguments)
        => string.Concat(typeArguments.Select(argument => "_" + ArgumentName(argument)));

    private static string ArgumentName(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return ArgumentName(array.ElementType) + "Array";
            case INamedTypeSymbol named:
            {
                var arguments = AllTypeArguments(named);
                // Name, not MetadataName: the arity suffix is not part of it, and a tuple's name is ValueTuple.
                var name = named.IsTupleType ? "ValueTuple" : named.Name;
                return arguments.Count == 0 ? name : name + NameSuffix(arguments);
            }
            default:
                return type.Name;
        }
    }

    /// <summary>Maps every type parameter of <paramref name="constructed"/>'s definition to its argument.</summary>
    public static Dictionary<ITypeParameterSymbol, ITypeSymbol> MapOf(INamedTypeSymbol constructed)
    {
        var map = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        for (INamedTypeSymbol? current = constructed; current is not null; current = current.ContainingType)
        {
            var parameters = current.OriginalDefinition.TypeParameters;
            for (var i = 0; i < parameters.Length && i < current.TypeArguments.Length; i++)
                map[parameters[i]] = current.TypeArguments[i];
        }

        return map;
    }

    /// <summary>
    /// Constructs <paramref name="definition"/> with <paramref name="arguments"/> (all type arguments,
    /// outermost first). Containing types are constructed first and the nested type is taken from the
    /// constructed container, which is how Roslyn represents <c>Outer&lt;X&gt;.Inner</c>.
    /// </summary>
    public static INamedTypeSymbol Construct(INamedTypeSymbol definition, IReadOnlyList<ITypeSymbol> arguments)
    {
        var chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = definition.OriginalDefinition; current is not null; current = current.ContainingType)
            chain.Add(current);
        chain.Reverse();

        var used = 0;
        INamedTypeSymbol? built = null;
        foreach (var level in chain)
        {
            var member = built is null
                ? level
                : built.GetTypeMembers(level.Name, level.Arity).First(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, level));

            if (level.Arity > 0)
            {
                member = member.Construct(arguments.Skip(used).Take(level.Arity).ToArray());
                used += level.Arity;
            }

            built = member;
        }

        return built!;
    }

    /// <summary>
    /// Replaces the type parameters in <paramref name="type"/> by their arguments in <paramref name="map"/>
    /// — for what Roslyn hands back unsubstituted, such as an attribute's type argument read through a
    /// constructed type (<c>[MemberOf&lt;Api&gt;]</c> inside <c>Outer&lt;T&gt;</c> is <c>Outer&lt;T&gt;.Api</c>).
    /// </summary>
    public static ITypeSymbol Substitute(ITypeSymbol type, Dictionary<ITypeParameterSymbol, ITypeSymbol> map, Compilation compilation)
    {
        switch (type)
        {
            case ITypeParameterSymbol parameter:
                return map.TryGetValue(parameter, out var argument) ? argument : parameter;

            case IArrayTypeSymbol array:
                return compilation.CreateArrayTypeSymbol(Substitute(array.ElementType, map, compilation), array.Rank);

            case INamedTypeSymbol named when map.Count > 0 && ContainsTypeParameter(named):
            {
                var containing = named.ContainingType is { } outer
                    ? (INamedTypeSymbol)Substitute(outer, map, compilation)
                    : null;

                var definition = named.OriginalDefinition;
                var member = containing is null
                    ? definition
                    : containing.GetTypeMembers(definition.Name, definition.Arity).First(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, definition));

                return definition.Arity == 0
                    ? member
                    : member.Construct(named.TypeArguments.Select(argumentType => Substitute(argumentType, map, compilation)).ToArray());
            }

            default:
                return type;
        }
    }

    private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter) || (named.ContainingType is { } outer && ContainsTypeParameter(outer)),
        _ => false,
    };
}
