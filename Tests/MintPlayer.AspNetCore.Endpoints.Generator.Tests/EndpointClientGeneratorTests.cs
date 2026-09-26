using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

// RS1035 bans file IO in analyzers. This is a test host, not an analyzer — it only inherits the
// rule from MintPlayer.SourceGenerators.Tools' build props — and comparing the emitted URL helper
// with the runtime library's source file on disk is the point of one test here.
#pragma warning disable RS1035

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// M9: the server writes <c>[assembly: EndpointContract(…)]</c>, and a client project that references
/// the server assembly metadata-only — with no ASP.NET Core of its own — generates a typed client
/// from it (PRD R7.4, R7.4a).
/// </summary>
/// <remarks>
/// Every server here is compiled with the real <see cref="EndpointGenerator"/> and emitted to an
/// image, and the client sees only that image: the same shape as the client project's
/// <c>Private=false</c> reference, with no source access to the endpoints. The client compilation's
/// references are the .NET shared framework alone, so a generated line that needed ASP.NET Core
/// would not compile.
/// </remarks>
public class EndpointClientGeneratorTests
{
    // ---------- fixtures ----------

    private const string ContractsSource = """
        namespace Shop.Contracts;

        public record ProductResponse(int Id, string Name);
        public record CreateProductRequest(string Name);
        """;

    private const string ServerSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;
        using Shop.Contracts;

        namespace Shop.Api;

        public class ProductsApi : IEndpointGroup
        {
            public static string Prefix => "/api/products";
        }

        [MemberOf<ProductsApi>]
        public partial class GetProduct : IGetEndpoint<ProductResponse>
        {
            public static string Path => "/{id}";
            [RouteParam] public int Id { get; set; }
            public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok(new ProductResponse(Id, "x")));
        }

        [MemberOf<ProductsApi>]
        public partial class CreateProduct : IPostEndpoint<CreateProductRequest, ProductResponse>
        {
            public static string Path => "/";
            public override Task<IResult> HandleAsync(CreateProductRequest request, CancellationToken ct) => Task.FromResult(Results.Ok(new ProductResponse(1, request.Name)));
        }

        [MemberOf<ProductsApi>]
        public partial class SearchProducts : IGetEndpoint
        {
            public static string Path => "/search/{term?}";
            [QueryParam] public int? Page { get; set; }
            [QueryParam("class")] public string? Category { get; set; }
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }

        public class Preflight : IEndpoint
        {
            public static string Path => "/api/{**path}";
            public static System.Collections.Generic.IEnumerable<string> Methods => ["OPTIONS", "HEAD"];
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }

        public class Computed : IEndpoint
        {
            public static string Path => "/api/" + System.DateTime.Now.Year;
            public static System.Collections.Generic.IEnumerable<string> Methods => ["GET"];
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }
        """;

    private const string ConsumerSource = """
        using System.Net.Http;
        using System.Threading.Tasks;

        namespace Shop.Client;

        public static class Consumer
        {
            public static async Task<string?> Run(HttpClient http)
            {
                var client = new ApiClient(http);
                var product = await client.GetProductAsync(42);
                var created = await client.CreateProductAsync(new Shop.Contracts.CreateProductRequest("n"));
                using var search = await client.SearchProductsAsync(term: "a b", page: 2, @class: "c");
                using var head = await client.PreflightHeadAsync();
                using var options = await client.PreflightOptionsAsync(path: "x/y");
                return product?.Name + created?.Name;
            }
        }
        """;

    // ---------- helpers ----------

    /// <summary>The .NET shared framework only — what a Blazor WebAssembly or console client has.</summary>
    private static readonly Lazy<ImmutableArray<MetadataReference>> clientFramework = new(() =>
    {
        var frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(Path.GetDirectoryName(path), frameworkDirectory, StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))];
    });

    private static MetadataReference Emit(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static MetadataReference Contracts() =>
        Emit(CSharpCompilation.Create(
            "Shop.Contracts",
            [CSharpSyntaxTree.ParseText(ContractsSource, new CSharpParseOptions(LanguageVersion.Latest))],
            clientFramework.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)));

    /// <summary>Compiles a server with the endpoint generator, and returns its generated text and image.</summary>
    private static (string Generated, MetadataReference Image) Server(string assemblyName, string source, params MetadataReference[] extra)
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation(assemblyName, [source]).AddReferences(extra);
        CSharpGeneratorDriver
            .Create(new EndpointGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", updated.SyntaxTrees.Skip(compilation.SyntaxTrees.Length).Select(tree => tree.ToString()));
        return (generated, Emit(updated));
    }

    private static CSharpCompilation Client(IEnumerable<MetadataReference> references, params string[] sources) =>
        CSharpCompilation.Create(
            "Shop.Client",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: $"Client{index}.cs")),
            clientFramework.Value.AddRange(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    private static GeneratorDriver ClientDriver(bool optIn = true, bool track = false) =>
        CSharpGeneratorDriver.Create(
            [new EndpointClientGenerator().AsSourceGenerator(), new EndpointGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            optionsProvider: new OptionsProvider(optIn ? new Dictionary<string, string> { ["build_property.GenerateEndpointsClient"] = "true" } : []),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: track));

    private sealed record ClientRun(GeneratorDriverRunResult Result, ImmutableArray<Diagnostic> CompileDiagnostics)
    {
        public string Text => string.Join("\n", Result.GeneratedTrees.Select(tree => tree.ToString()));
        public string File(string name) => Result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith(name, StringComparison.Ordinal)).ToString();
        public Diagnostic[] Errors => [.. CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];
    }

    private static ClientRun RunClient(CSharpCompilation compilation, bool optIn = true)
    {
        var driver = ClientDriver(optIn).RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var driverDiagnostics);
        return new ClientRun(driver.GetRunResult(), [.. driverDiagnostics, .. updated.GetDiagnostics()]);
    }

    // ---------- the server side ----------

    [Fact]
    public void Server_EmitsOneContractPerLinkableEndpoint_WithRouteQueryAndBodyTypes()
    {
        var (generated, _) = Server("Shop.Api", ServerSource, Contracts());

        Assert.Contains(
            "[assembly: global::MintPlayer.AspNetCore.Endpoints.Generated.EndpointContractAttribute(typeof(global::Shop.Api.GetProduct), \"GetProduct\", \"/api/products/{id}\", new string[] { \"GET\" }, Version = 1, ResponseType = typeof(global::Shop.Contracts.ProductResponse), RouteParameterNames = new string[] { \"id\" }, RouteParameterTypes = new global::System.Type[] { typeof(int) })]",
            generated);
        Assert.Contains(
            "(typeof(global::Shop.Api.CreateProduct), \"CreateProduct\", \"/api/products/\", new string[] { \"POST\" }, Version = 1, RequestType = typeof(global::Shop.Contracts.CreateProductRequest), ResponseType = typeof(global::Shop.Contracts.ProductResponse))]",
            generated);
        Assert.Contains(
            "\"/api/products/search/{term?}\", new string[] { \"GET\" }, Version = 1, RouteParameterNames = new string[] { \"term\" }, RouteParameterTypes = new global::System.Type[] { typeof(string) }, QueryParameterNames = new string[] { \"Page\", \"class\" }, QueryParameterTypes = new global::System.Type[] { typeof(int), typeof(string) })]",
            generated);
        Assert.Contains("\"Preflight\", \"/api/{**path}\", new string[] { \"HEAD\", \"OPTIONS\" }", generated);
        Assert.Contains("internal sealed class EndpointContractAttribute : global::System.Attribute", generated);
    }

    /// <summary>A computed <c>Path</c> has no recoverable route, so no contract — never a wrong one.</summary>
    [Fact]
    public void Server_EndpointWithoutAKnownRoute_GetsNoContract()
    {
        var (generated, _) = Server("Shop.Api", ServerSource, Contracts());

        Assert.DoesNotContain("typeof(global::Shop.Api.Computed)", generated);
    }

    [Fact]
    public void Server_WithNoEndpoints_WritesNoContractFile()
    {
        var result = EndpointGeneratorHarness.Run("Empty", "namespace Empty; public class Nothing { }");

        Assert.DoesNotContain(result.GeneratedTrees, tree => tree.FilePath.EndsWith("EndpointContracts.g.cs", StringComparison.Ordinal));
    }

    // ---------- the client side ----------

    /// <summary>
    /// The M9 gate: a project with no source access to the server's endpoints, and no ASP.NET Core,
    /// compiles a typed client from the server assembly's metadata, and code that uses it.
    /// </summary>
    [Fact]
    public void Client_FromAMetadataReference_CompilesAndIsUsable()
    {
        var contracts = Contracts();
        var (_, server) = Server("Shop.Api", ServerSource, contracts);

        var run = RunClient(Client([contracts, server], ConsumerSource));

        Assert.True(run.Errors.Length == 0, string.Join("\n", run.Errors.Select(d => d.ToString())));
        Assert.Empty(run.CompileDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));

        var client = run.File("EndpointClients.g.cs");
        Assert.Contains("internal sealed partial class ApiClient", client);
        Assert.Contains("public async global::System.Threading.Tasks.Task<global::Shop.Contracts.ProductResponse?> GetProductAsync(int id, global::System.Threading.CancellationToken cancellationToken = default)", client);
        Assert.Contains("SearchProductsAsync(string? term = null, int? page = null, string? @class = null, global::System.Threading.CancellationToken cancellationToken = default)", client);
        Assert.Contains("global::System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<global::Shop.Contracts.ProductResponse>(__response.Content", client);
        Assert.Contains("global::System.Net.Http.Json.JsonContent.Create<global::Shop.Contracts.CreateProductRequest>(body", client);

        // The endpoint generator shares the analyzer assembly and runs here too, but finds no ASP.NET
        // Core and writes nothing.
        Assert.DoesNotContain(run.Result.GeneratedTrees, tree => tree.FilePath.EndsWith("EndpointMapping.g.cs", StringComparison.Ordinal));
    }

    /// <summary>The M9 gate: removing an endpoint from the server breaks the CLIENT's build.</summary>
    [Fact]
    public void Client_EndpointRemovedFromTheServer_BreaksTheClientBuild()
    {
        var contracts = Contracts();
        var withoutGetProduct = ServerSource.Replace("public partial class GetProduct", "public partial class GetProductGone");
        var (_, server) = Server("Shop.Api", withoutGetProduct, contracts);

        var run = RunClient(Client([contracts, server], ConsumerSource));

        Assert.Contains(run.Errors, d => d.Id == "CS1061" && d.GetMessage().Contains("GetProductAsync"));
    }

    /// <summary>R7.4a: opted in, but nothing to read — an empty result, not an error.</summary>
    [Fact]
    public void Client_ZeroContracts_GeneratesNothing_AndReportsNothing()
    {
        var run = RunClient(Client([], "namespace Shop.Client; public class Placeholder { }"));

        Assert.Empty(run.Result.GeneratedTrees);
        Assert.Empty(run.Result.Diagnostics);
        Assert.Empty(run.Errors);
    }

    [Fact]
    public void Client_NotOptedIn_GeneratesNothing()
    {
        var contracts = Contracts();
        var (_, server) = Server("Shop.Api", ServerSource, contracts);

        var run = RunClient(Client([contracts, server], "namespace Shop.Client; public class Placeholder { }"), optIn: false);

        Assert.Empty(run.Result.GeneratedTrees);
        Assert.Empty(run.Result.Diagnostics);
    }

    /// <summary>
    /// The attribute class is generated into the server, so the contract decodes in a client that
    /// references nothing of the server's but its assembly — Abstractions included.
    /// </summary>
    [Fact]
    public void Client_ReadsContracts_WithoutReferencingTheAbstractionsPackage()
    {
        var contracts = Contracts();
        var (_, server) = Server("Shop.Api", ServerSource, contracts);
        var compilation = Client([contracts, server]);

        Assert.DoesNotContain(compilation.References, reference => reference.Display?.Contains("MintPlayer.AspNetCore.Endpoints") == true);

        var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(server)!;
        var read = ClientContractReader.ReadAssembly(assembly, CancellationToken.None);

        Assert.Equal(["CreateProduct", "GetProduct", "Preflight", "SearchProducts"], read.Endpoints.Select(endpoint => endpoint.Name).ToArray());
        Assert.Empty(read.Problems);
    }

    /// <summary>
    /// A request or response type declared in the server assembly compiles, but a metadata-only
    /// reference never deploys it: MPEP022, and the method is still generated.
    /// </summary>
    [Fact]
    public void Client_TypeDeclaredInTheServerAssembly_WarnsMPEP022()
    {
        // The same DTOs, declared in the server's own (file-scoped) namespace instead of a contracts assembly.
        var (_, server) = Server(
            "Shop.Api",
            ServerSource.Replace("using Shop.Contracts;", "") +
            "\npublic record ProductResponse(int Id, string Name);\npublic record CreateProductRequest(string Name);\n");

        var run = RunClient(Client([server]));

        var warnings = run.Result.Diagnostics.Where(d => d.Id == "MPEP022").ToArray();
        Assert.Contains(warnings, d => d.GetMessage().Contains("'GetProduct'") && d.GetMessage().Contains("Shop.Api.ProductResponse"));
        Assert.All(warnings, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.Contains("GetProductAsync(", run.File("EndpointClients.g.cs"));
        Assert.Empty(run.Errors);
    }

    /// <summary>
    /// A type the client cannot resolve — here the client lacks the contracts assembly — leaves that
    /// endpoint out with MPEP021; endpoints that do not need it are still generated.
    /// </summary>
    [Fact]
    public void Client_UnresolvableType_SkipsThatEndpoint_WithMPEP021()
    {
        var contracts = Contracts();
        var (_, server) = Server("Shop.Api", ServerSource, contracts);

        var run = RunClient(Client([server]));

        Assert.Contains(run.Result.Diagnostics, d => d.Id == "MPEP021" && d.GetMessage().Contains("'GetProduct'") && d.GetMessage().Contains("cannot be resolved"));
        var client = run.File("EndpointClients.g.cs");
        Assert.DoesNotContain("GetProductAsync(", client);
        Assert.Contains("SearchProductsAsync(", client);
        Assert.Empty(run.Errors);
    }

    /// <summary>A contract written by a newer generator is skipped, never misread.</summary>
    [Fact]
    public void Client_NewerContractFormat_IsSkipped_WithMPEP021()
    {
        var server = Emit(CSharpCompilation.Create(
            "Future.Api",
            [CSharpSyntaxTree.ParseText("""
                [assembly: MintPlayer.AspNetCore.Endpoints.Generated.EndpointContractAttribute(typeof(Future.Api.Thing), "Thing", "/thing", new string[] { "GET" }, Version = 99)]
                namespace MintPlayer.AspNetCore.Endpoints.Generated
                {
                    [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
                    internal sealed class EndpointContractAttribute : System.Attribute
                    {
                        public EndpointContractAttribute(System.Type endpoint, string name, string route, string[] methods) { }
                        public int Version { get; set; }
                    }
                }
                namespace Future.Api { public class Thing { } }
                """)],
            clientFramework.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));

        var run = RunClient(Client([server]));

        Assert.Contains(run.Result.Diagnostics, d => d.Id == "MPEP021" && d.GetMessage().Contains("format version 99"));
        Assert.Empty(run.Result.GeneratedTrees);
    }

    /// <summary>
    /// The URL builder is the runtime library's own source, moved into the generated namespace — so
    /// the client and <c>EndpointRoute.ToString()</c> cannot build URLs differently.
    /// </summary>
    [Fact]
    public void Client_UrlHelper_IsTheRuntimeBinderSource_InTheGeneratedNamespace()
    {
        var contracts = Contracts();
        var (_, server) = Server("Shop.Api", ServerSource, contracts);

        var helper = RunClient(Client([contracts, server])).File("EndpointClientUrl.g.cs");

        var runtimeSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "Endpoints", "MintPlayer.AspNetCore.Endpoints", "EndpointTemplateBinder.cs"));
        Assert.Contains("namespace MintPlayer.AspNetCore.Endpoints.Generated", helper);
        Assert.Contains("internal static class EndpointTemplateBinder", helper);
        Assert.Equal(
            Normalise(runtimeSource).Replace("namespace MintPlayer.AspNetCore.Endpoints\n", "namespace MintPlayer.AspNetCore.Endpoints.Generated\n"),
            Normalise(helper.Substring(helper.IndexOf("// This text", StringComparison.Ordinal))));

        static string Normalise(string text) => text.Replace("\r\n", "\n").TrimEnd();
    }

    /// <summary>
    /// The client files follow the generated-code rules: no using directives beyond the one
    /// <c>global using</c>, and no extension method invoked on an instance.
    /// </summary>
    [Fact]
    public void Client_GeneratedCode_HasNoUsingDirectives_AndCallsExtensionsStatically()
    {
        var contracts = Contracts();
        var (_, server) = Server("Shop.Api", ServerSource, contracts);

        var run = RunClient(Client([contracts, server]));

        foreach (var tree in run.Result.GeneratedTrees)
        {
            var usings = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>().ToArray();
            Assert.All(usings, directive => Assert.True(directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword), $"{tree.FilePath}: {directive}"));
        }

        Assert.DoesNotContain(".ReadFromJsonAsync", run.File("EndpointClients.g.cs").Replace("HttpContentJsonExtensions.ReadFromJsonAsync", ""));
    }

    /// <summary>
    /// An edit to the client's own code that changes no contract serves the client from cache: the
    /// model compares equal, so its output steps do not run again.
    /// </summary>
    [Fact]
    public void Client_EditThatChangesNoContract_IsServedFromCache()
    {
        var contracts = Contracts();
        var (_, server) = Server("Shop.Api", ServerSource, contracts);
        var compilation = Client([contracts, server], "namespace Shop.Client; public class Placeholder { }");

        var driver = ClientDriver(track: true).RunGenerators(compilation);
        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText("namespace Shop.Client; public class Placeholder { public int X; }", new CSharpParseOptions(LanguageVersion.Latest), path: "Client0.cs"));
        var result = driver.RunGenerators(edited).GetRunResult();

        var clientResult = result.Results.Single(r => r.Generator.GetGeneratorType() == typeof(EndpointClientGenerator));
        var modelReasons = clientResult.TrackedSteps[EndpointClientGenerator.TrackingName].SelectMany(step => step.Outputs).Select(output => output.Reason).ToArray();
        Assert.All(modelReasons, reason => Assert.Equal(IncrementalStepRunReason.Unchanged, reason));

        var outputReasons = clientResult.TrackedOutputSteps.SelectMany(step => step.Value).SelectMany(step => step.Outputs).Select(output => output.Reason).ToArray();
        Assert.NotEmpty(outputReasons);
        Assert.All(outputReasons, reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
    }

    /// <summary>Two server assemblies whose last name segment is the same get their full names.</summary>
    [Fact]
    public void ClassNames_FallBackToTheFullAssemblyName_OnACollision()
    {
        Assert.Equal(["ApiClient"], EndpointClientWriter.ClassNames(["Shop.Api"]));
        Assert.Equal(["ShopApiClient", "StoreApiClient", "BillingClient"], EndpointClientWriter.ClassNames(["Shop.Api", "Store.Api", "Billing"]));
    }

    /// <summary>
    /// The typed client emits a fixed set of files however many servers it references and whatever
    /// they are called (PRD addendum 2, D23 with D24): every client class goes into one
    /// <c>EndpointClients.g.cs</c> next to <c>EndpointClientUrl.g.cs</c>.
    /// </summary>
    /// <remarks>
    /// One file per server, named after it, was unbounded in length and renamed with the server
    /// assembly. The server generator's guard is <see cref="FixedFileSetGuardTests"/>.
    /// </remarks>
    [Fact]
    public void Client_EmitsTheSameFiles_ForOneReferencedServerAndForFive()
    {
        var contracts = Contracts();
        MetadataReference[] Servers(int count) =>
        [
            contracts,
            .. Enumerable.Range(1, count).Select(i => Server($"Demo.N{i}.Api", $$"""
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.AspNetCore.Http;
                using MintPlayer.AspNetCore.Endpoints;
                using Shop.Contracts;

                namespace Demo.N{{i}}.Api;

                public partial class GetItem{{i}} : IGetEndpoint<ProductResponse>
                {
                    public static string Path => "/items{{i}}/{id}";
                    [RouteParam] public int Id { get; set; }
                    public override Task<IResult> HandleAsync(CancellationToken cancellationToken) => Task.FromResult(Results.Ok());
                }
                """, contracts).Image),
        ];

        var one = RunClient(Client(Servers(1)));
        var five = RunClient(Client(Servers(5)));

        Assert.True(five.Errors.Length == 0, string.Join("\n", five.Errors.Select(d => d.ToString())));
        foreach (var i in Enumerable.Range(1, 5))
            Assert.Contains($"GetItem{i}Async(", five.Text);

        string[] HintNames(ClientRun run) =>
        [
            .. run.Result.Results
                .SelectMany(generatorResult => generatorResult.GeneratedSources)
                .Select(source => source.HintName)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        Assert.Equal(["EndpointClientUrl.g.cs", "EndpointClients.g.cs"], HintNames(one));
        Assert.Equal(HintNames(one), HintNames(five));
    }

    /// <summary>
    /// <see cref="ClientServer.IsCacheable"/> is not part of the model's equality (PRD D11). It only
    /// decides whether the reference read is memoised; two reads of the same contract are the same
    /// input to the client output whether or not the second one may be cached, and comparing it would
    /// re-emit an unchanged client.
    /// </summary>
    [Fact]
    public void ClientServer_Equality_IgnoresIsCacheable()
    {
        var cacheable = new ClientServer("Shop.Api", [], [], isCacheable: true);
        var notCacheable = new ClientServer("Shop.Api", [], [], isCacheable: false);

        Assert.Equal(cacheable, notCacheable);
        Assert.Equal(cacheable.GetHashCode(), notCacheable.GetHashCode());
        Assert.NotEqual(cacheable, new ClientServer("Shop.Other", [], [], isCacheable: true));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MintPlayer.AspNetCore.Tools.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class OptionsProvider(Dictionary<string, string> globals) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(globals);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options([]);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options([]);

        private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
        }
    }
}
