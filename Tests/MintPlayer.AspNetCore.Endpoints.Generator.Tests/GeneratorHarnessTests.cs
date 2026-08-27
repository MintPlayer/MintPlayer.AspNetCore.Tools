using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// Proves the driver harness itself works before any behaviour is asserted through it.
/// </summary>
/// <remarks>
/// The risky parts are the reference set (Basic.Reference.Assemblies would silently lack every
/// ASP.NET Core type the fixtures name) and loading MintPlayer.SourceGenerators.Tools, which the
/// generator references with <c>PrivateAssets="all"</c> so it does not flow transitively. Both
/// fail in ways that look like generator bugs rather than harness bugs.
/// </remarks>
public class GeneratorHarnessTests
{
    private const string RawGetEndpoint = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;

        public class HealthCheckEndpoint : IGetEndpoint
        {
            public static string Path => "/health";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }
        """;

    /// <summary>
    /// The fixture corpus must compile on its own, or every later failure is ambiguous between
    /// "the generator is wrong" and "the fixture does not build".
    /// </summary>
    [Fact]
    public void Harness_FixtureSourceCompiles_WithoutTheGenerator()
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", [RawGetEndpoint]);

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(errors);
    }

    /// <summary>
    /// The generator must not throw. Roslyn swallows generator exceptions into
    /// <c>Results[].Exception</c> and reports CS8785, so a crashing generator otherwise shows up
    /// as a confusing build warning rather than a test failure.
    /// </summary>
    [Fact]
    public void Harness_GeneratorRuns_WithoutThrowing()
    {
        var result = EndpointGeneratorHarness.Run("Fixtures", RawGetEndpoint);

        var generatorResult = Assert.Single(result.Results);
        Assert.Null(generatorResult.Exception);
    }

    [Fact]
    public void Harness_GeneratorEmitsEndpointMappingFile()
    {
        var result = EndpointGeneratorHarness.Run("Fixtures", RawGetEndpoint);

        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("EndpointMapping.g.cs", StringComparison.Ordinal));
    }

    /// <summary>
    /// The highest-value assertion in the whole generator suite: the emitted code compiles.
    /// </summary>
    [Fact]
    public void Harness_EmittedCodeCompiles_WithNoErrors()
    {
        var diagnostics = EndpointGeneratorHarness.RunAndCompile("Fixtures", RawGetEndpoint);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(errors);
    }

    /// <summary>
    /// The generated extension method name is derived from the assembly name, so the harness must
    /// really be passing it through.
    /// </summary>
    [Fact]
    public void Harness_AssemblyNameDrivesGeneratedMethodName()
    {
        var result = EndpointGeneratorHarness.Run("MyApp.Api", RawGetEndpoint);

        var generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));

        Assert.Contains("MapMyAppApiEndpoints", generated);
    }
}
