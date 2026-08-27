using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// The incremental contract: a rerun over an unchanged compilation should not re-produce source.
/// It currently does.
/// </summary>
/// <remarks>
/// <para>
/// This is what every hand-written <c>IEquatable</c> in <c>Models.cs</c> exists for, and it was
/// entirely unverified — so the models are correct (see <see cref="EndpointInfoTests"/> and
/// <see cref="AssemblyInfoTests"/>) and buy nothing, because the source-output step sits downstream
/// of something that compares unequal on every compilation.
/// </para>
/// <para>
/// The only tracked steps are <c>Compilation</c> and <c>SourceOutput</c> — the generator names none
/// of its own providers with <c>WithTrackingName</c>, which is also why the cause cannot be
/// localised further from here. What is measurable is the outcome: <c>SourceOutput</c> comes back
/// <see cref="IncrementalStepRunReason.Modified"/>, meaning the whole producer ran again and its
/// text was re-emitted.
/// </para>
/// </remarks>
public class EndpointGeneratorIncrementalTests
{
    private static IncrementalStepRunReason[] OutputReasons(GeneratorDriverRunResult result)
        => [.. result.Results
            .SelectMany(generatorResult => generatorResult.TrackedOutputSteps)
            .SelectMany(step => step.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)];

    /// <summary>
    /// Pins the gap: an identical compilation still re-produces source.
    /// </summary>
    /// <remarks>
    /// <c>Compilation.Clone()</c> is a distinct object with identical content, which is the mildest
    /// possible change — the same shape as a keystroke in a file containing no endpoints. Every model
    /// derived from it compares equal, so a correctly-shaped pipeline would report <c>Cached</c> or
    /// <c>Unchanged</c> here and skip the producer entirely.
    /// </remarks>
    [Fact]
    public void RerunOverAnIdenticalCompilation_StillReproducesSource_KnownGap()
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", [FixtureSources.Corpus]);

        var driver = EndpointGeneratorHarness.CreateTrackingDriver().RunGenerators(compilation);
        var second = driver.RunGenerators(compilation.Clone()).GetRunResult();

        var reasons = OutputReasons(second);

        Assert.NotEmpty(reasons);
        Assert.All(reasons, reason => Assert.Equal(IncrementalStepRunReason.Modified, reason));
    }

    /// <summary>
    /// Pins the gap for the case that matters most: editing a handler body changes no model, and is
    /// what a developer does all day in the IDE.
    /// </summary>
    [Fact]
    public void EditingAHandlerBody_StillReproducesSource_KnownGap()
    {
        const string before = """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using MintPlayer.AspNetCore.Endpoints;

            namespace Fixtures;

            public class HealthCheck : IGetEndpoint
            {
                public static string Path => "/health";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok("up"));
            }
            """;

        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", [before]);
        var driver = EndpointGeneratorHarness.CreateTrackingDriver().RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Last(),
            EndpointGeneratorHarness
                .CreateCompilation("Fixtures", [before.Replace("\"up\"", "\"healthy\"")])
                .SyntaxTrees
                .Last());

        var reasons = OutputReasons(driver.RunGenerators(edited).GetRunResult());

        Assert.NotEmpty(reasons);
        Assert.All(reasons, reason => Assert.Equal(IncrementalStepRunReason.Modified, reason));
    }

    /// <summary>
    /// Whatever the caching does, the emitted text must not drift: the producer is handed equal
    /// models, so it has nothing legitimate to vary on. This is the assertion that stays true after
    /// the caching gap is closed.
    /// </summary>
    [Fact]
    public void RerunOverAnIdenticalCompilation_EmitsIdenticalSource()
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", [FixtureSources.Corpus]);
        var driver = EndpointGeneratorHarness.CreateTrackingDriver();

        var first = driver.RunGenerators(compilation).GetRunResult();
        var second = driver.RunGenerators(compilation.Clone()).GetRunResult();

        Assert.Equal(Text(first), Text(second));
    }

    /// <summary>
    /// A handler-body edit must not change the generated text either — the routes, the base classes
    /// and the descriptors are all derived from declarations, not from bodies.
    /// </summary>
    [Fact]
    public void EditingAHandlerBody_EmitsIdenticalSource()
    {
        var before = FixtureSources.Corpus;
        var after = before.Replace("new UserResponse(request.Id, \"Alice\")", "new UserResponse(request.Id, \"Bob\")");
        Assert.NotEqual(before, after);

        var first = EndpointGeneratorHarness.Run("Fixtures", before);
        var second = EndpointGeneratorHarness.Run("Fixtures", after);

        Assert.Equal(Text(first), Text(second));
    }

    private static string Text(GeneratorDriverRunResult result)
        => string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));
}
