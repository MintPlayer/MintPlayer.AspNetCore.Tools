using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// The incremental contract: a rerun over an unchanged compilation, or over one whose only change is
/// inside a method body, must not rebuild the model.
/// </summary>
/// <remarks>
/// <para>
/// This is what every hand-written <see cref="IEquatable{T}"/> in <c>Models.cs</c> exists for, and it
/// was entirely unverified — the models were correct and bought nothing, because the source output
/// sat downstream of something that compared unequal on every compilation.
/// </para>
/// <para>
/// The cause was <c>GeneratorExtensions.ProduceCode</c> in MintPlayer.SourceGenerators.Tools: it
/// registers the source output on <c>CompilationProvider.Combine(producer)</c>. A
/// <see cref="Compilation"/> is a fresh object with no value equality — <c>Clone()</c> alone
/// guarantees inequality — and the producer was a freshly allocated object too, so the output node
/// could never be cached however well the models compared. The source output is now registered on
/// the value-equal model directly, and every provider carries a
/// <c>WithTrackingName</c> so the answer is localisable from a test either way.
/// </para>
/// <para>
/// Diagnostics still go through the Tools pipeline, because turning a stored location key back into
/// a <c>Location</c> needs the current <c>Compilation</c>. So the assertions here are on the named
/// model steps rather than on the raw output steps, which mix the two registrations together.
/// </para>
/// </remarks>
public class EndpointGeneratorIncrementalTests
{
    private const string TrackedModelStep = "EndpointModel";

    private static IncrementalStepRunReason[] ReasonsFor(GeneratorDriverRunResult result, string stepName)
        => [.. result.Results
            .SelectMany(generatorResult => generatorResult.TrackedSteps)
            .Where(step => step.Key == stepName)
            .SelectMany(step => step.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)];

    private static string Text(GeneratorDriverRunResult result)
        => string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

    /// <summary>
    /// An identical compilation does not rebuild the model.
    /// </summary>
    /// <remarks>
    /// <c>Compilation.Clone()</c> is a distinct object with identical content — the mildest possible
    /// change, and the same shape as a keystroke in a file containing no endpoints. Every model
    /// derived from it compares equal, so the model step must report <c>Unchanged</c> or
    /// <c>Cached</c>. It reported <c>Modified</c>, meaning the whole producer ran again.
    /// </remarks>
    [Fact]
    public void RerunOverAnIdenticalCompilation_DoesNotRebuildTheModel()
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", [FixtureSources.Corpus]);

        var driver = EndpointGeneratorHarness.CreateTrackingDriver().RunGenerators(compilation);
        var second = driver.RunGenerators(compilation.Clone()).GetRunResult();

        var reasons = ReasonsFor(second, TrackedModelStep);

        Assert.NotEmpty(reasons);
        Assert.All(reasons, reason =>
            Assert.True(
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"expected the model step to be cached, was {reason}"));
    }

    /// <summary>
    /// Editing a handler body does not rebuild the model either — the case that matters most, since
    /// it is what a developer does all day in the IDE.
    /// </summary>
    [Fact]
    public void EditingAHandlerBody_DoesNotRebuildTheModel()
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

        var reasons = ReasonsFor(driver.RunGenerators(edited).GetRunResult(), TrackedModelStep);

        Assert.NotEmpty(reasons);
        Assert.All(reasons, reason =>
            Assert.True(
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"expected the model step to be cached, was {reason}"));
    }

    /// <summary>
    /// A change that <i>does</i> affect the model rebuilds it, so the caching is not simply stuck.
    /// </summary>
    /// <remarks>
    /// Without this, a comparer that returned <c>true</c> unconditionally would satisfy both tests
    /// above and ship a generator that never notices a new endpoint.
    /// </remarks>
    [Fact]
    public void AddingAnEndpoint_RebuildsTheModel()
    {
        const string before = """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using MintPlayer.AspNetCore.Endpoints;

            namespace Fixtures;

            public class HealthCheck : IGetEndpoint
            {
                public static string Path => "/health";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        var after = before.Replace(
            "public class HealthCheck",
            """
            public class ReadyCheck : IGetEndpoint
            {
                public static string Path => "/ready";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }

            public class HealthCheck
            """);

        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", [before]);
        var driver = EndpointGeneratorHarness.CreateTrackingDriver().RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Last(),
            EndpointGeneratorHarness.CreateCompilation("Fixtures", [after]).SyntaxTrees.Last());

        var result = driver.RunGenerators(edited).GetRunResult();

        Assert.Contains(IncrementalStepRunReason.Modified, ReasonsFor(result, TrackedModelStep));
        Assert.Contains("global::Fixtures.ReadyCheck", Text(result));
    }

    /// <summary>
    /// Whatever the caching does, the emitted text must not drift: the producer is handed equal
    /// models, so it has nothing legitimate to vary on.
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

    /// <summary>
    /// Every provider is named, which is what makes a caching regression diagnosable rather than
    /// merely observable.
    /// </summary>
    /// <remarks>
    /// With only <c>Compilation</c> and <c>SourceOutput</c> tracked, a test could see that the output
    /// re-ran and had no way to say which step upstream caused it.
    /// </remarks>
    [Theory]
    [InlineData("Endpoints")]
    [InlineData("Groups")]
    [InlineData("AssemblyInfo")]
    [InlineData(TrackedModelStep)]
    public void EveryProvider_IsTracked(string stepName)
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", [FixtureSources.Corpus]);

        var result = EndpointGeneratorHarness.CreateTrackingDriver().RunGenerators(compilation).GetRunResult();

        Assert.NotEmpty(ReasonsFor(result, stepName));
    }
}
