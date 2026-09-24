using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MintPlayer.AspNetCore.Endpoints.Generator.CodeFixes;

/// <summary>
/// Adds the missing <c>partial</c> modifier that MPEP001, MPEP014 or MPEP019 reports.
/// </summary>
/// <remarks>
/// <para>
/// All three diagnostics are already correct and correctly located on the endpoint's identifier;
/// only the repair was manual, and for MPEP001 it sits under the cascading CS errors a missing
/// generated base class produces. MPEP001 and MPEP014 ask for the endpoint itself to be
/// <c>partial</c>; MPEP019 asks for every type the endpoint is nested in to be <c>partial</c>.
/// </para>
/// <para>
/// The diagnostics come from the source generator, not from a <c>DiagnosticAnalyzer</c>. The IDE
/// offers fixes for generator diagnostics the same way, keyed on the id alone, which is why the ids
/// are literals here: this assembly does not reference the generator.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakePartialCodeFixProvider)), Shared]
public sealed class MakePartialCodeFixProvider : CodeFixProvider
{
    /// <summary>MPEP001: a typed endpoint needs a generated base class.</summary>
    public const string EndpointMustBePartialId = "MPEP001";

    /// <summary>MPEP014: an endpoint's bound properties need a generated binder.</summary>
    public const string BoundPropertiesNeedPartialId = "MPEP014";

    /// <summary>MPEP019: an endpoint's generated code must reopen its containing types.</summary>
    public const string ContainingTypeNotPartialId = "MPEP019";

    /// <summary>
    /// One key for every fix this provider offers, so Fix All in a document, project or solution
    /// repairs all three diagnostics in one pass.
    /// </summary>
    public const string EquivalenceKey = "MintPlayer.Endpoints.MakePartial";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(EndpointMustBePartialId, BoundPropertiesNeedPartialId, ContainingTypeNotPartialId);

    /// <summary>
    /// The stock batch fixer. Adding a modifier to one type never changes whether another needs one,
    /// and when two endpoints nested in the same non-partial type both ask for that container to
    /// become <c>partial</c>, the batch fixer merges the two identical edits into one (pinned by
    /// <c>FixAll_RepairsEveryDiagnosticInTheDocumentInOnePass</c>).
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
            var targets = FindTargets(root, diagnostic);
            if (targets.Count == 0)
                continue;

            var names = string.Join("', '", targets.Select(target => target.Identifier.ValueText));
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Make '{names}' partial",
                    createChangedDocument: cancellationToken => MakePartialAsync(context.Document, diagnostic, cancellationToken),
                    equivalenceKey: EquivalenceKey),
                diagnostic);
        }
    }

    private static async Task<Document> MakePartialAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var targets = FindTargets(root, diagnostic);
        if (targets.Count == 0)
            return document;

        // ReplaceNodes hands each callback the node with its descendants already rewritten, so nested
        // containers (MPEP019) all change in one pass.
        return document.WithSyntaxRoot(root.ReplaceNodes(targets, (_, rewritten) => AddPartial(rewritten)));
    }

    /// <summary>
    /// The declarations a diagnostic asks to be <c>partial</c>, outermost first; empty when there is
    /// nothing left to do.
    /// </summary>
    private static List<TypeDeclarationSyntax> FindTargets(SyntaxNode root, Diagnostic diagnostic)
    {
        var targets = new List<TypeDeclarationSyntax>();
        if (!diagnostic.Location.IsInSource || !root.FullSpan.Contains(diagnostic.Location.SourceSpan))
            return targets;

        // AncestorsAndSelf rather than FindToken().Parent: the generator reports on the identifier
        // today, and a later move to the whole declaration must not silently stop offering the fix.
        var endpoint = root.FindNode(diagnostic.Location.SourceSpan)
            .AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (endpoint is null)
            return targets;

        IEnumerable<TypeDeclarationSyntax> candidates = diagnostic.Id == ContainingTypeNotPartialId
            ? endpoint.Ancestors().OfType<TypeDeclarationSyntax>().Reverse()
            : new[] { endpoint };

        targets.AddRange(candidates.Where(candidate => !candidate.Modifiers.Any(SyntaxKind.PartialKeyword)));
        return targets;
    }

    private static TypeDeclarationSyntax AddPartial(TypeDeclarationSyntax declaration)
    {
        // 'partial' must come last, directly before the keyword (CS0267). The first modifier carries
        // the declaration's leading trivia — indentation, and after attributes the new line — so
        // appending leaves that trivia where it is.
        var partial = SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.Space);

        if (declaration.Modifiers.Count > 0)
            return declaration.WithModifiers(declaration.Modifiers.Add(partial));

        // No modifiers: 'partial' becomes the first token of the declaration proper, so it takes over
        // the keyword's leading trivia and the keyword gives it up.
        return declaration
            .WithModifiers(SyntaxFactory.TokenList(partial.WithLeadingTrivia(declaration.Keyword.LeadingTrivia)))
            .WithKeyword(declaration.Keyword.WithLeadingTrivia(SyntaxFactory.TriviaList()));
    }
}
