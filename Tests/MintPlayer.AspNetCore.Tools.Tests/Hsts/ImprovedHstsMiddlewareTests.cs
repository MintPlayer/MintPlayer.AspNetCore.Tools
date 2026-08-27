using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public void Ctor_NullLoggerFactory_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new ImprovedHstsMiddleware(_ => Task.CompletedTask, Options.Create(new HstsOptions()), null!));

        Assert.Equal("loggerFactory", ex.ParamName);
    }

    /// <summary>
    /// Pins the argument-validation order: <c>next</c> is checked first.
    /// </summary>
    /// <remarks>
    /// D-M4, fixed. The guards used to run <c>options</c>-first, so a doubly-null call reported
    /// "options" and pointed the caller at the wrong argument. They now run in parameter order.
    /// </remarks>
    [Fact]
    public void Ctor_NullNextAndNullOptions_ReportsNextFirst()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new ImprovedHstsMiddleware(null!, null!, NullLoggerFactory.Instance));

        Assert.Equal("next", ex.ParamName);
    }

    /// <summary>
    /// There is exactly one public constructor, and it takes the logger factory.
    /// </summary>
    /// <remarks>
    /// D-M2, fixed. There used to be two constructors both starting with <c>RequestDelegate</c>.
    /// <c>UseMiddleware&lt;T&gt;</c> resolves through <c>ActivatorUtilities</c>, which prefers the
    /// greediest satisfiable constructor, so the shorter overload was dead code in every real
    /// application while the pair remained an ambiguity hazard. One constructor removes both.
    /// </remarks>
    [Fact]
    public void Type_HasExactlyOneConstructor()
    {
        var constructors = typeof(ImprovedHstsMiddleware).GetConstructors();

        var parameters = Assert.Single(constructors).GetParameters();

        Assert.Equal(
            [typeof(RequestDelegate), typeof(IOptions<HstsOptions>), typeof(ILoggerFactory)],
            parameters.Select(parameter => parameter.ParameterType));
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

    /// <summary>
    /// Both skip paths are logged.
    /// </summary>
    /// <remarks>
    /// D-M1, fixed. The <see cref="ILoggerFactory"/> used to be accepted and then dropped on the
    /// floor, so the two cases in which no header is written — the request was not HTTPS, or the
    /// host is excluded — happened in total silence, while the framework's own
    /// <c>HstsMiddleware</c> reports both. "Why is there no HSTS header?" is exactly the question
    /// these two messages answer.
    /// </remarks>
    [Fact]
    public async Task Invoke_PlainHttpRequest_LogsThatTheRequestIsInsecure()
    {
        var recorder = new RecordingLoggerFactory();
        var middleware = new ImprovedHstsMiddleware(
            _ => Task.CompletedTask, Options.Create(NoExclusions()), recorder);
        var (context, _) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "http";

        await middleware.Invoke(context);

        Assert.Contains(recorder.Messages, message => message.Contains("insecure"));
    }

    [Fact]
    public async Task Invoke_ExcludedHost_LogsThatTheHostIsExcluded()
    {
        var recorder = new RecordingLoggerFactory();
        var middleware = new ImprovedHstsMiddleware(
            _ => Task.CompletedTask, Options.Create(ExcludingOnly("example.com")), recorder);
        var (context, _) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com");

        await middleware.Invoke(context);

        Assert.Contains(recorder.Messages, message => message.Contains("excluded"));
    }

    [Fact]
    public async Task Invoke_HeaderWritten_LogsNothing()
    {
        var recorder = new RecordingLoggerFactory();
        var middleware = new ImprovedHstsMiddleware(
            _ => Task.CompletedTask, Options.Create(NoExclusions()), recorder);
        var (context, _) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";

        await middleware.Invoke(context);

        Assert.Empty(recorder.Messages);
    }

    /// <summary>
    /// Exclusion is equality only — no wildcards, no suffix matching, no IPv6 normalisation.
    /// </summary>
    /// <remarks>
    /// Not a defect: this is exactly what the framework's <c>HstsMiddleware</c> does, and matching
    /// it is the point of the library. It is pinned (and documented on <c>Invoke</c>) because the
    /// alternative reading — that <c>"*.example.com"</c> or <c>"::1"</c> would work — is a natural
    /// one to make and fails silently by writing the header on a host the caller meant to exclude.
    /// </remarks>
    [Theory]
    [InlineData("api.example.com", "*.example.com")]
    [InlineData("api.example.com", ".example.com")]
    [InlineData("api.example.com", "example.com")]
    [InlineData("[::1]", "::1")]
    public async Task Invoke_ExclusionIsEqualityOnly_StillSetsHeader(string requestHost, string excludedHost)
    {
        var middleware = Create(ExcludingOnly(excludedHost));
        var (context, response) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(requestHost);

        await middleware.Invoke(context);
        await response.FireOnStartingAsync();

        Assert.Equal("max-age=2592000", response.Headers.StrictTransportSecurity);
    }

    /// <summary>Captures every message the middleware logs, at any level.</summary>
    private sealed class RecordingLoggerFactory : ILoggerFactory, ILogger
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
