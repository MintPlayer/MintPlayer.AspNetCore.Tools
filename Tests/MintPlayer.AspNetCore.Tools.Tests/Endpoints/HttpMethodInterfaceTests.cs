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

    private sealed class GetTyped : GetEndpoint<Req>, IGetEndpoint<Req>
    {
        public static string Path => "/";
        protected override ValueTask<Req?> BindRequestAsync(HttpContext context) => new(new Req(1));
        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    private sealed class GetTypedWithResponse : GetEndpoint<Req>, IGetEndpoint<Req, Res>
    {
        public static string Path => "/";
        protected override ValueTask<Req?> BindRequestAsync(HttpContext context) => new(new Req(1));
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

    private sealed class DeleteTyped : DeleteEndpoint<Req>, IDeleteEndpoint<Req>
    {
        public static string Path => "/";
        protected override ValueTask<Req?> BindRequestAsync(HttpContext context) => new(new Req(1));
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
    /// Each access to <c>Methods</c> allocates a fresh array.
    /// </summary>
    /// <remarks>
    /// The collection expression <c>["GET"]</c> in an <c>IEnumerable&lt;string&gt;</c>-returning
    /// property body creates a new <c>string[]</c> per call. Harmless on its own, but it is the
    /// reason <see cref="EndpointDescriptor"/> equality does not behave as a record's usually does
    /// — see <c>EndpointDescriptorTests</c> and D-G16.
    /// </remarks>
    [Fact]
    public void Methods_AllocatesAFreshInstancePerAccess()
    {
        var first = MethodsOf<GetRaw>();
        var second = MethodsOf<GetRaw>();

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

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
}
