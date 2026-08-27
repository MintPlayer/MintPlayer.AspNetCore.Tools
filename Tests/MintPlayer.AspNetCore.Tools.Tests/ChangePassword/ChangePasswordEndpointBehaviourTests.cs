using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MintPlayer.AspNetCore.ChangePassword;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.ChangePassword;

/// <summary>
/// Drives the mapped endpoint's request delegate directly.
/// </summary>
/// <remarks>
/// A plain <see cref="DefaultHttpContext"/> is enough here, unlike the middleware suites: the
/// handler calls <c>Response.Redirect</c>, which writes the status code and Location header
/// eagerly rather than deferring to an <c>OnStarting</c> callback.
/// </remarks>
public class ChangePasswordEndpointBehaviourTests
{
    private static RequestDelegate DelegateOf(Action<IEndpointRouteBuilder> map)
    {
        var app = WebApplication.CreateBuilder([]).Build();
        map(app);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single();

        Assert.NotNull(endpoint.RequestDelegate);
        return endpoint.RequestDelegate!;
    }

    [Fact]
    public async Task SyncOverload_SetsStatusCode302AndLocation()
    {
        var handler = DelegateOf(app => app.MapChangePassword(() => "/account/password"));
        var context = new DefaultHttpContext();

        await handler(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/account/password", context.Response.Headers.Location);
    }

    [Fact]
    public async Task AsyncOverload_SetsStatusCode302AndLocation()
    {
        var handler = DelegateOf(app => app.MapChangePassword(() => Task.FromResult("/account/password")));
        var context = new DefaultHttpContext();

        await handler(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/account/password", context.Response.Headers.Location);
    }

    /// <summary>
    /// Pins 302 rather than 301.
    /// </summary>
    /// <remarks>
    /// <c>Response.Redirect</c> defaults to <c>permanent: false</c>. A permanent redirect would be
    /// wrong here — the change-password URL is an application detail that may move, and browsers
    /// cache 301s aggressively.
    /// </remarks>
    [Fact]
    public async Task Redirect_IsTemporaryNotPermanent()
    {
        var handler = DelegateOf(app => app.MapChangePassword(() => "/x"));
        var context = new DefaultHttpContext();

        await handler(context);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.NotEqual(301, context.Response.StatusCode);
    }

    /// <summary>The async factory must be awaited before the redirect is written.</summary>
    [Fact]
    public async Task AsyncOverload_AwaitsTheFactoryBeforeRedirecting()
    {
        var completion = new TaskCompletionSource<string>();
        var handler = DelegateOf(app => app.MapChangePassword(() => completion.Task));
        var context = new DefaultHttpContext();

        var pending = handler(context);

        Assert.False(pending.IsCompleted);
        Assert.Equal(200, context.Response.StatusCode);

        completion.SetResult("/late");
        await pending;

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/late", context.Response.Headers.Location);
    }

    /// <summary>
    /// The factory runs per request rather than being cached, which is what lets an application
    /// vary the URL by tenant, culture or configuration.
    /// </summary>
    [Fact]
    public async Task Factory_IsInvokedOncePerRequest()
    {
        var calls = 0;
        var handler = DelegateOf(app => app.MapChangePassword(() =>
        {
            calls++;
            return $"/password/{calls}";
        }));

        await handler(new DefaultHttpContext());
        await handler(new DefaultHttpContext());

        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("/account/password")]
    [InlineData("https://accounts.example.com/password")]
    [InlineData("~/password")]
    public async Task ProducedUrl_IsPassedThroughVerbatim(string url)
    {
        var handler = DelegateOf(app => app.MapChangePassword(() => url));
        var context = new DefaultHttpContext();

        await handler(context);

        Assert.Equal(url, context.Response.Headers.Location);
    }

    /// <summary>
    /// Note the explicit cast: a throw-only lambda is convertible to both <c>Func&lt;string&gt;</c>
    /// and <c>Func&lt;Task&lt;string&gt;&gt;</c>, so the two overloads are genuinely ambiguous for
    /// that shape and callers have to disambiguate by hand.
    /// </summary>
    [Fact]
    public async Task SyncFactoryThrows_ExceptionPropagates()
    {
        var handler = DelegateOf(app => app.MapChangePassword(
            (Func<string>)(() => throw new InvalidOperationException("no url configured"))));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler(new DefaultHttpContext()));

        Assert.Equal("no url configured", ex.Message);
    }

    [Fact]
    public async Task AsyncFactoryReturnsFaultedTask_ExceptionPropagates()
    {
        var handler = DelegateOf(app => app.MapChangePassword(
            () => Task.FromException<string>(new InvalidOperationException("no url configured"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler(new DefaultHttpContext()));
    }

    /// <summary>
    /// A null url from the factory fails loudly instead of producing an unfollowable redirect.
    /// </summary>
    /// <remarks>
    /// D-M19, fixed. The measured behaviour was worse than the register predicted:
    /// <c>Response.Redirect(null)</c> does not throw, it writes a 302 and simply omits the
    /// <c>Location</c> header — a syntactically valid response that no browser or password manager
    /// can follow, with nothing recorded anywhere. An empty string differed again, writing the
    /// header empty. Both are now rejected before the redirect is written, so the failure surfaces
    /// as a 500 with a message naming the cause.
    /// </remarks>
    [Fact]
    public async Task FactoryReturnsNull_ThrowsInvalidOperationException()
    {
        var handler = DelegateOf(app => app.MapChangePassword(() => (string)null!));
        var context = new DefaultHttpContext();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler(context));

        Assert.Contains("null", ex.Message);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    /// <summary>
    /// An empty or whitespace url is rejected the same way.
    /// </summary>
    /// <remarks>
    /// D-M19. Whitespace is included because <c>Location: "   "</c> is exactly as unfollowable as
    /// an empty one, and <c>IsNullOrEmpty</c> would have let it through.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("	")]
    public async Task FactoryReturnsEmptyOrWhitespaceUrl_ThrowsInvalidOperationException(string url)
    {
        var handler = DelegateOf(app => app.MapChangePassword(() => url));
        var context = new DefaultHttpContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler(context));

        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task AsyncFactoryReturnsNull_ThrowsInvalidOperationException()
    {
        var handler = DelegateOf(app => app.MapChangePassword(() => Task.FromResult<string>(null!)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler(new DefaultHttpContext()));
    }
}
