using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MintPlayer.AspNetCore.Endpoints.Generator.BuildSuppressions;

/// <summary>
/// Suppresses CS1591 for <c>Microsoft.CodeAnalysis.IncrementalValueProviderAdditionalEx</c>, and for
/// nothing else.
/// </summary>
/// <remarks>
/// MintPlayer.ValueComparerGenerator 12.0.1 emits that empty, public, non-partial static class
/// (<c>JoinMethods.g.cs</c>) into every project that references both it and Microsoft.CodeAnalysis,
/// so the generator assembly gains a public type it never declared and a CS1591 for it. That is an
/// upstream defect: a generator should add no public API to its consumer.
/// <para>
/// The usual remedies were measured and do not reach it. The class is not partial, so it cannot be
/// documented from here. <c>[assembly: SuppressMessage]</c> does not suppress a compiler warning, and
/// an <c>.editorconfig</c> severity, even under <c>[*]</c>, is not applied to a source-generated tree.
/// A suppressor is the one mechanism that can target exactly this type; a project-wide
/// <c>NoWarn</c> would hide every future undocumented public member of the generator as well.
/// </para>
/// Delete this project once MintPlayer.ValueComparerGenerator stops emitting the class.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UpstreamGeneratedTypeSuppressor : DiagnosticSuppressor
{
    private const string TypeName = "IncrementalValueProviderAdditionalEx";
    private const string TypeNamespace = "Microsoft.CodeAnalysis";

    private static readonly SuppressionDescriptor Descriptor = new(
        id: "MPEPBS001",
        suppressedDiagnosticId: "CS1591",
        justification: "Microsoft.CodeAnalysis.IncrementalValueProviderAdditionalEx is emitted by MintPlayer.ValueComparerGenerator, not declared by this assembly.");

    /// <inheritdoc />
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } = ImmutableArray.Create(Descriptor);

    /// <inheritdoc />
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            var tree = diagnostic.Location.SourceTree;
            if (tree is null) continue;

            var model = context.GetSemanticModel(tree);
            var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);
            if (model.GetDeclaredSymbol(node, context.CancellationToken) is INamedTypeSymbol
                {
                    Name: TypeName,
                    ContainingNamespace: { } ns,
                } && ns.ToDisplayString() == TypeNamespace)
            {
                context.ReportSuppression(Suppression.Create(Descriptor, diagnostic));
            }
        }
    }
}
