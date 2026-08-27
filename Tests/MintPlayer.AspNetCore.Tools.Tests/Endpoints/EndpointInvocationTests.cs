using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MintPlayer.AspNetCore.Endpoints;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// Covers the request-time half of <c>MapEndpoint</c>: per-request resolution, dependency
/// injection from the request scope, and disposal.
/// </summary>
/// <remarks>
/// This half is only reachable by dispatching a real request, because it lives inside the route
/// handler delegate. An inline <c>TestServer</c> pipeline is used rather than
/// <c>WebApplicationFactory</c> so the fixtures can live in this assembly.
/// </remarks>
public class EndpointInvocationTests
{
    /// <summary>Records what happened, across requests, for a single test.</summary>
    private sealed class Journal
    {
        public int Constructions;
        public int Disposals;
        public int AsyncDisposals;
        public List<int> ScopedInstanceIds { get; } = [];
    }

    private sealed class ScopedThing
    {
        private static int next;
        public int Id { get; } = Interlocked.Increment(ref next);
    }

    private sealed class InjectedEndpoint(Journal journal, ScopedThing thing) : IGetEndpoint
    {
        public static string Path => "/injected";

        public Task<IResult> HandleAsync(HttpContext httpContext)
        {
            Interlocked.Increment(ref journal.Constructions);
            journal.ScopedInstanceIds.Add(thing.Id);
            return Task.FromResult(Results.Ok(thing.Id));
        }
    }

    private sealed record Empty;

    /// <summary>Overrides both disposal members, so the test can see which one the library calls.</summary>

    private sealed class BothDisposableEndpoint(Journal journal) : EndpointBase<Empty>, IGetEndpoint
    {
        public static string Path => "/both-disposable";

        protected override ValueTask<Empty?> BindRequestAsync(HttpContext context) => new(new Empty());

        public override Task<IResult> HandleAsync(Empty request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());

        public override void Dispose() => Interlocked.Increment(ref journal.Disposals);

        public override ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref journal.AsyncDisposals);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// A typed endpoint that overrides only <c>Dispose()</c> — the shape whose cleanup used to be
    /// unreachable.
    /// </summary>
    private sealed class SyncOnlyDisposableEndpoint(Journal journal) : EndpointBase<Empty>, IGetEndpoint
    {
        public static string Path => "/sync-disposable";

        protected override ValueTask<Empty?> BindRequestAsync(HttpContext context) => new(new Empty());

        public override Task<IResult> HandleAsync(Empty request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());

        public override void Dispose() => Interlocked.Increment(ref journal.Disposals);
    }

    /// <summary>Implements only <see cref="IDisposable"/>, so the sync branch is reachable.</summary>
    private sealed class OnlyDisposableEndpoint(Journal journal) : IGetEndpoint, IDisposable
    {
        public static string Path => "/only-disposable";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        public void Dispose() => Interlocked.Increment(ref journal.Disposals);
    }

    private sealed class ThrowingEndpoint(Journal journal) : IGetEndpoint, IDisposable
    {
        public static string Path => "/throwing";
        public Task<IResult> HandleAsync(HttpContext httpContext) => throw new InvalidOperationException("boom");
        public void Dispose() => Interlocked.Increment(ref journal.Disposals);
    }

    private sealed class NeedsMissingServiceEndpoint(IDisposable missing) : IGetEndpoint
    {
        public static string Path => "/missing";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(missing.GetHashCode()));
    }

    private static async Task<IHost> StartHost<TEndpoint>(Journal journal)
        where TEndpoint : class, IEndpoint
        => await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(journal);
                    services.AddScoped<ScopedThing>();
                    services.AddRouting();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapEndpoint<TEndpoint>());
                }))
            .StartAsync();

    [Fact]
    public async Task Invoke_ResolvesANewEndpointPerRequest()
    {
        var journal = new Journal();
        using var host = await StartHost<InjectedEndpoint>(journal);
        var client = host.GetTestClient();

        await client.GetAsync("/injected");
        await client.GetAsync("/injected");

        Assert.Equal(2, journal.Constructions);
    }

    /// <summary>
    /// Constructor dependencies come from the <i>request</i> scope, so a scoped service differs
    /// between requests and is shared within one.
    /// </summary>
    [Fact]
    public async Task Invoke_InjectsFromTheRequestScope()
    {
        var journal = new Journal();
        using var host = await StartHost<InjectedEndpoint>(journal);
        var client = host.GetTestClient();

        await client.GetAsync("/injected");
        await client.GetAsync("/injected");

        Assert.Equal(2, journal.ScopedInstanceIds.Count);
        Assert.NotEqual(journal.ScopedInstanceIds[0], journal.ScopedInstanceIds[1]);
    }

    [Fact]
    public async Task Invoke_ReturnsTheHandlersResultToThePipeline()
    {
        var journal = new Journal();
        using var host = await StartHost<InjectedEndpoint>(journal);

        var response = await host.GetTestClient().GetAsync("/injected");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// For an endpoint that overrides both, only <c>DisposeAsync</c> runs — that is the normal
    /// contract, and an override that does not call <c>base.DisposeAsync()</c> takes over completely.
    /// </summary>
    [Fact]
    public async Task Invoke_EndpointOverridingBoth_OnlyDisposeAsyncRuns()
    {
        var journal = new Journal();
        using var host = await StartHost<BothDisposableEndpoint>(journal);

        await host.GetTestClient().GetAsync("/both-disposable");

        Assert.Equal(1, journal.AsyncDisposals);
        Assert.Equal(0, journal.Disposals);
    }

    /// <summary>
    /// A typed endpoint that overrides only <c>Dispose()</c> is disposed.
    /// </summary>
    /// <remarks>
    /// Both disposal sites test <see cref="IAsyncDisposable"/> first, and every typed endpoint
    /// inherits both interfaces from <see cref="EndpointBase{TRequest}"/> — so releasing resources in
    /// <c>Dispose()</c>, which is a perfectly reasonable thing to write and what the compiler's own
    /// dispose analysers nudge you toward, leaked silently on every request. The base class's
    /// <c>DisposeAsync</c> now forwards, so the sync override runs wherever the async path is taken.
    /// </remarks>
    [Fact]
    public async Task Invoke_TypedEndpointOverridingOnlyDispose_IsStillDisposed()
    {
        var journal = new Journal();
        using var host = await StartHost<SyncOnlyDisposableEndpoint>(journal);

        await host.GetTestClient().GetAsync("/sync-disposable");

        Assert.Equal(1, journal.Disposals);
    }

    [Fact]
    public async Task Invoke_EndpointImplementingOnlyIDisposable_IsDisposed()
    {
        var journal = new Journal();
        using var host = await StartHost<OnlyDisposableEndpoint>(journal);

        await host.GetTestClient().GetAsync("/only-disposable");

        Assert.Equal(1, journal.Disposals);
    }

    /// <summary>Disposal happens in a finally block, so a throwing handler still releases.</summary>
    [Fact]
    public async Task Invoke_HandlerThrows_EndpointIsStillDisposed()
    {
        var journal = new Journal();
        using var host = await StartHost<ThrowingEndpoint>(journal);

        await Assert.ThrowsAnyAsync<Exception>(() => host.GetTestClient().GetAsync("/throwing"));

        Assert.Equal(1, journal.Disposals);
    }

    /// <summary>
    /// An unsatisfiable constructor dependency fails at request time, not at startup.
    /// </summary>
    /// <remarks>
    /// <c>ActivatorUtilities.CreateFactory&lt;T&gt;(Type.EmptyTypes)</c> builds the factory eagerly
    /// but resolves services lazily, so a missing registration surfaces on the first request as a
    /// 500 rather than as a startup failure. Worth pinning because it is the difference between a
    /// misconfiguration you find in CI and one you find in production.
    /// </remarks>
    [Fact]
    public async Task Invoke_UnregisteredDependency_FailsAtRequestTimeNotStartup()
    {
        var journal = new Journal();

        // Startup succeeds despite IDisposable never being registered.
        using var host = await StartHost<NeedsMissingServiceEndpoint>(journal);

        await Assert.ThrowsAnyAsync<Exception>(() => host.GetTestClient().GetAsync("/missing"));
    }
}
