using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// Issue #34, PRD D1–D9: an open-generic endpoint is closed by the application with
/// <c>[assembly: EndpointTypeArgument&lt;TConstraint, TArgument&gt;]</c> (or the explicit
/// <c>typeof(X&lt;&gt;)</c> form), from its own compilation or from a referenced library, and then
/// behaves as an ordinary endpoint named <c>{Name}_{TypeArguments}</c>.
/// </summary>
public class EndpointTypeArgumentTests
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        """;

    /// <summary>The library shape: generic over the application's user type, in two groups.</summary>
    private const string LibrarySource = Usings + """
        namespace Lib;

        public class LibUser { }

        public class AuthGroup : IEndpointGroup { public static string Prefix => "/lib/auth"; }

        [MemberOf<AuthGroup>]
        public partial class Passkeys<TUser> : IGetEndpoint<string> where TUser : LibUser, new()
        {
            public static string Path => "/passkeys/{id}";
            [RouteParam] public int Id { get; set; }
            public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok(typeof(TUser).Name + Id));
        }

        [MemberOf<AuthGroup>]
        public class WhoAmI<TUser> : IGetEndpoint where TUser : LibUser, new()
        {
            public static string Path => "/whoami";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(new TUser().ToString()));
        }

        public class Echo<TPayload> : IPostEndpoint where TPayload : class
        {
            public static string Path => "/echo";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(typeof(TPayload).Name));
        }

        internal class Hidden<TUser> : IGetEndpoint where TUser : LibUser
        {
            public static string Path => "/hidden";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }
        """;

    // ---------- harness ----------

    private sealed class Outcome(ImmutableArray<Diagnostic> generator, ImmutableArray<Diagnostic> compile, Dictionary<string, string> files)
    {
        public ImmutableArray<Diagnostic> Generator { get; } = generator;
        public ImmutableArray<Diagnostic> Compile { get; } = compile;

        public string File(string name) => files.TryGetValue(name, out var text) ? text : "";

        public Diagnostic[] Errors => [.. Generator.Concat(Compile).Where(d => d.Severity == DiagnosticSeverity.Error)];

        public Diagnostic[] Ids(string id) => [.. Generator.Where(d => d.Id == id)];

        public string Describe(IEnumerable<Diagnostic> diagnostics) =>
            string.Join(" | ", diagnostics.Select(d => $"{d.Id} {d.Location.GetLineSpan().Path}: {d.GetMessage()}"));
    }

    private static Outcome Run(CSharpCompilation compilation)
    {
        CSharpGeneratorDriver
            .Create(new EndpointGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);

        var files = updated.SyntaxTrees
            .Skip(compilation.SyntaxTrees.Length)
            .ToDictionary(tree => System.IO.Path.GetFileName(tree.FilePath), tree => tree.ToString());

        return new Outcome(diagnostics, updated.GetDiagnostics(), files);
    }

    private static Outcome App(string source, params MetadataReference[] references)
        => Run(EndpointGeneratorHarness.CreateCompilation("App", [source]).AddReferences(references));

    /// <summary>Compiles a library through the generator and returns its generated text and image.</summary>
    private static (string Generated, MetadataReference Image) Library(string source = LibrarySource, string assemblyName = "Lib")
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation(assemblyName, [source]);
        CSharpGeneratorDriver
            .Create(new EndpointGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);

        var errors = diagnostics.Concat(updated.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, "library did not compile: " + string.Join(" | ", errors.Select(e => e.ToString())));

        using var stream = new MemoryStream();
        var emit = updated.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var generated = string.Join("\n", updated.SyntaxTrees.Skip(compilation.SyntaxTrees.Length).Select(tree => tree.ToString()));
        return (generated, MetadataReference.CreateFromImage(stream.ToArray()));
    }

    private static void AssertNoErrors(Outcome outcome)
        => Assert.True(outcome.Errors.Length == 0, "expected no errors, got: " + outcome.Describe(outcome.Errors));

    /// <summary>The text a diagnostic's location spans — for "located on the attribute".</summary>
    private static string SpanText(Diagnostic diagnostic)
        => diagnostic.Location.SourceTree?.GetText().ToString(diagnostic.Location.SourceSpan) ?? "";

    // ---------- the declaring side (D6, D3a) ----------

    /// <summary>
    /// The library compiles, maps none of its open endpoints, reports MPEP025 per open endpoint, and
    /// records each one (and the groups on its chain) for the application to read.
    /// </summary>
    [Fact]
    public void Library_RecordsItsOpenEndpoints_AndMapsNoneOfThem()
    {
        var (generated, _) = Library();

        Assert.Contains("[assembly: global::MintPlayer.AspNetCore.Endpoints.OpenEndpointAttribute(typeof(global::Lib.Passkeys<>)", generated);
        Assert.Contains("Path = \"/passkeys/{id}\"", generated);
        Assert.Contains("HasBinder = true", generated);
        Assert.Contains("[assembly: global::MintPlayer.AspNetCore.Endpoints.OpenEndpointGroupAttribute(typeof(global::Lib.AuthGroup), Prefix = \"/lib/auth\")]", generated);
        Assert.DoesNotContain("Map<global::Lib.Passkeys", generated);

        var outcome = Run(EndpointGeneratorHarness.CreateCompilation("Lib", [LibrarySource]));
        Assert.Equal(4, outcome.Ids("MPEP025").Length);
        Assert.All(outcome.Ids("MPEP025"), d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));

        // AuthGroup is joined only by open endpoints: it is used, just not mapped here.
        Assert.Empty(outcome.Ids("MPEP016"));
    }

    // ---------- closing from a referenced library (D1, D3, D4) ----------

    [Fact]
    public void CrossAssembly_ConstraintKeyed_ClosesEveryMatchingEndpoint()
    {
        var (_, lib) = Library();
        var outcome = App(Usings + """
            [assembly: EndpointTypeArgument<Lib.LibUser, App.AppUser>]

            namespace App;

            public class AppUser : Lib.LibUser { }
            """, lib);

        AssertNoErrors(outcome);

        var mapping = outcome.File("EndpointMapping.g.cs");
        Assert.Contains("Map<global::Lib.Passkeys<global::App.AppUser>", mapping);
        Assert.Contains("Map<global::Lib.WhoAmI<global::App.AppUser>>", mapping);
        Assert.Contains("WithName(b, \"Passkeys_AppUser\")", mapping);
        Assert.Contains("WithName(b, \"WhoAmI_AppUser\")", mapping);
        Assert.Contains("MapGroup<global::Lib.AuthGroup>(app)", mapping);
        Assert.DoesNotContain("Echo", mapping);
        Assert.DoesNotContain("Hidden", mapping);

        var contracts = outcome.File("EndpointContracts.g.cs");
        Assert.Contains("typeof(global::Lib.Passkeys<global::App.AppUser>), \"Passkeys_AppUser\", \"/lib/auth/passkeys/{id}\", new string[] { \"GET\" }", contracts);
        Assert.Contains("RouteParameterTypes = new global::System.Type[] { typeof(int) }", contracts);
        Assert.Contains("ResponseType = typeof(string)", contracts);

        var routes = outcome.File("EndpointRoutes.g.cs");
        Assert.Contains("Passkeys_AppUser(int id)", routes);
        Assert.Contains("\"/lib/auth/passkeys/{id}\"", routes);
    }

    /// <summary>The shadow parameter documents <c>{id}</c> as an int path parameter (the manual path cannot).</summary>
    [Fact]
    public void CrossAssembly_ClosedEndpoint_GetsItsOpenApiShadowParameters()
    {
        var (_, lib) = Library();
        var outcome = Run(EndpointGeneratorHarness.CreateCompilation("App", [Usings + """
            [assembly: EndpointTypeArgument<Lib.LibUser, App.AppUser>]
            namespace App;
            public class AppUser : Lib.LibUser { }
            """], includeOpenApi: true).AddReferences(lib));

        AssertNoErrors(outcome);
        var mapping = outcome.File("EndpointMapping.g.cs");
        Assert.Contains("[global::Microsoft.AspNetCore.Mvc.FromRouteAttribute] public string? @Id { get; set; }", mapping);
        Assert.Contains("\"int32\"", outcome.File("EndpointOpenApi.g.cs"));
    }

    [Fact]
    public void CrossAssembly_ExplicitForm_ClosesOneEndpoint()
    {
        var (_, lib) = Library();
        var outcome = App(Usings + """
            [assembly: EndpointTypeArgument(typeof(Lib.Echo<>), typeof(string))]
            namespace App;
            """, lib);

        AssertNoErrors(outcome);
        var mapping = outcome.File("EndpointMapping.g.cs");
        Assert.Contains("Map<global::Lib.Echo<string>>", mapping);
        Assert.Contains("WithName(b, \"Echo_String\")", mapping);
        Assert.DoesNotContain("Passkeys", mapping);
    }

    /// <summary>A non-public library endpoint cannot be named by the application's generated code.</summary>
    [Fact]
    public void CrossAssembly_InternalLibraryEndpoint_IsReportedAndNotMapped()
    {
        var (_, lib) = Library();
        var outcome = App(Usings + """
            [assembly: EndpointTypeArgument<Lib.LibUser, App.AppUser>]
            namespace App;
            public class AppUser : Lib.LibUser { }
            """, lib);

        AssertNoErrors(outcome);
        var hidden = Assert.Single(outcome.Ids("MPEP029"));
        Assert.Equal(DiagnosticSeverity.Warning, hidden.Severity);
        Assert.Contains("Hidden", hidden.GetMessage());
        Assert.StartsWith("EndpointTypeArgument", SpanText(hidden));
    }

    // ---------- closing in the same compilation ----------

    private const string SameCompilation = Usings + """
        [assembly: EndpointTypeArgument<Fixtures.LibUser, Fixtures.AppUser>]

        namespace Fixtures;

        public class LibUser { }
        public class AppUser : LibUser { }
        public class AuthGroup : IEndpointGroup { public static string Prefix => "/auth"; }

        [MemberOf<AuthGroup>]
        public partial class Passkeys<TUser> : IGetEndpoint<string> where TUser : LibUser, new()
        {
            public static string Path => "/passkeys/{id}";
            [RouteParam] public int Id { get; set; }
            public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok(typeof(TUser).Name + Id));
        }
        """;

    [Fact]
    public void SameCompilation_ConstraintKeyed_ClosesTheEndpoint()
    {
        var outcome = App(SameCompilation);

        AssertNoErrors(outcome);
        var mapping = outcome.File("EndpointMapping.g.cs");
        Assert.Contains("partial class Passkeys<TUser> : global::MintPlayer.AspNetCore.Endpoints.ResponseEndpoint", mapping);
        Assert.Contains("Map<global::Fixtures.Passkeys<global::Fixtures.AppUser>", mapping);
        Assert.Contains("WithName(b, \"Passkeys_AppUser\")", mapping);
        Assert.Contains("\"/auth/passkeys/{id}\"", outcome.File("EndpointContracts.g.cs"));

        // Closed here, so the "not mapped" Info does not apply.
        Assert.Empty(outcome.Ids("MPEP025"));
    }

    [Fact]
    public void SameCompilation_ClosedEndpoint_HasItsGeneratedBinder()
    {
        var assembly = EndpointGeneratorHarness.RunAndLoad("OpenGenericsBinderLoad", SameCompilation);

        var type = assembly.GetType("Fixtures.Passkeys`1")!.MakeGenericType(assembly.GetType("Fixtures.AppUser")!);
        Assert.NotNull(type.GetMethod("BindParameters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void ExplicitForm_WinsOverConstraintKeys_ForItsEndpoint()
    {
        var outcome = App(SameCompilation.Replace(
            "[assembly: EndpointTypeArgument<Fixtures.LibUser, Fixtures.AppUser>]",
            "[assembly: EndpointTypeArgument<Fixtures.LibUser, Fixtures.AppUser>]\n[assembly: EndpointTypeArgument(typeof(Fixtures.Passkeys<>), typeof(Fixtures.OtherUser))]")
            + "\npublic class OtherUser : LibUser { }\n");

        AssertNoErrors(outcome);
        var mapping = outcome.File("EndpointMapping.g.cs");
        Assert.Contains("Map<global::Fixtures.Passkeys<global::Fixtures.OtherUser>", mapping);
        Assert.DoesNotContain("Passkeys<global::Fixtures.AppUser>", mapping);
    }

    /// <summary>Two closings of one endpoint are two ordinary endpoints: distinct names, and MPEP007 on the shared route.</summary>
    [Fact]
    public void TwoExplicitClosings_AreTwoEndpointsWithDistinctNames()
    {
        var outcome = App(Usings + """
            [assembly: EndpointTypeArgument(typeof(Fixtures.Echo<>), typeof(string))]
            [assembly: EndpointTypeArgument(typeof(Fixtures.Echo<>), typeof(System.Collections.Generic.List<int>))]
            [assembly: EndpointTypeArgument(typeof(Fixtures.Echo<>), typeof(int[]))]
            namespace Fixtures;
            public class Echo<TPayload> : IPostEndpoint where TPayload : class
            {
                public static string Path => "/echo";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(typeof(TPayload).Name));
            }
            """);

        AssertNoErrors(outcome);
        var mapping = outcome.File("EndpointMapping.g.cs");
        Assert.Contains("WithName(b, \"Echo_String\")", mapping);
        Assert.Contains("WithName(b, \"Echo_List_Int32\")", mapping);
        Assert.Contains("WithName(b, \"Echo_Int32Array\")", mapping);
        // Three endpoints on one route and verb: once per colliding pair.
        Assert.Equal(3, outcome.Ids("MPEP007").Length);
    }

    // ---------- D2: every error, none as a compile error in generated code ----------

    [Theory]
    [InlineData("new()", "public class AppUser : LibUser { public AppUser(int x) { } }", "[assembly: EndpointTypeArgument<Fixtures.LibUser, Fixtures.AppUser>]")]
    [InlineData("class", "public struct Value { }", "[assembly: EndpointTypeArgument(typeof(Fixtures.Echo<>), typeof(Fixtures.Value))]")]
    [InlineData("struct", "", "[assembly: EndpointTypeArgument(typeof(Fixtures.Counter<>), typeof(string))]")]
    [InlineData("unmanaged", "public struct Holder { public string S; }", "[assembly: EndpointTypeArgument(typeof(Fixtures.Buffer<>), typeof(Fixtures.Holder))]")]
    [InlineData("notnull", "", "[assembly: EndpointTypeArgument(typeof(Fixtures.Keyed<>), typeof(int?))]")]
    [InlineData("IAudited", "public class AppUser : LibUser { }", "[assembly: EndpointTypeArgument<Fixtures.LibUser, Fixtures.AppUser>]", "Audited")]
    public void ViolatedConstraint_IsAnErrorOnTheAttribute(string constraint, string extra, string attribute, string endpoint = "")
    {
        var outcome = App(Usings + attribute + "\n" + """
            namespace Fixtures;
            public class LibUser { }
            public interface IAudited { }
            public class Echo<TPayload> : IPostEndpoint where TPayload : class
            { public static string Path => "/echo"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }
            public class Counter<T> : IGetEndpoint where T : struct
            { public static string Path => "/counter"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }
            public class Buffer<T> : IGetEndpoint where T : unmanaged
            { public static string Path => "/buffer"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }
            public class Keyed<T> : IGetEndpoint where T : notnull
            { public static string Path => "/keyed"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }
            """ + (endpoint == "Audited"
                ? "public class Audited<TUser> : IGetEndpoint where TUser : LibUser, IAudited { public static string Path => \"/audited\"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }"
                : "public class Users<TUser> : IGetEndpoint where TUser : LibUser, new() { public static string Path => \"/users\"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }")
            + "\n" + extra);

        var error = Assert.Single(outcome.Ids("MPEP026"));
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains(constraint, error.GetMessage());
        Assert.StartsWith("EndpointTypeArgument", SpanText(error));

        // The only error is ours: nothing uncompilable was emitted.
        Assert.True(outcome.Compile.All(d => d.Severity != DiagnosticSeverity.Error), outcome.Describe(outcome.Compile.Where(d => d.Severity == DiagnosticSeverity.Error)));
    }

    [Fact]
    public void TwoAttributesBindingOneParameter_IsAnError()
    {
        var outcome = App(Usings + """
            [assembly: EndpointTypeArgument<Fixtures.LibUser, Fixtures.AppUser>]
            [assembly: EndpointTypeArgument<Fixtures.IAudited, Fixtures.AppUser>]
            namespace Fixtures;
            public class LibUser { }
            public interface IAudited { }
            public class AppUser : LibUser, IAudited { }
            public class Users<TUser> : IGetEndpoint where TUser : LibUser, IAudited
            { public static string Path => "/users"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }
            """);

        var error = Assert.Single(outcome.Ids("MPEP027"));
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("TUser", error.GetMessage());
        Assert.StartsWith("EndpointTypeArgument", SpanText(error));
        Assert.DoesNotContain("Map<global::Fixtures.Users", outcome.File("EndpointMapping.g.cs"));
        Assert.True(outcome.Compile.All(d => d.Severity != DiagnosticSeverity.Error));
    }

    [Fact]
    public void ExplicitForm_WithTheWrongNumberOfTypeArguments_IsAnError()
    {
        var outcome = App(Usings + """
            [assembly: EndpointTypeArgument(typeof(Fixtures.Echo<>), typeof(string), typeof(int))]
            namespace Fixtures;
            public class Echo<TPayload> : IPostEndpoint where TPayload : class
            { public static string Path => "/echo"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }
            """);

        var error = Assert.Single(outcome.Ids("MPEP028"));
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.StartsWith("EndpointTypeArgument", SpanText(error));
        Assert.True(outcome.Compile.All(d => d.Severity != DiagnosticSeverity.Error));
    }

    // ---------- unbound parameters, unused attributes ----------

    [Fact]
    public void PartiallyBoundEndpoint_IsNotMapped_AndSaysWhichParameterIsUnbound()
    {
        var outcome = App(Usings + """
            [assembly: EndpointTypeArgument<Fixtures.LibUser, Fixtures.AppUser>]
            namespace Fixtures;
            public class LibUser { }
            public class AppUser : LibUser { }
            public class Pair<TUser, TOther> : IGetEndpoint where TUser : LibUser
            { public static string Path => "/pair"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }
            """);

        AssertNoErrors(outcome);
        Assert.DoesNotContain("Map<global::Fixtures.Pair", outcome.File("EndpointMapping.g.cs"));
        var warning = Assert.Single(outcome.Ids("MPEP031"));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("TOther", warning.GetMessage());
    }

    [Fact]
    public void AttributeThatClosesNothing_IsAWarning()
    {
        var outcome = App(Usings + """
            [assembly: EndpointTypeArgument<Fixtures.LibUser, Fixtures.AppUser>]
            namespace Fixtures;
            public class LibUser { }
            public class AppUser : LibUser { }
            public class Health : IGetEndpoint
            { public static string Path => "/health"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }
            """);

        AssertNoErrors(outcome);
        var warning = Assert.Single(outcome.Ids("MPEP030"));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.StartsWith("EndpointTypeArgument", SpanText(warning));
    }

    // ---------- nested open containers ----------

    [Fact]
    public void NestedInAGenericContainer_IsClosedThroughTheContainersParameter()
    {
        var outcome = App(Usings + """
            [assembly: EndpointTypeArgument<Fixtures.LibUser, Fixtures.AppUser>]
            namespace Fixtures;
            public class LibUser { }
            public class AppUser : LibUser { }
            public class OuterApi : IEndpointGroup { public static string Prefix => "/outer"; }
            public partial class Outer<TUser> where TUser : LibUser
            {
                [MemberOf<OuterApi>]
                public partial class Inner : IGetEndpoint<string>
                {
                    public static string Path => "/inner/{id}";
                    [RouteParam] public int Id { get; set; }
                    public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok(typeof(TUser).Name + Id));
                }
            }
            """);

        AssertNoErrors(outcome);
        var mapping = outcome.File("EndpointMapping.g.cs");
        Assert.Contains("Map<global::Fixtures.Outer<global::Fixtures.AppUser>.Inner", mapping);
        Assert.Contains("partial class Outer<TUser>", mapping);
        Assert.Contains("MapGroup<global::Fixtures.OuterApi>(app)", mapping);
        Assert.Contains("WithName(b, \"Inner_AppUser\")", mapping);
        Assert.Contains("\"/outer/inner/{id}\"", outcome.File("EndpointContracts.g.cs"));
    }

    // ---------- D7: closed generic groups, new static Path ----------

    [Fact]
    public void ClosedGenericGroup_KeepsLinkAndContract()
    {
        var outcome = App(Usings + """
            namespace Fixtures;
            public class Api<T> : IEndpointGroup { public static string Prefix => "/gapi"; }

            [MemberOf<Api<string>>]
            public class Ping : IGetEndpoint
            { public static string Path => "/ping"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok("pong")); }
            """);

        AssertNoErrors(outcome);
        Assert.Empty(outcome.Ids("MPEP016"));
        Assert.Contains("MapGroup<global::Fixtures.Api<string>>(app)", outcome.File("EndpointMapping.g.cs"));
        Assert.Contains("\"/gapi/ping\"", outcome.File("EndpointContracts.g.cs"));
        Assert.Contains("Ping()", outcome.File("EndpointRoutes.g.cs"));
    }

    [Fact]
    public void NewStaticPath_OnADerivedEndpoint_UsesTheRuntimeRoute_AndWarns()
    {
        var outcome = App(Usings + """
            namespace Fixtures;
            public abstract class PingBase : IGetEndpoint
            {
                public static string Path => "/base";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            public class Hidden : PingBase { public new static string Path => "/hidden"; }
            public class Relisted : PingBase, IGetEndpoint { public new static string Path => "/relisted"; }
            """);

        AssertNoErrors(outcome);
        var contracts = outcome.File("EndpointContracts.g.cs");
        Assert.Contains("typeof(global::Fixtures.Hidden), \"Hidden\", \"/base\"", contracts);
        Assert.Contains("typeof(global::Fixtures.Relisted), \"Relisted\", \"/relisted\"", contracts);

        var warning = Assert.Single(outcome.Ids("MPEP032"));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("Hidden", warning.GetMessage());
    }

    /// <summary>
    /// <c>Methods</c> resolves like <c>Path</c>: through the interface map the runtime's
    /// <c>TEndpoint.Methods</c> dispatches through. A verb interface's default implementation is the verb.
    /// A <c>new static Methods</c> that does not re-implement the interface is ignored at run time, so
    /// the contract and MPEP007 must ignore it too, and MPEP032 says so.
    /// </summary>
    [Fact]
    public void NewStaticMethods_OnADerivedEndpoint_UsesTheRuntimeVerbs_AndWarns()
    {
        var outcome = App(Usings + """
            using System.Collections.Generic;
            namespace Fixtures;
            public abstract class GetBase : IGetEndpoint
            {
                public static string Path => "/items";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            public abstract class MultiBase : IGetEndpoint
            {
                public static string Path => "/multi";
                public static IEnumerable<string> Methods => ["GET", "HEAD"];
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            // Runtime: GET (IGetEndpoint's default), not POST.
            public class HiddenVerb : GetBase { public new static IEnumerable<string> Methods => ["POST"]; }
            // Runtime: GET, HEAD (MultiBase's), not DELETE.
            public class HiddenMulti : MultiBase { public new static IEnumerable<string> Methods => ["DELETE"]; }
            // Re-listing the interface re-implements it: PUT.
            public class RelistedVerb : GetBase, IGetEndpoint { public new static string Path => "/relisted"; public new static IEnumerable<string> Methods => ["PUT"]; }
            // Shares HiddenVerb's route with another verb: no conflict at run time.
            public class ItemsPost : IPostEndpoint
            { public static string Path => "/items"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }
            """);

        AssertNoErrors(outcome);
        var contracts = outcome.File("EndpointContracts.g.cs");
        Assert.Contains("typeof(global::Fixtures.HiddenVerb), \"HiddenVerb\", \"/items\", new string[] { \"GET\" }", contracts);
        Assert.Contains("typeof(global::Fixtures.HiddenMulti), \"HiddenMulti\", \"/multi\", new string[] { \"GET\", \"HEAD\" }", contracts);
        Assert.Contains("typeof(global::Fixtures.RelistedVerb), \"RelistedVerb\", \"/relisted\", new string[] { \"PUT\" }", contracts);

        Assert.True(outcome.Ids("MPEP007").Length == 0, outcome.Describe(outcome.Ids("MPEP007")));

        var warnings = outcome.Ids("MPEP032");
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, warning =>
        {
            Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
            Assert.Contains("Methods", warning.GetMessage());
        });
        Assert.Contains(warnings, warning => warning.GetMessage().Contains("HiddenVerb") && warning.GetMessage().Contains("GET"));
        Assert.Contains(warnings, warning => warning.GetMessage().Contains("HiddenMulti") && warning.GetMessage().Contains("GET, HEAD"));
    }

    // ---------- generic constraint types ----------

    /// <summary>
    /// Type parameters constrained to a generic type: closed (<c>IUser&lt;Guid&gt;</c>) and dependent
    /// on another type parameter (<c>IUser&lt;TKey&gt;</c>).
    /// </summary>
    private const string GenericConstraintLibrary = Usings + """
        namespace Lib;

        public interface IUser<TKey> { TKey Id { get; } }

        public class Profile<TUser> : IGetEndpoint where TUser : IUser<Guid>
        {
            public static string Path => "/profile";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(typeof(TUser).Name));
        }

        public partial class Passkeys<TUser, TKey> : IGetEndpoint<string> where TUser : IUser<TKey>
        {
            public static string Path => "/passkeys/{id}";
            [RouteParam] public int Id { get; set; }
            public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok(typeof(TUser).Name + typeof(TKey).Name + Id));
        }
        """;

    private const string GenericConstraintUsers = """

        namespace App
        {
            public class AppUser : Lib.IUser<System.Guid> { public System.Guid Id => default; }
            public class IntUser : Lib.IUser<int> { public int Id => 0; }
        }
        """;

    private static Outcome GenericConstraintApp(bool crossAssembly, string attributes)
    {
        var app = Usings + attributes + GenericConstraintUsers;
        if (!crossAssembly)
            return Run(EndpointGeneratorHarness.CreateCompilation("App", [GenericConstraintLibrary, app]));

        var (_, lib) = Library(GenericConstraintLibrary);
        return App(app, lib);
    }

    [Fact]
    public void GenericConstraintLibrary_CompilesAndRecordsBothEndpoints()
    {
        var (generated, _) = Library(GenericConstraintLibrary);
        Assert.Contains("typeof(global::Lib.Profile<>)", generated);
        Assert.Contains("typeof(global::Lib.Passkeys<,>)", generated);
    }

    /// <summary>(a) A closed generic constraint is an ordinary key: equality holds.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedGenericConstraint_IsBoundByItsKey(bool crossAssembly)
    {
        var outcome = GenericConstraintApp(crossAssembly, "[assembly: EndpointTypeArgument<Lib.IUser<System.Guid>, App.AppUser>]\n");

        AssertNoErrors(outcome);
        var mapping = outcome.File("EndpointMapping.g.cs");
        Assert.Contains("Map<global::Lib.Profile<global::App.AppUser>>", mapping);
        Assert.Contains("WithName(b, \"Profile_AppUser\")", mapping);
        Assert.DoesNotContain("Map<global::Lib.Passkeys", mapping);
        Assert.Empty(outcome.Ids("MPEP030"));
    }

    /// <summary>
    /// (b) <c>IUser&lt;TKey&gt;</c> mentions another type parameter, so no key can equal it. The key that
    /// was evidently meant for it gets a diagnostic naming the explicit form, and nothing is emitted.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstraintOnAnotherTypeParameter_IsNotBoundByAKey_AndSaysToUseTheExplicitForm(bool crossAssembly)
    {
        var outcome = GenericConstraintApp(crossAssembly, "[assembly: EndpointTypeArgument<Lib.IUser<System.Guid>, App.AppUser>]\n");

        Assert.True(outcome.Compile.All(d => d.Severity != DiagnosticSeverity.Error), outcome.Describe(outcome.Compile.Where(d => d.Severity == DiagnosticSeverity.Error)));
        Assert.DoesNotContain("Map<global::Lib.Passkeys", outcome.File("EndpointMapping.g.cs"));

        var warning = Assert.Single(outcome.Ids("MPEP033"));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.StartsWith("EndpointTypeArgument", SpanText(warning));
        Assert.Contains("TUser", warning.GetMessage());
        Assert.Contains("TKey", warning.GetMessage());
        Assert.Contains("typeof(Lib.Passkeys<,>)", warning.GetMessage());
        Assert.Empty(outcome.Ids("MPEP030"));
        Assert.Empty(outcome.Ids("MPEP031"));
    }

    /// <summary>(c) The explicit form closes it, with the dependent constraint checked after substitution.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstraintOnAnotherTypeParameter_IsClosedByTheExplicitForm(bool crossAssembly)
    {
        var outcome = GenericConstraintApp(crossAssembly, "[assembly: EndpointTypeArgument(typeof(Lib.Passkeys<,>), typeof(App.AppUser), typeof(System.Guid))]\n");

        AssertNoErrors(outcome);
        var mapping = outcome.File("EndpointMapping.g.cs");
        Assert.Contains("Map<global::Lib.Passkeys<global::App.AppUser, global::System.Guid>", mapping);
        Assert.Contains("WithName(b, \"Passkeys_AppUser_Guid\")", mapping);
        Assert.Contains("\"/passkeys/{id}\"", outcome.File("EndpointContracts.g.cs"));
        Assert.Contains("Passkeys_AppUser_Guid(int id)", outcome.File("EndpointRoutes.g.cs"));
        Assert.Empty(outcome.Ids("MPEP033"));
    }

    /// <summary>
    /// The usual pairing: a key for the endpoints it can close, the explicit form for the one it cannot.
    /// The explicit form wins for its endpoint, so the key raises no MPEP033 there.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyAndExplicitForm_TogetherCloseBoth_WithoutMPEP033(bool crossAssembly)
    {
        var outcome = GenericConstraintApp(crossAssembly,
            "[assembly: EndpointTypeArgument<Lib.IUser<System.Guid>, App.AppUser>]\n" +
            "[assembly: EndpointTypeArgument(typeof(Lib.Passkeys<,>), typeof(App.AppUser), typeof(System.Guid))]\n");

        AssertNoErrors(outcome);
        var mapping = outcome.File("EndpointMapping.g.cs");
        Assert.Contains("Map<global::Lib.Profile<global::App.AppUser>>", mapping);
        Assert.Contains("Map<global::Lib.Passkeys<global::App.AppUser, global::System.Guid>", mapping);
        Assert.Empty(outcome.Ids("MPEP033"));
        Assert.Empty(outcome.Ids("MPEP030"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstraintOnAnotherTypeParameter_ExplicitFormViolatingIt_IsAnError(bool crossAssembly)
    {
        var outcome = GenericConstraintApp(crossAssembly, "[assembly: EndpointTypeArgument(typeof(Lib.Passkeys<,>), typeof(App.IntUser), typeof(System.Guid))]\n");

        var error = Assert.Single(outcome.Ids("MPEP026"));
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Lib.IUser<System.Guid>", error.GetMessage());
        Assert.Contains("TUser", error.GetMessage());
        Assert.StartsWith("EndpointTypeArgument", SpanText(error));
        Assert.DoesNotContain("Map<global::Lib.Passkeys", outcome.File("EndpointMapping.g.cs"));
        Assert.True(outcome.Compile.All(d => d.Severity != DiagnosticSeverity.Error), outcome.Describe(outcome.Compile.Where(d => d.Severity == DiagnosticSeverity.Error)));
    }

    // ---------- D5: IsEnabled ----------

    [Fact]
    public void EveryGroup_IsWrappedInItsIsEnabledCheck()
    {
        var outcome = App(Usings + """
            namespace Fixtures;
            public class ApiGroup : IEndpointGroup { public static string Prefix => "/api"; }
            [MemberOf<ApiGroup>]
            public class Health : IGetEndpoint
            { public static string Path => "/health"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok()); }
            """);

        AssertNoErrors(outcome);
        var mapping = outcome.File("EndpointMapping.g.cs");
        Assert.Contains("if (IsEnabled<global::Fixtures.ApiGroup>(app.ServiceProvider))", mapping);
        Assert.Contains("private static bool IsEnabled<TGroup>(global::System.IServiceProvider services) where TGroup : global::MintPlayer.AspNetCore.Endpoints.IEndpointGroup", mapping);
    }

    // ---------- incremental ----------

    /// <summary>
    /// The closing step reads the compilation, so it runs every time; its model must compare equal so
    /// that nothing downstream reruns on a handler-body edit.
    /// </summary>
    [Fact]
    public void HandlerBodyEdit_WithClosings_DoesNotRebuildTheModel()
    {
        var (_, lib) = Library();
        var appSource = Usings + """
            [assembly: EndpointTypeArgument<Lib.LibUser, App.AppUser>]
            namespace App;
            public class AppUser : Lib.LibUser { }
            public class Health : IGetEndpoint
            { public static string Path => "/health"; public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok("up")); }
            """;
        var compilation = EndpointGeneratorHarness.CreateCompilation("App", [appSource]).AddReferences(lib);
        var driver = EndpointGeneratorHarness.CreateTrackingDriver().RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Last(),
            CSharpSyntaxTree.ParseText(appSource.Replace("\"up\"", "\"ok\""), new CSharpParseOptions(LanguageVersion.Latest), path: compilation.SyntaxTrees.Last().FilePath));
        var result = driver.RunGenerators(edited).GetRunResult();

        foreach (var step in new[] { "ClosedEndpoints", "EndpointModel" })
        {
            var reasons = result.Results.SelectMany(r => r.TrackedSteps).Where(s => s.Key == step)
                .SelectMany(s => s.Value).SelectMany(s => s.Outputs).Select(o => o.Reason).ToArray();
            Assert.NotEmpty(reasons);
            Assert.All(reasons, reason => Assert.True(
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"expected {step} to be cached, was {reason}"));
        }

        Assert.Contains("Passkeys_AppUser", string.Join("\n", result.GeneratedTrees.Select(t => t.ToString())));
    }
}
