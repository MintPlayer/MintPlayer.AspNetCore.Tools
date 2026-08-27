using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.Hsts;
using MintPlayer.AspNetCore.Tools.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Hsts;

public class ImprovedHstsMiddlewareTests
{
    private static ImprovedHstsMiddleware Create(HstsOptions options, RequestDelegate? next = null)
        => new(next ?? (_ => Task.CompletedTask), Options.Create(options), NullLoggerFactory.Instance);

    /// <summary>
    /// Options with the framework's default exclusions removed.
    /// </summary>
    /// <remarks>
    /// <c>HstsOptions.ExcludedHosts</c> is a get-only property pre-populated with localhost,
    /// 127.0.0.1 and [::1], so a collection initializer <i>adds</i> to that list rather than
    /// replacing it. Tests that mean "no exclusions" have to clear it explicitly.
    /// </remarks>
    private static HstsOptions NoExclusions()
    {
        var options = new HstsOptions();
        options.ExcludedHosts.Clear();
        return options;
    }

    private static HstsOptions ExcludingOnly(string host)
    {
        var options = new HstsOptions();
        options.ExcludedHosts.Clear();
        options.ExcludedHosts.Add(host);
        return options;
    }

    [Fact]
    public void Ctor_NullOptions_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new ImprovedHstsMiddleware(_ => Task.CompletedTask, null!, NullLoggerFactory.Instance));

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public void Ctor_NullNext_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new ImprovedHstsMiddleware(null!, Options.Create(new HstsOptions()), NullLoggerFactory.Instance));

        Assert.Equal("next", ex.ParamName);
    }

    /// <summary>
    /// Pins the argument-validation order: <c>options</c> is checked before <c>next</c>.
    /// </summary>
    /// <remarks>
    /// D-M4. With both arguments null the reported parameter name is "options", which points the
    /// caller at the wrong argument. Cosmetic, but the order is observable, so it is pinned here
    /// and the test flips when the guards are reordered.
    /// </remarks>
    [Fact]
    public void Ctor_NullNextAndNullOptions_ReportsOptionsFirst_KnownGap()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new ImprovedHstsMiddleware(null!, null!, NullLoggerFactory.Instance));

        Assert.Equal("options", ex.ParamName);
    }

    /// <summary>
    /// The two-argument overload is the only way to reach the <c>NullLoggerFactory</c> path.
    /// </summary>
    /// <remarks>
    /// D-M2: <c>UseMiddleware&lt;T&gt;</c> resolves through <c>ActivatorUtilities</c>, which prefers
    /// the greediest satisfiable constructor — and an <c>ILoggerFactory</c> is registered in every
    /// real application. So this overload is dead code in practice and is only reachable by
    /// constructing the middleware directly.
    /// </remarks>
    [Fact]
    public async Task Ctor_TwoArgumentOverload_UsesNullLoggerFactoryAndWorks()
    {
        var middleware = new ImprovedHstsMiddleware(_ => Task.CompletedTask, Options.Create(NoExclusions()));
        var (context, response) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";

        await middleware.Invoke(context);
        await response.FireOnStartingAsync();

        Assert.False(StringValues.IsNullOrEmpty(response.Headers.StrictTransportSecurity));
    }

    [Fact]
    public async Task Invoke_PlainHttpRequest_DoesNotRegisterOnStarting()
    {
        var middleware = Create(NoExclusions());
        var (context, response) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "http";

        await middleware.Invoke(context);

        Assert.Equal(0, response.StartingCallbackCount);
    }

    [Fact]
    public async Task Invoke_PlainHttpRequest_CallsNext()
    {
        var nextCalled = 0;
        var middleware = Create(NoExclusions(), _ => { nextCalled++; return Task.CompletedTask; });
        var (context, _) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "http";

        await middleware.Invoke(context);

        Assert.Equal(1, nextCalled);
    }

    [Fact]
    public async Task Invoke_HttpsRequest_SetsStrictTransportSecurity()
    {
        var middleware = Create(NoExclusions());
        var (context, response) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";

        await middleware.Invoke(context);
        await response.FireOnStartingAsync();

        Assert.Equal("max-age=2592000", response.Headers.StrictTransportSecurity);
    }

    [Fact]
    public async Task Invoke_HttpsRequest_CallsNext()
    {
        var nextCalled = 0;
        var middleware = Create(NoExclusions(), _ => { nextCalled++; return Task.CompletedTask; });
        var (context, _) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";

        await middleware.Invoke(context);

        Assert.Equal(1, nextCalled);
    }

    /// <summary>
    /// Host exclusion is <see cref="StringComparison.OrdinalIgnoreCase"/> against
    /// <c>Request.Host.Host</c>, which excludes the port.
    /// </summary>
    [Theory]
    [InlineData("localhost", "localhost")]
    [InlineData("LOCALHOST", "localhost")]
    [InlineData("localhost", "LOCALHOST")]
    [InlineData("localhost:5001", "localhost")]
    public async Task Invoke_ExcludedHost_DoesNotSetHeader(string requestHost, string excludedHost)
    {
        var middleware = Create(ExcludingOnly(excludedHost));
        var (context, response) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(requestHost);

        await middleware.Invoke(context);

        Assert.Equal(0, response.StartingCallbackCount);
    }

    [Fact]
    public async Task Invoke_NonExcludedHost_SetsHeader()
    {
        var middleware = Create(ExcludingOnly("localhost"));
        var (context, response) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com");

        await middleware.Invoke(context);
        await response.FireOnStartingAsync();

        Assert.Equal("max-age=2592000", response.Headers.StrictTransportSecurity);
    }

    /// <summary>
    /// The framework's default exclusions must keep working, since callers rely on
    /// <c>new HstsOptions()</c> not breaking local development over HTTPS.
    /// </summary>
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public async Task Invoke_DefaultOptions_ExcludeLoopbackHosts(string host)
    {
        var middleware = Create(new HstsOptions());
        var (context, response) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(host);

        await middleware.Invoke(context);

        Assert.Equal(0, response.StartingCallbackCount);
    }

    [Fact]
    public async Task Invoke_HeaderAlreadyPresent_IsOverwritten()
    {
        var middleware = Create(NoExclusions());
        var (context, response) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";
        response.Headers.StrictTransportSecurity = "max-age=1";

        await middleware.Invoke(context);
        await response.FireOnStartingAsync();

        Assert.Equal("max-age=2592000", response.Headers.StrictTransportSecurity);
    }

    /// <summary>
    /// The callback is registered before <c>next</c> runs, so it survives a throwing pipeline.
    /// </summary>
    /// <remarks>
    /// This ordering is the whole point of the library: registering with the response rather than
    /// writing the header directly is what makes it survive downstream handlers.
    /// </remarks>
    [Fact]
    public async Task Invoke_NextThrows_CallbackWasStillRegistered()
    {
        var middleware = Create(NoExclusions(), _ => throw new InvalidOperationException("boom"));
        var (context, response) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(context));

        Assert.Equal(1, response.StartingCallbackCount);
    }

}
