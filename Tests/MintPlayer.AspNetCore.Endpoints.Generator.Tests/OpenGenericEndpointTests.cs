using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// Issue #34: an endpoint class with type parameters (its own, or a containing type's) cannot be
/// named by the generated mapping, typed-link, contract and OpenAPI files, so it must be skipped
/// there instead of breaking the whole assembly with CS0246.
/// </summary>
public class OpenGenericEndpointTests
{
    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;
        """;

    private const string FixturePreamble = Usings + """


        namespace Fixtures;

        public record UserResponse(int Id, string Name);
        public class ApiGroup : IEndpointGroup { public static string Prefix => "/api"; }


        """;

    private static Diagnostic[] Errors(IEnumerable<Diagnostic> diagnostics)
        => [.. diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];

    private static string Describe(IEnumerable<Diagnostic> diagnostics)
        => string.Join(" | ", diagnostics.Select(d => $"{d.Id} {d.Location.GetLineSpan().Path}: {d.GetMessage()}"));

    private static void AssertCompilesWithoutErrors(string source, bool includeOpenApi = false)
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", [source], includeOpenApi: includeOpenApi);
        var errors = Errors(EndpointGeneratorHarness.RunAndCompile(compilation));
        Assert.True(errors.Length == 0, $"expected no compile errors after generation, got: {Describe(errors)}");
    }

    private static string GeneratedFile(string source, string fileName, bool includeOpenApi = false)
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", [source], includeOpenApi: includeOpenApi);
        var run = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver
            .Create(new EndpointGenerator())
            .RunGenerators(compilation)
            .GetRunResult();
        return run.GeneratedTrees.SingleOrDefault(t => t.FilePath.EndsWith(fileName, StringComparison.Ordinal))?.ToString() ?? "";
    }

    /// <summary>
    /// The issue's exact repro, in the global namespace as reported. Today: CS0246 'TPayload' in
    /// EndpointMapping.g.cs (factory field, CreateFactory, Map, Describe) and EndpointContracts.g.cs.
    /// EndpointRoutes.g.cs names no types today; it is asserted to keep it that way.
    /// </summary>
    [Fact]
    public void IssueRepro_OpenGenericRawEndpoint_CompilesAndIsNotMapped()
    {
        const string source = Usings + """


            public class ApiGroup : IEndpointGroup { public static string Prefix => "/api"; }

            [MemberOf<ApiGroup>]
            public class Echo<TPayload> : IPostEndpoint where TPayload : class
            {
                public static string Path => "/echo";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(typeof(TPayload).Name));
            }
            """;

        AssertCompilesWithoutErrors(source);

        Assert.DoesNotContain("Echo<TPayload>", GeneratedFile(source, "EndpointMapping.g.cs"));
        Assert.DoesNotContain("Echo<TPayload>", GeneratedFile(source, "EndpointContracts.g.cs"));
        Assert.DoesNotContain("Echo<TPayload>", GeneratedFile(source, "EndpointRoutes.g.cs"));

        // D6: an Info on the class says it is not mapped here and how an application closes it.
        var info = Assert.Single(EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics, d => d.Id == "MPEP025");
        Assert.Equal(DiagnosticSeverity.Info, info.Severity);
        Assert.Contains("Echo<TPayload>", info.GetMessage());
        Assert.Contains("EndpointTypeArgument", info.GetMessage());
    }

    /// <summary>
    /// The issue's P1: one open-generic endpoint must not take the valid endpoints next to it down.
    /// They are still mapped, linked and contracted.
    /// </summary>
    [Fact]
    public void OpenGenericEndpoint_DoesNotBreakItsNeighbours()
    {
        var source = FixturePreamble + """
            [MemberOf<ApiGroup>]
            public class Echo<TPayload> : IPostEndpoint where TPayload : class
            {
                public static string Path => "/echo";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(typeof(TPayload).Name));
            }

            [MemberOf<ApiGroup>]
            public class Health : IGetEndpoint
            {
                public static string Path => "/health";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        AssertCompilesWithoutErrors(source);

        var mapping = GeneratedFile(source, "EndpointMapping.g.cs");
        Assert.Contains("global::Fixtures.Health", mapping);
        Assert.DoesNotContain("Echo<TPayload>", mapping);
        Assert.DoesNotContain("Map<global::Fixtures.Echo", mapping);

        // Recorded instead, unbound, for an application to close (PRD D3a).
        Assert.Contains("OpenEndpointAttribute(typeof(global::Fixtures.Echo<>)", mapping);
        Assert.Contains("typeof(global::Fixtures.Health)", GeneratedFile(source, "EndpointContracts.g.cs"));
    }

    /// <summary>
    /// PRD acceptance 2: the non-generic endpoints' mapping, contract and route output is byte for byte
    /// the same with and without the open-generic class beside them. The only addition to the mapping
    /// file is the block of <c>[assembly: OpenEndpoint…]</c> records for the open class (PRD D3a).
    /// </summary>
    [Fact]
    public void OpenGenericEndpoint_LeavesItsNeighboursOutputByteIdentical()
    {
        const string health = """
            [MemberOf<ApiGroup>]
            public class Health : IGetEndpoint
            {
                public static string Path => "/health";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }

            """;
        const string echo = """
            [MemberOf<ApiGroup>]
            public class Echo<TPayload> : IPostEndpoint where TPayload : class
            {
                public static string Path => "/echo";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(typeof(TPayload).Name));
            }
            """;

        var without = FixturePreamble + health;
        var with = FixturePreamble + health + echo;

        Assert.Equal(GeneratedFile(without, "EndpointContracts.g.cs"), GeneratedFile(with, "EndpointContracts.g.cs"));
        Assert.Equal(GeneratedFile(without, "EndpointRoutes.g.cs"), GeneratedFile(with, "EndpointRoutes.g.cs"));

        var mappingWith = GeneratedFile(with, "EndpointMapping.g.cs");
        Assert.Contains("[assembly: global::MintPlayer.AspNetCore.Endpoints.OpenEndpointAttribute(typeof(global::Fixtures.Echo<>)", mappingWith);
        var records = mappingWith.Split('\n')
            .Where(line => line.StartsWith("[assembly: global::MintPlayer.AspNetCore.Endpoints.OpenEndpoint", StringComparison.Ordinal))
            .ToArray();
        var stripped = string.Join("\n", mappingWith.Split('\n').Where(line => !records.Contains(line)))
            .Replace("\r\n\r\n\r\n", "\r\n\r\n").Replace("\n\n\n", "\n\n");
        Assert.Equal(GeneratedFile(without, "EndpointMapping.g.cs"), stripped);
    }

    /// <summary>
    /// Every open shape from PRD R1 compiles once the generator skips it. Each is red today.
    /// </summary>
    [Theory]
    [InlineData("nested raw", """
        public partial class Outer<T>
        {
            public class Inner : IGetEndpoint
            {
                public static string Path => "/inner";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(typeof(T).Name));
            }
        }
        """)]
    [InlineData("nested typed", """
        public partial class Outer<T> where T : class
        {
            public partial class Inner : IGetEndpoint<UserResponse>
            {
                public static string Path => "/inner";
                public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok(new UserResponse(1, typeof(T).Name)));
            }
        }
        """)]
    [InlineData("typed request is the type parameter", """
        public partial class Create<TReq> : IPostEndpoint<TReq, UserResponse> where TReq : class
        {
            public static string Path => "/create";
            public override Task<IResult> HandleAsync(TReq request, CancellationToken ct) => Task.FromResult(Results.Ok(new UserResponse(1, typeof(TReq).Name)));
        }
        """)]
    [InlineData("response-only response is the type parameter", """
        public partial class GetOne<TResponse> : IGetEndpoint<TResponse> where TResponse : class, new()
        {
            public static string Path => "/one";
            public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok(new TResponse()));
        }
        """)]
    [InlineData("typed with closed arguments on a generic class", """
        public partial class GetUser<T> : IGetEndpoint<UserResponse> where T : struct
        {
            public static string Path => "/user";
            public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok(new UserResponse(1, typeof(T).Name)));
        }
        """)]
    [InlineData("typed with route and query properties", """
        public partial class GetById<T> : IGetEndpoint<UserResponse> where T : class, new()
        {
            public static string Path => "/users/{id}";
            [RouteParam] public int Id { get; set; }
            [QueryParam] public string? Q { get; set; }
            public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok(new UserResponse(Id, typeof(T).Name + Q)));
        }
        """)]
    [InlineData("raw with a route property", """
        public partial class RawById<T> : IGetEndpoint where T : notnull
        {
            public static string Path => "/raw/{id}";
            [RouteParam] public int Id { get; set; }
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(typeof(T).Name + Id));
        }
        """)]
    public void OpenShape_CompilesWithoutErrors(string shape, string body)
    {
        _ = shape;
        AssertCompilesWithoutErrors(FixturePreamble + body, includeOpenApi: true);
    }

    /// <summary>
    /// An open group joined through a closed construction (<c>[MemberOf&lt;Api&lt;string&gt;&gt;]</c>)
    /// already compiles and maps at <c>/gapi/ping</c> at run time. But the plan keys groups by the
    /// open FQN <c>Api&lt;T&gt;</c>, so the membership <c>Api&lt;string&gt;</c> is never matched:
    /// MPEP016 misfires on <c>Api&lt;T&gt;</c> ("not joined"), and <c>Ping</c>'s composed route is
    /// null, so it silently gets no typed link and no contract. PRD D7: the closed group is keyed by
    /// its closed type, so it keeps its link and contract, and the open declaration is not reported.
    /// </summary>
    [Fact]
    public void OpenGroupJoinedThroughClosedConstruction_IsNotReportedAsUnjoined()
    {
        var source = FixturePreamble + """
            public class Api<T> : IEndpointGroup { public static string Prefix => "/gapi"; }

            [MemberOf<Api<string>>]
            public class Ping : IGetEndpoint
            {
                public static string Path => "/ping";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok("pong"));
            }

            [MemberOf<ApiGroup>]
            public class Health : IGetEndpoint
            {
                public static string Path => "/health";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        AssertCompilesWithoutErrors(source);

        var diagnostics = EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics;
        Assert.DoesNotContain(diagnostics, d => d.Id == "MPEP016");
        Assert.Contains("typeof(global::Fixtures.Ping), \"Ping\", \"/gapi/ping\"", GeneratedFile(source, "EndpointContracts.g.cs"));
        Assert.Contains("Ping()", GeneratedFile(source, "EndpointRoutes.g.cs"));
    }

    /// <summary>
    /// The generated partial of a generic endpoint must reopen the generic class, not declare a
    /// second, non-generic class of the same name: <c>BindParameters</c> must land on
    /// <c>GetById&lt;T&gt;</c>, which is what makes <c>MapEndpoint&lt;GetById&lt;X&gt;&gt;()</c> bind.
    /// </summary>
    [Fact]
    public void GenericPartialEndpoint_PartialDeclarationRepeatsTypeParameters()
    {
        var source = FixturePreamble + """
            public partial class GetById<T> : IGetEndpoint<UserResponse> where T : class, new()
            {
                public static string Path => "/users/{id}";
                [RouteParam] public int Id { get; set; }
                public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok(new UserResponse(Id, typeof(T).Name)));
            }
            """;

        var mapping = GeneratedFile(source, "EndpointMapping.g.cs");
        Assert.Contains("partial class GetById<T> : global::MintPlayer.AspNetCore.Endpoints.ResponseEndpoint", mapping);
        Assert.DoesNotContain("partial class GetById :", mapping);
    }

    /// <summary>
    /// Regression guard (PRD acceptance 5): a closed class deriving from an open generic base is an
    /// ordinary endpoint and stays mapped, with the base's [MemberOf&lt;T&gt;] inherited. Green today.
    /// </summary>
    [Fact]
    public void ClosedDerivedFromAbstractGenericBase_IsMappedWithInheritedGroup()
    {
        var source = FixturePreamble + """
            [MemberOf<ApiGroup>]
            public abstract class EchoBase<TPayload> : IPostEndpoint where TPayload : class
            {
                public static string Path => "/echo";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(typeof(TPayload).Name));
            }

            public class EchoString : EchoBase<string> { }
            """;

        AssertCompilesWithoutErrors(source);
        Assert.Contains("\"/api/echo\"", GeneratedFile(source, "EndpointContracts.g.cs"));
        Assert.Contains("typeof(global::Fixtures.EchoString)", GeneratedFile(source, "EndpointContracts.g.cs"));
    }
}
