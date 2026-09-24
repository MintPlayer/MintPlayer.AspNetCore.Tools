using System.Text.RegularExpressions;
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
/// the generated file, whether it still compiles, and whether the consumer is told when the name
/// they asked for was not the name they got.
/// </remarks>
public class EndpointMethodNameTests
{
    private static string Generated(GeneratorDriverRunResult result)
        => string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

    private static Diagnostic[] Errors(IEnumerable<Diagnostic> diagnostics)
        => [.. diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];

    [Fact]
    public void AssemblyName_DrivesBothTheMethodAndTheClassName()
    {
        var generated = Generated(EndpointGeneratorHarness.Run("MyApp.Api", FixtureSources.RawGetEndpoint));

        Assert.Contains("public static partial class MyAppApiEndpointsExtensions", generated);
        Assert.Contains("MapMyAppApiEndpoints(this global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)", generated);
    }

    [Fact]
    public void MethodNameAttribute_OverridesTheAssemblyName()
    {
        var generated = Generated(EndpointGeneratorHarness.Run(
            "MyApp.Api",
            FixtureSources.RawGetEndpoint,
            FixtureSources.MethodNameOverride("MapCustomEndpoints")));

        Assert.Contains("public static partial class CustomEndpointsExtensions", generated);
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

        Assert.Empty(Errors(diagnostics));
    }

    /// <summary>
    /// An assembly name with an empty dot-segment produces a working method, and MPEP006 says the
    /// name was adjusted.
    /// </summary>
    /// <remarks>
    /// This used to produce <b>nothing at all</b>, in complete silence. The
    /// <see cref="IndexOutOfRangeException"/> from indexing <c>s[0]</c> on an empty segment was
    /// swallowed inside the <c>ProduceCode</c> pipeline that came from MintPlayer.SourceGenerators.Tools,
    /// so there was no generated file, no diagnostic, nothing on the generator's result, and not even
    /// Roslyn's CS8785 "generator failed to generate source". The consumer's build succeeded and the
    /// only symptom was that <c>app.Map…Endpoints()</c> did not exist.
    /// <para>
    /// The sanitising is what makes it work; the diagnostic is what makes it visible. Either alone
    /// would still leave the consumer guessing why the method is not called what they expected.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("My..Api", "MapMyApiEndpoints")]
    [InlineData(".MyApi", "MapMyApiEndpoints")]
    [InlineData("MyApi.", "MapMyApiEndpoints")]
    public void AssemblyNameWithAnEmptySegment_EmitsASanitisedName_AndWarns(string assemblyName, string expected)
    {
        var result = EndpointGeneratorHarness.Run(assemblyName, FixtureSources.RawGetEndpoint);

        Assert.Contains($"{expected}(this", Generated(result));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MPEP006", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(expected, diagnostic.GetMessage());

        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile(assemblyName, FixtureSources.RawGetEndpoint)));
    }

    /// <summary>
    /// A hyphen in the assembly name is dropped rather than copied into an identifier.
    /// </summary>
    /// <remarks>
    /// A hyphenated assembly name is entirely ordinary (<c>my-app.csproj</c>), and the consumer had no
    /// way to see why the build broke: the errors were in a file they never wrote.
    /// </remarks>
    [Fact]
    public void AssemblyNameWithAHyphen_EmitsAValidIdentifier_AndWarns()
    {
        var result = EndpointGeneratorHarness.Run("My-App", FixtureSources.RawGetEndpoint);

        Assert.Contains("MapMyAppEndpoints(this", Generated(result));
        Assert.Equal("MPEP006", Assert.Single(result.Diagnostics).Id);

        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("My-App", FixtureSources.RawGetEndpoint)));
    }

    /// <summary>
    /// An override that sanitises to nothing falls back to the assembly-derived name, rather than
    /// emitting a method with no name.
    /// </summary>
    [Fact]
    public void EmptyMethodNameOverride_FallsBackToTheAssemblyName_AndWarns()
    {
        var sources = new[] { FixtureSources.RawGetEndpoint, FixtureSources.MethodNameOverride("") };

        var result = EndpointGeneratorHarness.Run("MyApp.Api", sources);

        Assert.Contains("public static partial class MyAppApiEndpointsExtensions", Generated(result));
        Assert.Contains("MapMyAppApiEndpoints(this", Generated(result));
        Assert.Equal("MPEP006", Assert.Single(result.Diagnostics).Id);

        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("MyApp.Api", sources)));
    }

    /// <summary>
    /// A method name that strips down to a shipped type's name no longer shadows it, because the
    /// generated class is emitted into a namespace only the generator writes to.
    /// </summary>
    /// <remarks>
    /// <c>"MapEndpointRouteBuilder"</c> reads like a perfectly reasonable choice and produces the
    /// class name <c>EndpointRouteBuilderExtensions</c> — which the runtime package already defines.
    /// In the library's own namespace the consumer's build stayed <b>silent</b>: source wins over
    /// metadata for a name, so the generated type shadowed the shipped one and nothing said so. (The
    /// register predicted CS0101; that only happens for two definitions in one compilation, which
    /// this is not. Silence is the harder problem, not the easier one.)
    /// <para>
    /// The call site is unaffected: the generated file emits a <c>global using</c> for its own
    /// namespace, so <c>app.MapEndpointRouteBuilder()</c> resolves without the consumer importing
    /// anything new — and the shipped <c>MapEndpoint&lt;T&gt;</c> keeps resolving to the shipped type.
    /// </para>
    /// </remarks>
    [Fact]
    public void MethodNameOverrideMatchingAShippedTypeName_DoesNotShadowIt()
    {
        // A consumer that also registers one endpoint by hand — the shipped MapEndpoint<T> lives on
        // exactly the type whose name the generated class reuses.
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

        // Same class name as the shipped type, deliberately — and a different namespace, which is
        // what makes that harmless. Asserted against the real type so this breaks if either moves.
        var shippedNamespace = typeof(EndpointRouteBuilderExtensions).Namespace!;

        Assert.Contains($"public static partial class {typeof(EndpointRouteBuilderExtensions).Name}", generated);
        Assert.Contains($"namespace {shippedNamespace}.Generated", generated);
        Assert.Contains($"global using global::{shippedNamespace}.Generated;", generated);

        // The library's own namespace is never declared — only namespaces below it, and the
        // consumer's own for the partial base classes.
        Assert.DoesNotMatch($@"namespace {Regex.Escape(shippedNamespace)}\s*[\r\n{{]", generated);

        var diagnostics = EndpointGeneratorHarness.RunAndCompile("MyApp.Api", sources);

        Assert.Empty(diagnostics.Where(d =>
            d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
    }
}
