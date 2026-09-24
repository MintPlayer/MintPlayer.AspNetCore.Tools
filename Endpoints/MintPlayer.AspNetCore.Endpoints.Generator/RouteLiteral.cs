using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Recovers the compile-time value of a <c>static abstract string</c> member — <c>Path</c> on an
/// endpoint, <c>Prefix</c> on a group — so the generator can reason about routes instead of
/// deferring every question about them to run time.
/// </summary>
/// <remarks>
/// Everything route-shaped this library can diagnose depends on this file. Without it the
/// generator emits <c>TEndpoint.Path</c> as an expression it never reads, which is why a typo'd
/// route parameter, two endpoints on one route, and a group-relative path written absolutely are
/// all invisible at build time today.
/// <para>
/// <b>Recovery is best-effort by design, and callers must treat <see langword="null"/> as "do not
/// know", never as "empty".</b> Two cases are permanently unrecoverable and both are legitimate:
/// a non-constant expression (an interpolated string, a <c>static readonly</c> field, a method
/// call), and any endpoint that lives in a <i>referenced assembly</i>, where the property body is
/// IL rather than syntax and <c>DeclaringSyntaxReferences</c> is empty. A generator that hard-fails
/// on either would break cross-assembly endpoints outright.
/// </para>
/// </remarks>
internal static class RouteLiteral
{
    /// <summary>
    /// Reads a static string member's constant value, walking the base chain so a member declared
    /// on a base class is still found.
    /// </summary>
    /// <param name="symbol">The endpoint or group type.</param>
    /// <param name="memberName">
    /// <c>Path</c> or <c>Prefix</c>. An explicit interface implementation is matched too, since it
    /// is named after the fully qualified interface (<c>…IEndpointBase.Path</c>).
    /// </param>
    /// <param name="model">
    /// Any semantic model from the same compilation. The member may be declared in a different
    /// syntax tree than the one being visited — a base class, or the other half of a partial — so
    /// the model for the expression's own tree is fetched from
    /// <see cref="SemanticModel.Compilation"/>. Going through the compilation here rather than
    /// combining the pipeline with <c>CompilationProvider</c> is what keeps the generator's
    /// caching intact.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The constant value, or <see langword="null"/> when it cannot be recovered.</returns>
    public static string? Read(
        INamedTypeSymbol symbol,
        string memberName,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var property = FindStaticStringProperty(symbol, memberName);
        if (property is null) return null;

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var expression = ValueExpressionOf(reference.GetSyntax(cancellationToken));
            if (expression is null) continue;

            var treeModel = model.Compilation.GetSemanticModel(expression.SyntaxTree);
            var constant = treeModel.GetConstantValue(expression, cancellationToken);
            if (constant.HasValue && constant.Value is string value)
                return value;
        }

        return null;
    }

    /// <summary>
    /// Finds the static string property contributing <paramref name="memberName"/>, nearest
    /// declaration first.
    /// </summary>
    /// <remarks>
    /// The base walk matters: an endpoint may inherit <c>Path</c> from a shared base class, and the
    /// nearest declaration is the one that wins at run time, so it must be the one read here.
    /// </remarks>
    private static IPropertySymbol? FindStaticStringProperty(INamedTypeSymbol symbol, string memberName)
    {
        foreach (var type in symbol.GetAllBaseTypes())
        {
            foreach (var member in type.GetMembers())
            {
                if (member is not IPropertySymbol property) continue;
                if (!property.IsStatic) continue;
                if (property.Type.SpecialType != SpecialType.System_String) continue;

                // "Path", or an explicit implementation named "Ns.IEndpointBase.Path".
                if (property.Name != memberName &&
                    !property.Name.EndsWith("." + memberName, StringComparison.Ordinal))
                    continue;

                return property;
            }
        }

        return null;
    }

    /// <summary>
    /// Picks the expression a property's value comes from, across every spelling that can carry a
    /// constant.
    /// </summary>
    /// <remarks>
    /// Recoverable: <c>=&gt; "/x"</c>, <c>{ get; } = "/x";</c>, <c>{ get =&gt; "/x"; }</c> and
    /// <c>{ get { return "/x"; } }</c> — each of which then folds through
    /// <c>GetConstantValue</c>, so a <c>const</c>, a concatenation of <c>const</c>s, a
    /// <c>nameof</c> and a raw string literal all work.
    /// <para>
    /// Deliberately not handled: a getter with more than one statement. There is no single
    /// expression to fold, and guessing at the first <c>return</c> of a branching getter would
    /// produce a route the endpoint does not actually answer on — worse than admitting ignorance.
    /// </para>
    /// </remarks>
    private static ExpressionSyntax? ValueExpressionOf(SyntaxNode node)
    {
        if (node is not PropertyDeclarationSyntax declaration) return null;

        if (declaration.ExpressionBody?.Expression is { } arrowBody) return arrowBody;
        if (declaration.Initializer?.Value is { } initializer) return initializer;

        var getter = declaration.AccessorList?.Accessors
            .FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));

        if (getter is null) return null;
        if (getter.ExpressionBody?.Expression is { } getterArrow) return getterArrow;
        if (getter.Body is null) return null;

        // Not a list pattern: this assembly targets netstandard2.0, where System.Index does not
        // exist, so `is [X]` fails to compile with CS0518 rather than anything that names the
        // real cause.
        var statements = getter.Body.Statements;
        if (statements.Count != 1) return null;

        return statements[0] is ReturnStatementSyntax { Expression: { } returned }
            ? returned
            : null;
    }
}
