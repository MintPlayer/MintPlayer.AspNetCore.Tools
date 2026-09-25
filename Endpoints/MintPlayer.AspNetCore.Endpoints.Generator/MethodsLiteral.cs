using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Recovers the set of HTTP verbs an endpoint answers, so MPEP007 can tell two endpoints on one
/// route apart by verb.
/// </summary>
/// <remarks>
/// The five verb interfaces answer exactly one verb each. A custom <c>Methods</c> is only known when
/// it is written as a literal collection of constant strings (<c>=&gt; ["GET", "HEAD"]</c>,
/// <c>new[] { … }</c>) or as one of the <c>HttpVerbs</c> fields. That literal is what catches the
/// measured partial-overlap case — <c>["GET","HEAD"]</c> against <c>["HEAD","OPTIONS"]</c>, where GET
/// and OPTIONS answer 200 and every HEAD request is an ambiguous-match 500 — which ASP0022 misses
/// entirely.
/// <para>
/// <b>Null means "unknown", and unknown means "no conflict".</b> Anything else — a field, a method
/// call, a multi-statement getter, an endpoint whose <c>Methods</c> lives in a referenced assembly —
/// must keep MPEP007 silent. Guessing a verb set would turn a Warning that is supposed to be exact
/// into noise.
/// </para>
/// </remarks>
internal static class MethodsLiteral
{
    private const string EndpointsNamespace = "MintPlayer.AspNetCore.Endpoints";

    /// <summary>The separator in the encoded verb set. Never a character a verb token may contain.</summary>
    public const char Separator = ',';

    /// <summary>
    /// The endpoint's verbs, upper-cased, de-duplicated, ordinally sorted and joined by
    /// <see cref="Separator"/>; or <see langword="null"/> when they cannot be known at compile time.
    /// </summary>
    /// <remarks>
    /// A string rather than a collection so <see cref="EndpointInfo"/> stays trivially equatable
    /// (R6.7). The verbs are those of <b>the <c>Methods</c> the runtime uses</b>: the implementation of
    /// <c>IEndpointBase.Methods</c>, which is what <c>TEndpoint.Methods</c> dispatches to — resolved
    /// like <c>Path</c> (<see cref="RouteLiteral.ReadImplementation"/>, PRD D7). A <c>Methods</c>
    /// declared on the class or a base class that implements the interface wins over the verb
    /// interface's default implementation, which stands for its verb. A <c>new static Methods</c> on a
    /// class that does not re-implement the interface is not the implementation, and
    /// <paramref name="newMemberIgnored"/> reports it (MPEP032).
    /// </remarks>
    /// <param name="symbol">The endpoint type; constructed types work.</param>
    /// <param name="verb">The verb its verb interface stands for.</param>
    /// <param name="compilation">The compilation the symbol belongs to.</param>
    /// <param name="newMemberIgnored">True when a nearer <c>new static Methods</c> is not the one used.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <param name="model">Optional: the caller's semantic model, reused when the expression lives in its tree.</param>
    /// <remarks>
    /// The same fast path as <see cref="RouteLiteral.ReadImplementation"/> (PRD addendum 2, D19): when
    /// the class's own declaration is decidably the implementation, or the class has no base class and
    /// declares no <c>Methods</c> (the common case, answered by its verb interface), the interface map is
    /// not consulted; string literals are read from syntax.
    /// </remarks>
    public static string? Read(INamedTypeSymbol symbol, HttpMethodKind verb, Compilation compilation, out bool newMemberIgnored, CancellationToken cancellationToken, SemanticModel? model = null)
    {
        var interfaceMember = RouteLiteral.FindInterfaceMember(symbol, "IEndpointBase", "Methods");
        if (interfaceMember is not null && RouteLiteral.OwnDeclarationCanImplement(symbol, interfaceMember))
        {
            // Syntax first, without binding the class's member list.
            var declarations = RouteLiteral.OwnPropertyDeclarations(symbol, "Methods", cancellationToken);
            var hasBaseClass = symbol.BaseType is { SpecialType: not SpecialType.System_Object };

            // No Methods of its own and no base class: only an interface default can implement it, and
            // with no user interface declaring one, that is its verb interface's.
            if (declarations is { Count: 0 } && !hasBaseClass && !HasUserInterfaceMethods(symbol))
            {
                newMemberIgnored = false;
                return VerbOf(verb);
            }

            // One `public static IEnumerable<string> Methods` of its own: the implementation. Only the
            // declared type is bound, to compare it with the interface's exactly.
            if (declarations is { Count: 1 } &&
                RouteLiteral.IsPublicStaticImplicit(declarations[0]) &&
                RouteLiteral.ModelFor(declarations[0].SyntaxTree, compilation, model).GetTypeInfo(declarations[0].Type, cancellationToken).Type is { } declaredType &&
                declaredType.SpecialType != SpecialType.System_String &&
                SymbolEqualityComparer.Default.Equals(declaredType, interfaceMember.Type))
            {
                newMemberIgnored = false;
                return VerbsOfDeclaration(declarations[0], compilation, model, cancellationToken);
            }
        }

        IPropertySymbol? property;
        if (interfaceMember is not null &&
            RouteLiteral.TryFindOwnImplementation(symbol, interfaceMember, IsMethodsProperty, out var own) &&
            (own is not null || !HasUserInterfaceMethods(symbol)))
        {
            // The class's own Methods, or none: then only an interface default can implement it, and
            // with no user interface declaring one, that is a verb interface's.
            newMemberIgnored = false;
            property = own;
        }
        else
        {
            var nearest = FindMethodsProperty(symbol);
            var implementation = FindImplementation(symbol);

            newMemberIgnored = implementation is not null && nearest is not null &&
                               !SymbolEqualityComparer.Default.Equals(implementation.OriginalDefinition, nearest.OriginalDefinition);

            property = implementation ?? nearest;
        }

        if (property is null || IsVerbInterfaceDefault(property))
            return VerbOf(verb);

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var verbs = VerbsOfDeclaration(reference.GetSyntax(cancellationToken), compilation, model, cancellationToken);
            if (verbs is not null) return verbs;
        }

        return null;
    }

    /// <summary>The verb a verb interface stands for, encoded; null for a custom endpoint.</summary>
    private static string? VerbOf(HttpMethodKind verb) => verb switch
    {
        HttpMethodKind.Get => "GET",
        HttpMethodKind.Post => "POST",
        HttpMethodKind.Put => "PUT",
        HttpMethodKind.Delete => "DELETE",
        HttpMethodKind.Patch => "PATCH",
        _ => null,
    };

    /// <summary>The encoded verbs of one <c>Methods</c> declaration, or null when they cannot be read.</summary>
    private static string? VerbsOfDeclaration(SyntaxNode declaration, Compilation compilation, SemanticModel? model, CancellationToken cancellationToken)
    {
        var expression = RouteLiteral.ValueExpressionOf(declaration);
        if (expression is null) return null;

        var verbs = VerbsOf(expression, new LazyModel(expression.SyntaxTree, compilation, model), cancellationToken);
        return verbs is null ? null : Encode(verbs);
    }

    /// <summary>A static, non-string property named <c>Methods</c>, or an explicit implementation named <c>Ns.IEndpointBase.Methods</c>.</summary>
    private static bool IsMethodsProperty(IPropertySymbol property) =>
        property.IsStatic &&
        property.Type.SpecialType != SpecialType.System_String &&
        (property.Name == "Methods" || property.Name.EndsWith(".Methods", StringComparison.Ordinal));

    /// <summary>
    /// True when an interface outside the library declares a static <c>Methods</c>, which could be a
    /// default implementation of <c>IEndpointBase.Methods</c> that only the interface map can rank.
    /// </summary>
    private static bool HasUserInterfaceMethods(INamedTypeSymbol symbol)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (SymbolNames.IsNamespace(iface.ContainingNamespace, EndpointsNamespace)) continue;
            if (iface.GetMembers().OfType<IPropertySymbol>().Any(property => property.IsStatic && (property.Name == "Methods" || property.Name.EndsWith(".Methods", StringComparison.Ordinal))))
                return true;
        }

        return false;
    }

    /// <summary>A semantic model created only when a verb cannot be read from syntax.</summary>
    private sealed class LazyModel(SyntaxTree tree, Compilation compilation, SemanticModel? hint)
    {
        private SemanticModel? model;

        public SemanticModel Value => model ??= RouteLiteral.ModelFor(tree, compilation, hint);
    }

    /// <summary>Splits an encoded verb set back into its verbs.</summary>
    public static string[] Decode(string encoded)
        => encoded.Length == 0 ? new string[0] : encoded.Split(Separator);

    /// <summary>The implementation of <c>IEndpointBase.Methods</c> the runtime dispatches to, or null.</summary>
    private static IPropertySymbol? FindImplementation(INamedTypeSymbol symbol)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.Name != "IEndpointBase" || !SymbolNames.IsNamespace(iface.ContainingNamespace, EndpointsNamespace)) continue;

            foreach (var member in iface.GetMembers("Methods"))
            {
                if (member is IPropertySymbol { IsStatic: true } property &&
                    symbol.FindImplementationForInterfaceMember(property) is IPropertySymbol implementation)
                    return implementation;
            }
        }

        return null;
    }

    /// <summary>
    /// A verb interface's own default (<c>static IEnumerable&lt;string&gt; IEndpointBase.Methods =&gt;
    /// HttpVerbs.Get</c> on <c>IGetEndpoint</c>): the verb, which is known without reading the body — a
    /// body that lives in the library's metadata anyway.
    /// </summary>
    private static bool IsVerbInterfaceDefault(IPropertySymbol property)
        => property.ContainingType is { TypeKind: TypeKind.Interface } containing &&
           SymbolNames.IsNamespace(containing.ContainingNamespace, EndpointsNamespace);

    /// <summary>The nearest <c>Methods</c> declared on the class or a base class, or null.</summary>
    private static IPropertySymbol? FindMethodsProperty(INamedTypeSymbol symbol)
    {
        foreach (var type in symbol.GetAllBaseTypes())
        {
            foreach (var member in type.GetMembers())
            {
                if (member is not IPropertySymbol { IsStatic: true } property) continue;
                if (property.Type.SpecialType == SpecialType.System_String) continue;

                // "Methods", or an explicit implementation named "Ns.IEndpointBase.Methods".
                if (property.Name == "Methods" || property.Name.EndsWith(".Methods", StringComparison.Ordinal))
                    return property;
            }
        }

        return null;
    }

    private static List<string>? VerbsOf(ExpressionSyntax expression, LazyModel model, CancellationToken cancellationToken)
    {
        switch (expression)
        {
            case CollectionExpressionSyntax collection:
            {
                var verbs = new List<string>();
                foreach (var element in collection.Elements)
                {
                    // A spread (..other) has no single constant to read.
                    if (element is not ExpressionElementSyntax { Expression: { } item }) return null;
                    if (ConstantString(item, model, cancellationToken) is not { } verb) return null;
                    verbs.Add(verb);
                }
                return verbs;
            }

            case ImplicitArrayCreationExpressionSyntax implicitArray:
                return ConstantStrings(implicitArray.Initializer.Expressions, model, cancellationToken);

            case ArrayCreationExpressionSyntax { Initializer: { } initializer }:
                return ConstantStrings(initializer.Expressions, model, cancellationToken);

            case MemberAccessExpressionSyntax or IdentifierNameSyntax:
                // HttpVerbs.Get and friends are static readonly, so not constants; recognise them by
                // symbol, and only the library's own.
                if (model.Value.GetSymbolInfo(expression, cancellationToken).Symbol is IFieldSymbol
                    {
                        IsStatic: true,
                        ContainingType: { Name: "HttpVerbs" } containing,
                    } field &&
                    SymbolNames.IsNamespace(containing.ContainingNamespace, EndpointsNamespace))
                {
                    return field.Name switch
                    {
                        "Get" or "Post" or "Put" or "Delete" or "Patch" => new List<string> { field.Name.ToUpperInvariant() },
                        _ => null,
                    };
                }
                return null;

            default:
                return null;
        }
    }

    private static List<string>? ConstantStrings(SeparatedSyntaxList<ExpressionSyntax> items, LazyModel model, CancellationToken cancellationToken)
    {
        var verbs = new List<string>();
        foreach (var item in items)
        {
            if (ConstantString(item, model, cancellationToken) is not { } verb) return null;
            verbs.Add(verb);
        }
        return verbs;
    }

    private static string? ConstantString(ExpressionSyntax expression, LazyModel model, CancellationToken cancellationToken)
    {
        // A string literal needs no binding (PRD addendum 2, D19); anything else is folded by the model.
        var value = RouteLiteral.FoldStringLiterals(expression);
        if (value is null)
        {
            var constant = model.Value.GetConstantValue(expression, cancellationToken);
            if (!constant.HasValue || constant.Value is not string folded) return null;
            value = folded;
        }

        // A verb containing the separator cannot be encoded faithfully; admit ignorance instead.
        return value.IndexOf(Separator) >= 0 ? null : value;
    }

    private static string Encode(IEnumerable<string> verbs) => string.Join(
        Separator.ToString(),
        verbs.Select(verb => verb.Trim().ToUpperInvariant())
            .Where(verb => verb.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(verb => verb, StringComparer.Ordinal));
}
