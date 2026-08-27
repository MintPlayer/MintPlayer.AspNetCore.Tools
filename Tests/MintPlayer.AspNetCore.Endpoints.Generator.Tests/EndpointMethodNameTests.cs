using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// The naming rule as the consumer experiences it: through a compilation, with the assembly name
/// and <c>[assembly: EndpointsMethodName]</c> as the only inputs.
/// </summary>
/// <remarks>
/// <see cref="AssemblyInfoTests"/> covers the string function; these tests are about what reaches
/// the generated file and whether it still compiles, which is where the unsanitised inputs actually
/// hurt.
/// </remarks>
public class EndpointMethodNameTests
{
    private static string Generated(GeneratorDriverRunResult result)
        => string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

    [Fact]
    public void AssemblyName_DrivesBothTheMethodAndTheClassName()
    {
        var generated = Generated(EndpointGeneratorHarness.Run("MyApp.Api", FixtureSources.RawGetEndpoint));

        Assert.Contains("public static class MyAppApiEndpointsExtensions", generated);
        Assert.Contains("MapMyAppApiEndpoints(this global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)", generated);
    }

    [Fact]
    public void MethodNameAttribute_OverridesTheAssemblyName()
    {
        var generated = Generated(EndpointGeneratorHarness.Run(
            "MyApp.Api",
            FixtureSources.RawGetEndpoint,
            FixtureSources.MethodNameOverride("MapCustomEndpoints")));

        Assert.Contains("public static class CustomEndpointsExtensions", generated);
        Assert.Contains("MapCustomEndpoints(this", generated);
        Assert.DoesNotContain("MapMyAppApiEndpoints", generated);
    }

    [Fact]
    public void GeneratedCode_WithAnOverriddenMethodName_Compiles()
    {
        var diagnostics = EndpointGeneratorHarness.RunAndCompile(
            "MyApp.Api",
            FixtureSources.RawGetEndpoint,
            FixtureSources.MethodNameOverride("MapCustomEndpoints"));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Pins D-G12: an assembly name with an empty dot-segment makes the generator produce nothing at
    /// all, in complete silence.
    /// </summary>
    /// <remarks>
    /// <see cref="AssemblyInfoTests.GetMethodName_EmptySegment_Throws_KnownBug"/> shows the
    /// <see cref="IndexOutOfRangeException"/> that starts this. What is measurable here is where it
    /// ends up: <b>nowhere</b>. No generated file, no diagnostic, and nothing on the generator's own
    /// result either — the throw is swallowed inside the <c>ProduceCode</c> pipeline that comes from
    /// MintPlayer.SourceGenerators.Tools, so not even Roslyn's usual CS8785 "generator failed to
    /// generate source" is reported.
    /// <para>
    /// That is worse than a crash: the consumer's build succeeds, and the only symptom is that
    /// <c>app.Map…Endpoints()</c> does not exist. <c>"My..Api"</c> is unusual but legal, and a
    /// trailing dot arrives the same way from a mistyped <c>&lt;AssemblyName&gt;</c>.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("My..Api")]
    [InlineData(".MyApi")]
    [InlineData("MyApi.")]
    public void AssemblyNameWithAnEmptySegment_SilentlyProducesNothing_KnownBug(string assemblyName)
    {
        var result = EndpointGeneratorHarness.Run(assemblyName, FixtureSources.RawGetEndpoint);

        var generatorResult = Assert.Single(result.Results);
        Assert.Empty(generatorResult.GeneratedSources);
        Assert.Empty(result.GeneratedTrees);
        Assert.Empty(result.Diagnostics);
        Assert.Null(generatorResult.Exception);
    }

    /// <summary>
    /// Pins D-G12: characters that are legal in an assembly name but not in an identifier are copied
    /// into the emitted method name, producing generated code that cannot compile.
    /// </summary>
    /// <remarks>
    /// A hyphenated assembly name is entirely ordinary (<c>my-app.csproj</c>), and the consumer has
    /// no way to see why the build broke — the errors are in a file they never wrote.
    /// </remarks>
    [Fact]
    public void AssemblyNameWithAHyphen_EmitsAnInvalidIdentifier_KnownBug()
    {
        var result = EndpointGeneratorHarness.Run("My-App", FixtureSources.RawGetEndpoint);
        Assert.Contains("MapMy-AppEndpoints", Generated(result));

        var errors = EndpointGeneratorHarness
            .RunAndCompile("My-App", FixtureSources.RawGetEndpoint)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.Contains("EndpointMapping.g.cs", error.Location.GetLineSpan().Path));
    }

    /// <summary>
    /// Pins D-G12: an empty override is taken literally and emits a method with no name.
    /// </summary>
    [Fact]
    public void EmptyMethodNameOverride_EmitsANamelessMethod_KnownBug()
    {
        var result = EndpointGeneratorHarness.Run(
            "MyApp.Api",
            FixtureSources.RawGetEndpoint,
            FixtureSources.MethodNameOverride(""));

        var generated = Generated(result);
        Assert.Contains("public static class Extensions", generated);
        Assert.Contains("IEndpointRouteBuilder (this global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)", generated);

        var errors = EndpointGeneratorHarness
            .RunAndCompile("MyApp.Api", FixtureSources.RawGetEndpoint, FixtureSources.MethodNameOverride(""))
            .Where(d => d.Severity == DiagnosticSeverity.Error);

        Assert.NotEmpty(errors);
    }

    /// <summary>
    /// Pins D-G13: the generated class always lands in the library's own namespace, so a method name
    /// that strips down to a shipped type's name collides with it.
    /// </summary>
    /// <remarks>
    /// <c>"MapEndpointRouteBuilder"</c> reads like a perfectly reasonable choice and produces
    /// <c>MintPlayer.AspNetCore.Endpoints.EndpointRouteBuilderExtensions</c> — which the runtime
    /// package already defines.
    /// <para>
    /// Measured, the consumer's own build stays <b>silent</b>: source wins over metadata for that
    /// name, so the generated type shadows the shipped one and even a call to the shipped
    /// <c>MapEndpoint&lt;T&gt;</c> still resolves. (The register predicts CS0101; that only happens
    /// for two definitions in the same compilation, which this is not.) Silence is the problem —
    /// nothing tells the consumer that a public type of the package is now hidden behind a generated
    /// one, and anything that later references both assemblies inherits the ambiguity. Emitting into
    /// the consumer's own namespace would define the collision out of existence.
    /// </para>
    /// </remarks>
    [Fact]
    public void MethodNameOverrideCollidingWithAShippedType_SilentlyShadowsIt_KnownBug()
    {
        // A consumer that also registers one endpoint by hand — the shipped MapEndpoint<T> lives on
        // exactly the type the generated class is about to shadow.
        const string consumer = """
            using Microsoft.AspNetCore.Routing;
            using MintPlayer.AspNetCore.Endpoints;

            namespace Fixtures;

            public static class Startup
            {
                public static void Configure(IEndpointRouteBuilder app)
                {
                    app.MapEndpointRouteBuilder();
                    app.MapEndpoint<HealthCheck>();
                }
            }
            """;

        var sources = new[]
        {
            FixtureSources.RawGetEndpoint,
            FixtureSources.MethodNameOverride("MapEndpointRouteBuilder"),
            consumer,
        };

        var generated = Generated(EndpointGeneratorHarness.Run("MyApp.Api", sources));

        // Same namespace, same name as the shipped type — asserted against the real type so this
        // test breaks if either side is ever renamed.
        Assert.Contains(
            $"namespace {typeof(EndpointRouteBuilderExtensions).Namespace}",
            generated);
        Assert.Contains(
            $"public static class {typeof(EndpointRouteBuilderExtensions).Name}",
            generated);

        var diagnostics = EndpointGeneratorHarness.RunAndCompile("MyApp.Api", sources);

        Assert.Empty(diagnostics.Where(d =>
            d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
    }
}
