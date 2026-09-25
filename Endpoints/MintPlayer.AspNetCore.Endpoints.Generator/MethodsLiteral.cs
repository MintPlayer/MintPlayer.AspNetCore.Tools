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
    /// (R6.7). A <c>Methods</c> declared on the class or one of its base classes wins over the verb
    /// interface, exactly as it does at run time: the interface's implementation is only a default.
    /// </remarks>
    public static string? Read(INamedTypeSymbol symbol, HttpMethodKind verb, SemanticModel model, CancellationToken cancellationToken)
        => Read(symbol, verb, model.Compilation, cancellationToken);

    /// <inheritdoc cref="Read(INamedTypeSymbol, HttpMethodKind, SemanticModel, CancellationToken)"/>
    public static string? Read(INamedTypeSymbol symbol, HttpMethodKind verb, Compilation compilation, CancellationToken cancellationToken)
    {
        var property = FindMethodsProperty(symbol);
        if (property is null)
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
