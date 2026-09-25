using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.AspNetCore.Endpoints.Generator;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;

/// <summary>
/// Runs <see cref="EndpointGenerator"/> in-process over source strings and hands back both the
/// generator's result and the ability to compile what it emitted.
/// </summary>
/// <remarks>
/// <para>
/// In-process is not an implementation detail, it is the only option. The generator normally runs
/// inside <c>csc</c> as an analyzer, and an analyzer execution is not observed by the test host's
/// profiler — so end-to-end tests over the sample app contribute exactly zero generator coverage.
/// Every covered line in the generator assembly comes from this harness.
/// </para>
/// <para>
/// A raw driver is used rather than snapshot testing. It makes the single most valuable assertion
/// — "does the emitted code actually compile" — three lines instead of impossible, and the emitted
/// order is now fully sorted, so the assertions that do care about order can name it exactly.
/// </para>
/// </remarks>
internal static class EndpointGeneratorHarness
{
    /// <summary>
    /// Metadata references for the fixture compilations, taken from this test host's own
    /// trusted-platform-assemblies list.
    /// </summary>
    /// <remarks>
    /// Basic.Reference.Assemblies is deliberately not used: it ships no ASP.NET Core reference
    /// assemblies, and every fixture here needs <c>HttpContext</c>, <c>IResult</c>,
    /// <c>RouteHandlerBuilder</c> and <c>IEndpointRouteBuilder</c>. Because this project carries
    /// <c>FrameworkReference Microsoft.AspNetCore.App</c> plus project references to the endpoint
    /// libraries, the host's own reference set already contains everything a fixture can name.
    /// </remarks>
    private static readonly Lazy<ImmutableArray<MetadataReference>> allReferences = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray());

    /// <summary>
    /// The host's references minus the OpenAPI assemblies — the default consumer.
    /// </summary>
    /// <remarks>
    /// This test host references <c>Microsoft.AspNetCore.OpenApi</c> so the OpenAPI emission tests
    /// can compile against it. Handing it to every fixture would switch <c>EndpointOpenApi.g.cs</c>
    /// on everywhere and leave the no-package consumer — the default — untested, so it is opt-in.
    /// </remarks>
    private static readonly Lazy<ImmutableArray<MetadataReference>> references = new(() =>
        [.. allReferences.Value.Where(reference => !IsOpenApiAssembly(reference))]);

    /// <summary>The default consumer's reference set, for harnesses other than this one (the code-fix tests).</summary>
    internal static ImmutableArray<MetadataReference> DefaultReferences => references.Value;

    private static bool IsOpenApiAssembly(MetadataReference reference) =>
        Path.GetFileName(reference.Display) is "Microsoft.AspNetCore.OpenApi.dll" or "Microsoft.OpenApi.dll";

    /// <summary>
    /// Compiles <paramref name="sources"/> and runs the generator over the result.
    /// </summary>
    /// <param name="assemblyName">
    /// Deliberately explicit: it drives <c>AssemblyInfo.GetMethodName()</c>, so it is an input
    /// under test rather than incidental.
    /// </param>
    public static GeneratorDriverRunResult Run(string assemblyName, params string[] sources)
    {
        var driver = CSharpGeneratorDriver
            .Create(new EndpointGenerator())
            .RunGenerators(CreateCompilation(assemblyName, sources));

        return driver.GetRunResult();
    }

    /// <summary>
    /// Runs the generator, then compiles the original sources together with everything it emitted,
    /// and returns the resulting diagnostics.
    /// </summary>
    /// <remarks>
    /// This is what catches a malformed emit — an unbalanced brace, an invalid identifier, a wrong
    /// base class. No substring assertion does.
    /// </remarks>
    public static ImmutableArray<Diagnostic> RunAndCompile(string assemblyName, params string[] sources)
        => RunAndCompile(CreateCompilation(assemblyName, sources));

    /// <inheritdoc cref="RunAndCompile(string, string[])"/>
    public static ImmutableArray<Diagnostic> RunAndCompile(CSharpCompilation compilation)
    {
        CSharpGeneratorDriver
            .Create(new EndpointGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var driverDiagnostics);

        return [.. driverDiagnostics, .. updated.GetDiagnostics()];
    }

    /// <summary>
    /// Runs the generator, compiles the result and loads the emitted assembly into this process.
    /// </summary>
    /// <remarks>
    /// Some of the generator's contract is only observable by executing what it emitted: the
    /// resolved route of a grouped endpoint, the status code that reaches
    /// <c>Produces&lt;TResponse&gt;</c>, the contents of the descriptor list. All three go through
    /// static abstract interface members, which no amount of text matching can resolve.
    /// <para>
    /// Each caller must pass a distinct <paramref name="assemblyName"/>: loading two different
    /// images under the same simple name leaves two unrelated <see cref="Type"/> identities in the
    /// process, which produces cast failures that look nothing like their cause.
    /// </para>
    /// </remarks>
    public static Assembly RunAndLoad(string assemblyName, params string[] sources)
        => RunAndLoad(CreateCompilation(assemblyName, sources));

    /// <inheritdoc cref="RunAndLoad(string, string[])"/>
    public static Assembly RunAndLoad(CSharpCompilation compilation)
    {
        var assemblyName = compilation.AssemblyName;
        CSharpGeneratorDriver
            .Create(new EndpointGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        using var stream = new MemoryStream();
        var emitResult = updated.Emit(stream);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
            throw new InvalidOperationException(
                $"Fixture assembly '{assemblyName}' did not emit: {string.Join("; ", errors)}");
        }

        // RS1035 bans Assembly.Load for analyzers. This project is a test host, not an analyzer —
        // the ban rides in with the Microsoft.CodeAnalysis package reference and does not apply.
#pragma warning disable RS1035
        return Assembly.Load(stream.ToArray());
#pragma warning restore RS1035
    }

    /// <summary>
    /// A driver that records incremental step reasons, for the caching tests.
    /// </summary>
    /// <remarks>
    /// <c>IncrementalGeneratorOutputKind.None</c> disables nothing — the source output still has to
    /// run, otherwise there are no output steps left to assert about.
    /// </remarks>
    public static GeneratorDriver CreateTrackingDriver()
        => CSharpGeneratorDriver.Create(
            [new EndpointGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

    /// <summary>
    /// The global usings that <c>Microsoft.NET.Sdk.Web</c> injects into every file when
    /// <c>ImplicitUsings</c> is enabled.
    /// </summary>
    /// <remarks>
    /// These model the ordinary consumer, which is a Web SDK project. The generated code no longer
    /// depends on them — it emits the using directives its own extension-method calls need — and
    /// <c>GeneratedCodeUsingsTests</c> compiles without them to keep it that way.
    /// </remarks>
    public const string WebSdkImplicitUsings = """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        global using global::Microsoft.AspNetCore.Builder;
        global using global::Microsoft.AspNetCore.Hosting;
        global using global::Microsoft.AspNetCore.Http;
        global using global::Microsoft.AspNetCore.Routing;
        global using global::Microsoft.Extensions.Configuration;
        global using global::Microsoft.Extensions.DependencyInjection;
        global using global::Microsoft.Extensions.Hosting;
        global using global::Microsoft.Extensions.Logging;
        """;

    /// <summary>
    /// Creates a fixture compilation that mirrors a real <c>Microsoft.NET.Sdk.Web</c> consumer.
    /// </summary>
    /// <param name="includeImplicitUsings">
    /// Pass <see langword="false"/> to model a plain <c>Microsoft.NET.Sdk</c> consumer, or one with
    /// implicit usings disabled. Only D-G25's pinning test needs that.
    /// </param>
    /// <param name="includeOpenApi">
    /// Pass <see langword="true"/> to model a consumer that references
    /// <c>Microsoft.AspNetCore.OpenApi</c>, which is what makes the generator emit
    /// <c>EndpointOpenApi.g.cs</c>.
    /// </param>
    /// <param name="includeValidation">
    /// Pass <see langword="false"/> to model a consumer whose compilation cannot resolve
    /// <c>Microsoft.Extensions.Validation</c> at all, which MPEP015 must stay silent for.
    /// </param>
    public static CSharpCompilation CreateCompilation(
        string assemblyName,
        string[] sources,
        bool includeImplicitUsings = true,
        bool includeOpenApi = false,
        bool includeValidation = true)
    {
        // LanguageVersion.Latest is required, not cosmetic: the fixtures use collection
        // expressions (C# 12) and static abstract interface members (C# 11).
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        var allSources = includeImplicitUsings
            ? sources.Prepend(WebSdkImplicitUsings)
            : sources;

        // Distinct file paths, not decoration: the generator's diagnostics carry a location, and a
        // location is resolved back to its tree by path. Every tree sharing the default empty path
        // makes each diagnostic land in whichever tree happens to be first.
        return CSharpCompilation.Create(
            assemblyName,
            allSources.Select((source, index) => CSharpSyntaxTree.ParseText(source, parseOptions, path: $"Source{index}.cs")),
            (includeOpenApi ? allReferences.Value : references.Value)
                .Where(reference => includeValidation || Path.GetFileName(reference.Display) != "Microsoft.Extensions.Validation.dll"),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }
}
