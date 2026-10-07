using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace MintPlayer.AspNetCore.Endpoints.Generator.CodeFixes;

/// <summary>
/// Adds the <c>IServiceProvider services</c> parameter that MPEP035 reports missing from a
/// one-argument <c>Configure(RouteGroupBuilder)</c> or <c>Configure(RouteHandlerBuilder)</c> hook.
/// </summary>
/// <remarks>
/// <para>
/// Since 11.4 the group and endpoint <c>Configure</c> hooks take the root service provider as a
/// second parameter, and adding it is the whole migration (PLAN spike S2): the body keeps compiling,
/// and the method becomes the interface member's implementation again — an implicit one starts being
/// called, an explicit one stops failing with CS0539.
/// </para>
/// <para>
/// The parameter type is written fully qualified with the <see cref="Simplifier"/> annotation, so it
/// reads <c>IServiceProvider</c> wherever <c>System</c> is in scope and stays correct where it is not.
/// The id is a literal, as in <see cref="MakePartialCodeFixProvider"/>: this assembly does not
/// reference the generator.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddServiceProviderParameterCodeFixProvider)), Shared]
public sealed class AddServiceProviderParameterCodeFixProvider : CodeFixProvider
{
    /// <summary>MPEP035: a one-argument <c>Configure</c> hook is no longer called.</summary>
    public const string LegacyConfigureHookIgnoredId = "MPEP035";

    /// <summary>One key for every fix this provider offers, so Fix All repairs every hook in one pass.</summary>
    public const string EquivalenceKey = "MintPlayer.Endpoints.AddServiceProviderParameter";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(LegacyConfigureHookIgnoredId);

    /// <summary>
    /// The stock batch fixer. Each fix edits one method's parameter list and nothing else, so the
    /// edits of a Fix All never overlap.
    /// </summary>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (FindTarget(root, diagnostic) is null)
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Add 'IServiceProvider services' parameter",
                    createChangedDocument: cancellationToken => AddParameterAsync(context.Document, diagnostic, cancellationToken),
                    equivalenceKey: EquivalenceKey),
                diagnostic);
        }
    }

    private static async Task<Document> AddParameterAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var method = FindTarget(root, diagnostic);
        if (method is null)
            return document;

        var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("services"))
            .WithType(SyntaxFactory.ParseTypeName("global::System.IServiceProvider")
                .WithAdditionalAnnotations(Simplifier.Annotation)
                .WithTrailingTrivia(SyntaxFactory.Space));

        // An explicit separator: SeparatedSyntaxList.Add would write the comma without the space after it.
        var parameters = SyntaxFactory.SeparatedList(
            new[] { method.ParameterList.Parameters[0], parameter },
            new[] { SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space) });
        return document.WithSyntaxRoot(root.ReplaceNode(method, method.WithParameterList(method.ParameterList.WithParameters(parameters))));
    }

    /// <summary>The one-parameter method the diagnostic is on; null when there is nothing left to do.</summary>
    private static MethodDeclarationSyntax? FindTarget(SyntaxNode root, Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource || !root.FullSpan.Contains(diagnostic.Location.SourceSpan))
            return null;

        // AncestorsAndSelf, as in MakePartialCodeFixProvider: the generator reports on the identifier,
        // and a later move to the whole declaration must not silently stop offering the fix.
        var method = root.FindNode(diagnostic.Location.SourceSpan)
            .AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        return method is { ParameterList.Parameters.Count: 1 } ? method : null;
    }
}
