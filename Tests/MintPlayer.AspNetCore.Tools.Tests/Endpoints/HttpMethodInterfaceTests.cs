using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MintPlayer.AspNetCore.Endpoints;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// Pins the HTTP verb each convenience interface contributes.
/// </summary>
/// <remarks>
/// These are cheap, and they guard the single most breakable thing in the abstractions package.
/// The fifteen verb interfaces are literal copies of one another across three arities, so a
/// copy-paste slip in one arity — <c>IPutEndpoint&lt;TRequest&gt;</c> returning "POST", say — would
/// compile fine, pass every other test, and silently register routes on the wrong verb.
/// </remarks>
public class HttpMethodInterfaceTests
{
    /// <summary>
    /// Reads a static abstract interface member, which requires a generic type parameter — there
    /// is no way to get at <c>T.Methods</c> without one.
    /// </summary>
    private static IEnumerable<string> MethodsOf<T>() where T : IEndpointBase => T.Methods;

    private sealed class GetRaw : IGetEndpoint
    {
        public static string Path => "/";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class PostRaw : IPostEndpoint
    {
        public static string Path => "/";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class PutRaw : IPutEndpoint
    {
        public static string Path => "/";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class PatchRaw : IPatchEndpoint
    {
        public static string Path => "/";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class DeleteRaw : IDeleteEndpoint
    {
        public static string Path => "/";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    [Fact]
    public void IGetEndpoint_ContributesExactlyGet() => Assert.Equal(["GET"], MethodsOf<GetRaw>());

    [Fact]
    public void IPostEndpoint_ContributesExactlyPost() => Assert.Equal(["POST"], MethodsOf<PostRaw>());

    [Fact]
    public void IPutEndpoint_ContributesExactlyPut() => Assert.Equal(["PUT"], MethodsOf<PutRaw>());

    [Fact]
    public void IPatchEndpoint_ContributesExactlyPatch() => Assert.Equal(["PATCH"], MethodsOf<PatchRaw>());

    [Fact]
    public void IDeleteEndpoint_ContributesExactlyDelete() => Assert.Equal(["DELETE"], MethodsOf<DeleteRaw>());

    // Arity-1 and arity-2 fixtures. These are what catch a copy-paste verb error in a single
    // arity, which the arity-0 tests above cannot see.

    private sealed record Req(int Id);
    private sealed record Res(string Name);

    // Since M4 the arity-1 GET/DELETE is the response-only rung: the type argument is the
    // response, and the handler takes no request.
    private sealed class GetTyped : ResponseEndpoint, IGetEndpoint<Res>
    {
        public static string Path => "/";
        public override Task<IResult> HandleAsync(CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    private sealed class GetTypedWithResponse : GetEndpoint<Req>, IGetEndpoint<Req, Res>
    {
        public static string Path => "/";
        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    private sealed class PostTyped : PostEndpoint<Req>, IPostEndpoint<Req>
    {
        public static string Path => "/";
        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    private sealed class PostTypedWithResponse : PostEndpoint<Req>, IPostEndpoint<Req, Res>
    {
        public static string Path => "/";
        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    private sealed class PutTyped : PutEndpoint<Req>, IPutEndpoint<Req>
    {
        public static string Path => "/";
        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    private sealed class PatchTyped : PatchEndpoint<Req>, IPatchEndpoint<Req>
    {
        public static string Path => "/";
        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    private sealed class DeleteTyped : ResponseEndpoint, IDeleteEndpoint<Res>
    {
        public static string Path => "/";
        public override Task<IResult> HandleAsync(CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    private sealed class PutTypedWithResponse : PutEndpoint<Req>, IPutEndpoint<Req, Res>
    {
        public static string Path => "/";
        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    private sealed class PatchTypedWithResponse : PatchEndpoint<Req>, IPatchEndpoint<Req, Res>
    {
        public static string Path => "/";
        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    private sealed class DeleteTypedWithResponse : DeleteEndpoint<Req>, IDeleteEndpoint<Req, Res>
    {
        public static string Path => "/";
        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    [Fact]
    public void GenericArities_ContributeTheSameVerbAsArityZero()
    {
        Assert.Equal(["GET"], MethodsOf<GetTyped>());
        Assert.Equal(["GET"], MethodsOf<GetTypedWithResponse>());
        Assert.Equal(["POST"], MethodsOf<PostTyped>());
        Assert.Equal(["POST"], MethodsOf<PostTypedWithResponse>());
        Assert.Equal(["PUT"], MethodsOf<PutTyped>());
        Assert.Equal(["PATCH"], MethodsOf<PatchTyped>());
        Assert.Equal(["DELETE"], MethodsOf<DeleteTyped>());

        // Arity 2. These were the gap: PUT, PATCH and DELETE had no two-parameter fixture, so
        // their arity-2 Methods implementations were never executed — which is exactly the
        // per-arity copy-paste slip this class exists to catch.
        Assert.Equal(["PUT"], MethodsOf<PutTypedWithResponse>());
        Assert.Equal(["PATCH"], MethodsOf<PatchTypedWithResponse>());
        Assert.Equal(["DELETE"], MethodsOf<DeleteTypedWithResponse>());
    }

    /// <summary>
    /// <c>SuccessStatusCode</c> defaults to 200 and can be overridden explicitly.
    /// </summary>
    private static int StatusOf<TEndpoint, TRequest, TResponse>()
        where TEndpoint : IEndpoint<TRequest, TResponse> => TEndpoint.SuccessStatusCode;

    private sealed class Created : PostEndpoint<Req>, IPostEndpoint<Req, Res>
    {
        public static string Path => "/";
        static int IEndpoint<Req, Res>.SuccessStatusCode => 201;
        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    [Fact]
    public void SuccessStatusCode_DefaultsTo200()
        => Assert.Equal(200, StatusOf<PostTypedWithResponse, Req, Res>());

    [Fact]
    public void SuccessStatusCode_HonoursExplicitOverride()
        => Assert.Equal(201, StatusOf<Created, Req, Res>());

    /// <summary>
    /// The response-only rung declares its own <c>SuccessStatusCode</c> on
    /// <c>IResponseEndpoint&lt;TResponse&gt;</c>, defaulting to 200 and overridable the same way.
    /// </summary>
    /// <remarks>
    /// It is a different static virtual from <c>IEndpoint&lt;TRequest, TResponse&gt;.SuccessStatusCode</c>;
    /// an override written against the wrong interface would not compile, but a default that
    /// drifted from 200 would silently change every GET's documented status.
    /// </remarks>
    private static int ResponseStatusOf<TEndpoint, TResponse>()
        where TEndpoint : IResponseEndpoint<TResponse> => TEndpoint.SuccessStatusCode;

    private sealed class Accepted : ResponseEndpoint, IDeleteEndpoint<Res>
    {
        public static string Path => "/";
        static int IResponseEndpoint<Res>.SuccessStatusCode => 202;
        public override Task<IResult> HandleAsync(CancellationToken cancellationToken)
            => Task.FromResult(Results.Accepted());
    }

    [Fact]
    public void ResponseOnlySuccessStatusCode_DefaultsTo200_AndHonoursOverride()
    {
        Assert.Equal(200, ResponseStatusOf<GetTyped, Res>());
        Assert.Equal(202, ResponseStatusOf<Accepted, Res>());
    }

    /// <summary>
    /// <c>Methods</c> returns the same cached instance on every access.
    /// </summary>
    /// <remarks>
    /// The collection expression <c>["GET"]</c> in an <c>IEnumerable&lt;string&gt;</c>-returning
    /// property body allocated a fresh <c>string[]</c> per call, and <c>Methods</c> is read once per
    /// registration <i>and</i> once per descriptor. It is also what made
    /// <see cref="EndpointDescriptor"/>'s record equality useless — see
    /// <see cref="EndpointDescriptorTests"/>.
    /// </remarks>
    [Fact]
    public void Methods_ReturnsACachedInstance()
    {
        var first = MethodsOf<GetRaw>();
        var second = MethodsOf<GetRaw>();

        Assert.Same(first, second);
        Assert.Equal(HttpVerbs.Get, first);
    }

    /// <summary>
    /// The cached lists are read-only, so a consumer cannot mutate the instance every endpoint of
    /// that verb shares.
    /// </summary>
    [Fact]
    public void Methods_CachedInstanceIsNotAMutableArray()
    {
        Assert.All<IEnumerable<string>>(
            [HttpVerbs.Get, HttpVerbs.Post, HttpVerbs.Put, HttpVerbs.Patch, HttpVerbs.Delete],
            verbs => Assert.False(verbs is string[]));
    }

    /// <summary>
    /// A class may declare its own <c>Methods</c> while implementing a convenience interface — the
    /// class member is more specific, so it wins.
    /// </summary>
    /// <remarks>
    /// Recorded in the defect register as unfixable without changing the interface shape. Measured,
    /// it already works, in every arity: the interfaces' explicit static implementations are default
    /// implementations, and a class-level implicit implementation beats them. The register entry is
    /// wrong, and this test is here to keep anyone from "fixing" the interfaces on the strength of it.
    /// </remarks>
    private sealed class MultiVerbGet : IGetEndpoint
    {
        public static string Path => "/";
        public static IEnumerable<string> Methods => ["GET", "HEAD"];
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class MultiVerbTypedGet : ResponseEndpoint, IGetEndpoint<Res>
    {
        public static string Path => "/";
        public static IEnumerable<string> Methods => ["GET", "HEAD"];
        public override Task<IResult> HandleAsync(CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    [Fact]
    public void AClassCanOverrideMethodsWhileImplementingAVerbInterface()
    {
        Assert.Equal(["GET", "HEAD"], MethodsOf<MultiVerbGet>());
        Assert.Equal(["GET", "HEAD"], MethodsOf<MultiVerbTypedGet>());
    }

    /// <summary>
    /// A class implementing two verb interfaces resolves the ambiguity by declaring <c>Methods</c>,
    /// and gets the union it asked for.
    /// </summary>
    /// <remarks>
    /// Without its own <c>Methods</c> the compiler reports CS8705 — neither verb is most specific —
    /// which is a clear, fixable error rather than the "ambiguity with no way out" the register
    /// described.
    /// </remarks>
    private sealed class GetAndPost : IGetEndpoint, IPostEndpoint
    {
        public static string Path => "/";
        public static IEnumerable<string> Methods => ["GET", "POST"];
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    [Fact]
    public void AClassImplementingTwoVerbInterfaces_SuppliesTheUnionItself()
        => Assert.Equal(["GET", "POST"], MethodsOf<GetAndPost>());

    /// <summary>The default <c>Configure</c> hook is a no-op and must not throw.</summary>
    [Fact]
    public void Configure_DefaultImplementation_IsANoOp()
    {
        var app = WebApplication.CreateBuilder([]).Build();
        var builder = app.MapGet("/probe", () => Results.Ok());

        ConfigureVia<GetRaw>(builder);
    }

    private static void ConfigureVia<T>(RouteHandlerBuilder builder)
        where T : IEndpointBase => T.Configure(builder);

    /// <summary>
    /// The assembly-level attribute that overrides the generated mapping method's name.
    /// </summary>
    /// <remarks>
    /// Its usage and multiplicity are part of the contract: the generator reads only the first
    /// occurrence, and <c>AllowMultiple = false</c> is what makes "the first" unambiguous.
    /// </remarks>
    [Fact]
    public void EndpointsMethodNameAttribute_ExposesTheNameAndTargetsAssembliesOnce()
    {
        var attribute = new EndpointsMethodNameAttribute("MapCustomEndpoints");

        Assert.Equal("MapCustomEndpoints", attribute.MethodName);

        var usage = typeof(EndpointsMethodNameAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
