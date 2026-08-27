using Microsoft.AspNetCore.Http;
using MintPlayer.AspNetCore.Endpoints;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// Covers the <c>HandleAsync(HttpContext)</c> bridge on <see cref="EndpointBase{TRequest}"/>.
/// </summary>
public class EndpointBaseTests
{
    private sealed record Req(int Id);

    /// <summary>A spy over the two abstract members, so the bridge's behaviour is observable.</summary>
    private sealed class SpyEndpoint(
        Func<HttpContext, ValueTask<Req?>> bind,
        Func<Req, CancellationToken, Task<IResult>>? handle = null) : EndpointBase<Req>
    {
        public int BindCalls { get; private set; }
        public int HandleCalls { get; private set; }
        public Req? ReceivedRequest { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }
        public List<string> CallOrder { get; } = [];

        protected override ValueTask<Req?> BindRequestAsync(HttpContext context)
        {
            BindCalls++;
            CallOrder.Add("bind");
            return bind(context);
        }

        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
        {
            HandleCalls++;
            CallOrder.Add("handle");
            ReceivedRequest = request;
            ReceivedToken = cancellationToken;
            return handle?.Invoke(request, cancellationToken) ?? Task.FromResult(Results.Ok());
        }
    }

    [Fact]
    public async Task HandleAsync_BindsThenCallsTypedHandler_InThatOrder()
    {
        var endpoint = new SpyEndpoint(_ => new ValueTask<Req?>(new Req(7)));

        await endpoint.HandleAsync(new DefaultHttpContext());

        Assert.Equal(["bind", "handle"], endpoint.CallOrder);
        Assert.Equal(1, endpoint.BindCalls);
        Assert.Equal(1, endpoint.HandleCalls);
    }

    [Fact]
    public async Task HandleAsync_PassesTheBoundRequestToTheTypedHandler()
    {
        var endpoint = new SpyEndpoint(_ => new ValueTask<Req?>(new Req(42)));

        await endpoint.HandleAsync(new DefaultHttpContext());

        Assert.Equal(new Req(42), endpoint.ReceivedRequest);
    }

    /// <summary>
    /// The token handed to the typed handler is the context's <c>RequestAborted</c>, which is what
    /// lets an endpoint honour client disconnects.
    /// </summary>
    [Fact]
    public async Task HandleAsync_PassesRequestAbortedToTheTypedHandler()
    {
        using var cts = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cts.Token };
        var endpoint = new SpyEndpoint(_ => new ValueTask<Req?>(new Req(1)));

        await endpoint.HandleAsync(context);

        Assert.Equal(cts.Token, endpoint.ReceivedToken);
    }

    [Fact]
    public async Task HandleAsync_ReturnsTheResultFromTheTypedHandler()
    {
        var expected = Results.StatusCode(418);
        var endpoint = new SpyEndpoint(
            _ => new ValueTask<Req?>(new Req(1)),
            (_, _) => Task.FromResult(expected));

        var actual = await endpoint.HandleAsync(new DefaultHttpContext());

        Assert.Same(expected, actual);
    }

    /// <summary>
    /// A null bound request is passed straight through to user code.
    /// </summary>
    /// <remarks>
    /// D-G5. The bridge does <c>HandleAsync(request!, …)</c> — the null-forgiving operator silences
    /// the compiler without changing anything at runtime. An empty body, an unbound GET, or a
    /// formatter that returned no value all produce null here, and the endpoint author's handler
    /// receives a null it was told (by the signature) it would never get. The observable result in
    /// an application is a 500 from inside user code rather than a 400.
    /// </remarks>
    [Fact]
    public async Task HandleAsync_NullBoundRequest_IsPassedToUserCodeAsNull_KnownBug()
    {
        var endpoint = new SpyEndpoint(_ => new ValueTask<Req?>((Req?)null));

        await endpoint.HandleAsync(new DefaultHttpContext());

        Assert.Equal(1, endpoint.HandleCalls);
        Assert.Null(endpoint.ReceivedRequest);
    }

    /// <summary>Binding failures are not translated — there is no try/catch in the bridge.</summary>
    [Fact]
    public async Task HandleAsync_BindThrows_ExceptionPropagatesAndHandlerIsNotCalled()
    {
        var endpoint = new SpyEndpoint(_ => throw new FormatException("bad id"));

        await Assert.ThrowsAsync<FormatException>(() => endpoint.HandleAsync(new DefaultHttpContext()));

        Assert.Equal(0, endpoint.HandleCalls);
    }

    [Fact]
    public async Task HandleAsync_TypedHandlerThrows_ExceptionPropagates()
    {
        var endpoint = new SpyEndpoint(
            _ => new ValueTask<Req?>(new Req(1)),
            (_, _) => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => endpoint.HandleAsync(new DefaultHttpContext()));
    }

    [Fact]
    public void Dispose_DefaultImplementation_IsNoOpAndIdempotent()
    {
        var endpoint = new SpyEndpoint(_ => new ValueTask<Req?>(new Req(1)));

        endpoint.Dispose();
        endpoint.Dispose();
    }

    [Fact]
    public void DisposeAsync_DefaultImplementation_CompletesSynchronously()
    {
        var endpoint = new SpyEndpoint(_ => new ValueTask<Req?>(new Req(1)));

        Assert.True(endpoint.DisposeAsync().IsCompletedSuccessfully);
    }

    /// <summary>
    /// <see cref="EndpointBase{TRequest}"/> implements both disposal interfaces.
    /// </summary>
    /// <remarks>
    /// This is what makes D-G6 possible: both disposal call sites check
    /// <see cref="IAsyncDisposable"/> first, so for anything deriving from this base — which is
    /// every typed endpoint — an overridden <c>Dispose()</c> can never run. Pinned here so the
    /// cause is documented next to the effect.
    /// </remarks>
    [Fact]
    public void EndpointBase_ImplementsBothDisposableInterfaces()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(EndpointBase<Req>)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(EndpointBase<Req>)));
    }

    /// <summary>
    /// <c>NonBodyEndpoint</c> must leave <c>BindRequestAsync</c> abstract.
    /// </summary>
    /// <remarks>
    /// The entire purpose of the type is to force GET and DELETE endpoints to write their own
    /// binding, and it is one deleted keyword away from silently becoming a no-op that binds null.
    /// Reflection is the only way to assert it.
    /// </remarks>
    [Fact]
    public void NonBodyEndpoint_LeavesBindRequestAsyncAbstract()
    {
        var method = typeof(NonBodyEndpoint<Req>).GetMethod(
            "BindRequestAsync",
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

        Assert.NotNull(method);
        Assert.True(method!.IsAbstract);
    }

    [Fact]
    public void HttpMethodBaseClasses_DeriveFromTheRightBinderAndAreAbstract()
    {
        Assert.True(typeof(BodyEndpoint<Req>).IsAssignableFrom(typeof(PostEndpoint<Req>)));
        Assert.True(typeof(BodyEndpoint<Req>).IsAssignableFrom(typeof(PutEndpoint<Req>)));
        Assert.True(typeof(BodyEndpoint<Req>).IsAssignableFrom(typeof(PatchEndpoint<Req>)));
        Assert.True(typeof(NonBodyEndpoint<Req>).IsAssignableFrom(typeof(GetEndpoint<Req>)));
        Assert.True(typeof(NonBodyEndpoint<Req>).IsAssignableFrom(typeof(DeleteEndpoint<Req>)));

        Assert.All<Type>(
            [
                typeof(EndpointBase<Req>), typeof(BodyEndpoint<Req>), typeof(NonBodyEndpoint<Req>),
                typeof(PostEndpoint<Req>), typeof(PutEndpoint<Req>), typeof(PatchEndpoint<Req>),
                typeof(GetEndpoint<Req>), typeof(DeleteEndpoint<Req>),
            ],
            type => Assert.True(type.IsAbstract));
    }
}
