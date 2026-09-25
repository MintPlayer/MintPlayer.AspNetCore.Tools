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
    public static string? Read(INamedTypeSymbol symbol, HttpMethodKind verb, Compilation compilation, out bool newMemberIgnored, CancellationToken cancellationToken)
    {
        var nearest = FindMethodsProperty(symbol);
        var implementation = FindImplementation(symbol);

        newMemberIgnored = implementation is not null && nearest is not null &&
                           !SymbolEqualityComparer.Default.Equals(implementation.OriginalDefinition, nearest.OriginalDefinition);

        var property = implementation ?? nearest;
        if (property is null || IsVerbInterfaceDefault(property))
        {
            return verb switch
            {
                HttpMethodKind.Get => "GET",
                HttpMethodKind.Post => "POST",
                HttpMethodKind.Put => "PUT",
                HttpMethodKind.Delete => "DELETE",
                HttpMethodKind.Patch => "PATCH",
                _ => null,
            };
        }

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var expression = RouteLiteral.ValueExpressionOf(reference.GetSyntax(cancellationToken));
            if (expression is null) continue;

            var treeModel = compilation.GetSemanticModel(expression.SyntaxTree);
            var verbs = VerbsOf(expression, treeModel, cancellationToken);
            if (verbs is not null) return Encode(verbs);
        }

        return null;
    }

    /// <summary>Splits an encoded verb set back into its verbs.</summary>
    public static string[] Decode(string encoded)
        => encoded.Length == 0 ? new string[0] : encoded.Split(Separator);

    /// <summary>The implementation of <c>IEndpointBase.Methods</c> the runtime dispatches to, or null.</summary>
    private static IPropertySymbol? FindImplementation(INamedTypeSymbol symbol)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.Name != "IEndpointBase" || iface.ContainingNamespace?.ToDisplayString() != EndpointsNamespace) continue;

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
           containing.ContainingNamespace?.ToDisplayString() == EndpointsNamespace;

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

    private static List<string>? VerbsOf(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken)
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
                if (model.GetSymbolInfo(expression, cancellationToken).Symbol is IFieldSymbol
                    {
                        IsStatic: true,
                        ContainingType: { Name: "HttpVerbs" } containing,
                    } field &&
                    containing.ContainingNamespace?.ToDisplayString() == EndpointsNamespace)
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

    private static List<string>? ConstantStrings(SeparatedSyntaxList<ExpressionSyntax> items, SemanticModel model, CancellationToken cancellationToken)
    {
        var verbs = new List<string>();
        foreach (var item in items)
        {
            if (ConstantString(item, model, cancellationToken) is not { } verb) return null;
            verbs.Add(verb);
        }
        return verbs;
    }

    private static string? ConstantString(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken)
    {
        var constant = model.GetConstantValue(expression, cancellationToken);
        if (!constant.HasValue || constant.Value is not string value) return null;

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
