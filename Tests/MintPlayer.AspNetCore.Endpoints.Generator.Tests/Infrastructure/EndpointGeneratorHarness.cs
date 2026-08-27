using System.Collections.Immutable;
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
/// A raw driver is used rather than snapshot testing. The generator's output order is currently
/// nondeterministic (root groups come from iterating a <c>HashSet&lt;string&gt;</c> and nothing is
/// sorted), so snapshots would produce red builds that are not bugs. It also makes the single
/// most valuable assertion — "does the emitted code actually compile" — three lines instead of
/// impossible.
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
    private static readonly Lazy<ImmutableArray<MetadataReference>> references = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray());

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
    /// The global usings that <c>Microsoft.NET.Sdk.Web</c> injects into every file when
    /// <c>ImplicitUsings</c> is enabled.
    /// </summary>
    /// <remarks>
    /// These are part of the harness rather than of each fixture because the generated code
    /// <b>depends on them</b>: it fully qualifies types with <c>global::</c> but emits no using
    /// directives of its own, while calling the extension methods <c>MapMethods</c>,
    /// <c>MapGroup</c> and <c>Produces</c> — and an extension-method invocation cannot be
    /// resolved from a <c>global::</c> type name, it needs the namespace in scope.
    /// <para>
    /// So the emitted code only compiles inside a project that happens to be
    /// <c>Microsoft.NET.Sdk.Web</c> with implicit usings on. That is defect D-G25; see
    /// <c>GeneratedCodeUsingsTests</c>, which pins it by compiling without these.
    /// </para>
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
    public static CSharpCompilation CreateCompilation(
        string assemblyName,
        string[] sources,
        bool includeImplicitUsings = true)
    {
        // LanguageVersion.Latest is required, not cosmetic: the fixtures use collection
        // expressions (C# 12) and static abstract interface members (C# 11).
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        var allSources = includeImplicitUsings
            ? sources.Prepend(WebSdkImplicitUsings)
            : sources;

        return CSharpCompilation.Create(
            assemblyName,
            allSources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions)),
            references.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }
}
