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
    /// A null bound request never reaches the typed handler; the bridge answers 400 instead.
    /// </summary>
    /// <remarks>
    /// The bridge used to do <c>HandleAsync(request!, …)</c> — the null-forgiving operator silences
    /// the compiler without changing anything at runtime. A literal JSON <c>null</c> body, or a
    /// hand-written binder that returns <c>default</c>, then handed user code a null it had been told
    /// (by the signature) it would never get, and the observable result was a 500 from inside the
    /// endpoint author's own code for what is a malformed request.
    /// </remarks>
    [Fact]
    public async Task HandleAsync_NullBoundRequest_ReturnsBadRequest_WithoutCallingTheHandler()
    {
        var endpoint = new SpyEndpoint(_ => new ValueTask<Req?>((Req?)null));

        var result = await endpoint.HandleAsync(new DefaultHttpContext());

        Assert.Equal(0, endpoint.HandleCalls);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    /// <summary>
    /// A binding failure the library raised becomes its own status code, and the handler is skipped.
    /// </summary>
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status415UnsupportedMediaType)]
    public async Task HandleAsync_BindingFailure_ReturnsItsStatusCode(int statusCode)
    {
        var endpoint = new SpyEndpoint(_ => throw new EndpointBindingException(statusCode, "no"));

        var result = await endpoint.HandleAsync(new DefaultHttpContext());

        Assert.Equal(0, endpoint.HandleCalls);
        Assert.Equal(statusCode, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    /// <summary>An endpoint can take over the binding-failure response.</summary>
    private sealed class CustomFailureEndpoint : EndpointBase<Req>
    {
        public EndpointBindingException? SeenFailure { get; private set; }
        public bool WasCalled { get; private set; }

        protected override ValueTask<Req?> BindRequestAsync(HttpContext context)
            => throw new EndpointBindingException(StatusCodes.Status400BadRequest, "bad body");

        protected override ValueTask<IResult> OnBindFailedAsync(HttpContext context, EndpointBindingException? failure)
        {
            WasCalled = true;
            SeenFailure = failure;
            return new(Results.StatusCode(StatusCodes.Status422UnprocessableEntity));
        }

        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    [Fact]
    public async Task OnBindFailedAsync_CanBeOverriddenToChangeTheResponse()
    {
        var endpoint = new CustomFailureEndpoint();

        var result = await endpoint.HandleAsync(new DefaultHttpContext());

        Assert.True(endpoint.WasCalled);
        Assert.Equal("bad body", endpoint.SeenFailure?.Message);
        Assert.Equal(
            StatusCodes.Status422UnprocessableEntity,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    /// <summary>
    /// An exception the <i>endpoint's own</i> binder threw is not translated.
    /// </summary>
    /// <remarks>
    /// Only <see cref="EndpointBindingException"/> is caught. The library has no idea what a
    /// <see cref="FormatException"/> from a hand-written binder means, and guessing 400 would swallow
    /// genuine bugs — an endpoint that wants a status code for its own parse failures throws
    /// <see cref="EndpointBindingException"/> and gets one.
    /// </remarks>
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

    /// <summary>
    /// The forwarding must stay synchronous, or every request pays an allocation to dispose an
    /// endpoint that has nothing to release.
    /// </summary>
    [Fact]
    public void DisposeAsync_DefaultImplementation_CompletesSynchronously()
    {
        var endpoint = new SpyEndpoint(_ => new ValueTask<Req?>(new Req(1)));

        Assert.True(endpoint.DisposeAsync().IsCompletedSuccessfully);
    }

    /// <summary>
    /// <see cref="EndpointBase{TRequest}"/> implements both disposal interfaces, and the async one
    /// forwards to the synchronous one.
    /// </summary>
    /// <remarks>
    /// Both disposal call sites test <see cref="IAsyncDisposable"/> first, and every typed endpoint
    /// inherits both interfaces from this base — so without the forwarding an overridden
    /// <c>Dispose()</c> could never run for any of them. That is a perfectly reasonable thing to
    /// write, and what the compiler's own dispose analysers nudge you toward, so it leaked silently.
    /// </remarks>
    [Fact]
    public async Task DisposeAsync_DefaultImplementation_ForwardsToDispose()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(EndpointBase<Req>)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(EndpointBase<Req>)));

        var endpoint = new SyncOnlyDisposableEndpoint();

        await ((IAsyncDisposable)endpoint).DisposeAsync();

        Assert.Equal(1, endpoint.Disposals);
    }

    /// <summary>Overrides only <c>Dispose()</c>, which is the shape that used to leak.</summary>
    private sealed class SyncOnlyDisposableEndpoint : EndpointBase<Req>
    {
        public int Disposals { get; private set; }

        protected override ValueTask<Req?> BindRequestAsync(HttpContext context) => new(new Req(1));

        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());

        public override void Dispose() => Disposals++;
    }

    /// <summary>
    /// <c>NonBodyEndpoint&lt;TRequest&gt;</c> no longer exists, and <c>GetEndpoint&lt;TRequest&gt;</c> /
    /// <c>DeleteEndpoint&lt;TRequest&gt;</c> derive <c>BodyEndpoint&lt;TRequest&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately rewritten for M4 (PRD R2.14a, R2.14b). This used to pin that
    /// <c>NonBodyEndpoint.BindRequestAsync</c> stayed abstract, forcing GET and DELETE endpoints to
    /// write their own binding. Route values now bind to endpoint properties (R2.1), and a GET or
    /// DELETE that declares a request type is saying it takes a body, so the class lost every user
    /// and was deleted. The pin moves with the truth: if the type came back, or the per-verb bases
    /// fell back to an abstract binder, a two-argument <c>IGetEndpoint&lt;TRequest, TResponse&gt;</c>
    /// would stop binding its body like a POST — so both are asserted by reflection, the only way
    /// to see either.
    /// </remarks>
    [Fact]
    public void NonBodyEndpoint_IsGone_AndBodylessVerbBasesDeriveBodyEndpoint()
    {
        Assert.Null(typeof(EndpointBase<>).Assembly.GetType("MintPlayer.AspNetCore.Endpoints.NonBodyEndpoint`1"));

        Assert.Equal(typeof(BodyEndpoint<Req>), typeof(GetEndpoint<Req>).BaseType);
        Assert.Equal(typeof(BodyEndpoint<Req>), typeof(DeleteEndpoint<Req>).BaseType);

        // BodyEndpoint supplies a concrete binder, so nothing on the GET/DELETE bases is left
        // abstract for the consumer to implement.
        var method = typeof(GetEndpoint<Req>).GetMethod(
            "BindRequestAsync",
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

        Assert.NotNull(method);
        Assert.False(method!.IsAbstract);
        Assert.Equal(typeof(BodyEndpoint<Req>), method.DeclaringType);
    }

    [Fact]
    public void HttpMethodBaseClasses_DeriveFromTheRightBinderAndAreAbstract()
    {
        Assert.True(typeof(BodyEndpoint<Req>).IsAssignableFrom(typeof(PostEndpoint<Req>)));
        Assert.True(typeof(BodyEndpoint<Req>).IsAssignableFrom(typeof(PutEndpoint<Req>)));
        Assert.True(typeof(BodyEndpoint<Req>).IsAssignableFrom(typeof(PatchEndpoint<Req>)));
        Assert.True(typeof(BodyEndpoint<Req>).IsAssignableFrom(typeof(GetEndpoint<Req>)));
        Assert.True(typeof(BodyEndpoint<Req>).IsAssignableFrom(typeof(DeleteEndpoint<Req>)));

        Assert.All<Type>(
            [
                typeof(EndpointBase<Req>), typeof(BodyEndpoint<Req>), typeof(ResponseEndpoint),
                typeof(PostEndpoint<Req>), typeof(PutEndpoint<Req>), typeof(PatchEndpoint<Req>),
                typeof(GetEndpoint<Req>), typeof(DeleteEndpoint<Req>),
            ],
            type => Assert.True(type.IsAbstract));
    }
}
