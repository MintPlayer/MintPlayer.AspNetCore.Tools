using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using MintPlayer.AspNetCore.NoSniff;
using MintPlayer.AspNetCore.Tools.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.NoSniff;

public class NoSniffMiddlewareTests
{
    /// <summary>
    /// A null <c>next</c> is rejected at construction time.
    /// </summary>
    /// <remarks>
    /// D-M6, fixed. The constructor used to be source-generated from <c>[Inject]</c> with no guard,
    /// so <c>new NoSniffMiddleware(null!)</c> succeeded and failed a request later with an opaque
    /// <see cref="NullReferenceException"/> from inside <c>Invoke</c>. The sibling HSTS middleware
    /// guards; the constructor is now written by hand so this one does too.
    /// </remarks>
    [Fact]
    public void Ctor_NullNext_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new NoSniffMiddleware(null!));

        Assert.Equal("next", ex.ParamName);
    }

    [Fact]
    public async Task Invoke_SetsXContentTypeOptionsNosniff()
    {
        var (context, response) = RecordingResponseFeature.CreateContext();
        var middleware = new NoSniffMiddleware(_ => Task.CompletedTask);

        await middleware.Invoke(context);
        await response.FireOnStartingAsync();

        Assert.Equal("nosniff", response.Headers.XContentTypeOptions);
    }

    [Fact]
    public async Task Invoke_CallsNext()
    {
        var (context, _) = RecordingResponseFeature.CreateContext();
        var nextCalled = 0;
        var middleware = new NoSniffMiddleware(_ =>
        {
            nextCalled++;
            return Task.CompletedTask;
        });

        await middleware.Invoke(context);

        Assert.Equal(1, nextCalled);
    }

    [Fact]
    public async Task Invoke_RegistersExactlyOneOnStartingCallback()
    {
        var (context, response) = RecordingResponseFeature.CreateContext();
        var middleware = new NoSniffMiddleware(_ => Task.CompletedTask);

        await middleware.Invoke(context);

        Assert.Equal(1, response.StartingCallbackCount);
    }

    /// <summary>
    /// The header must not be written until the response actually starts.
    /// </summary>
    /// <remarks>
    /// Deferring to <c>OnStarting</c> is the entire design of this middleware — it is what makes
    /// the header survive a downstream handler that rewrites or clears response headers. Without
    /// the paired <see cref="RecordingResponseFeature.StartingCallbackCount"/> assertion above,
    /// this test would also pass against a middleware that did nothing at all, because
    /// DefaultHttpContext's own response feature silently discards OnStarting callbacks.
    /// </remarks>
    [Fact]
    public async Task Invoke_DoesNotSetHeaderBeforeCallbackFires()
    {
        var (context, response) = RecordingResponseFeature.CreateContext();
        var middleware = new NoSniffMiddleware(_ => Task.CompletedTask);

        await middleware.Invoke(context);

        Assert.True(StringValues.IsNullOrEmpty(response.Headers.XContentTypeOptions));
        Assert.Equal(1, response.StartingCallbackCount);
    }

    [Fact]
    public async Task Invoke_HeaderAlreadySetByCaller_IsOverwrittenWithNosniff()
    {
        var (context, response) = RecordingResponseFeature.CreateContext();
        response.Headers.XContentTypeOptions = "application/json";
        var middleware = new NoSniffMiddleware(_ => Task.CompletedTask);

        await middleware.Invoke(context);
        await response.FireOnStartingAsync();

        Assert.Equal("nosniff", response.Headers.XContentTypeOptions);
    }

    /// <summary>Unlike the HSTS middleware, this one has no HTTPS gate.</summary>
    [Fact]
    public async Task Invoke_PlainHttpRequest_StillSetsHeader()
    {
        var (context, response) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "http";
        var middleware = new NoSniffMiddleware(_ => Task.CompletedTask);

        await middleware.Invoke(context);
        await response.FireOnStartingAsync();

        Assert.Equal("nosniff", response.Headers.XContentTypeOptions);
    }

    [Fact]
    public async Task Invoke_NextThrows_ExceptionPropagates()
    {
        var (context, _) = RecordingResponseFeature.CreateContext();
        var middleware = new NoSniffMiddleware(_ => throw new InvalidOperationException("boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(context));

        Assert.Equal("boom", ex.Message);
    }
}
