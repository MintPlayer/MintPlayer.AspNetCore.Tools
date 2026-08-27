using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MintPlayer.AspNetCore.Endpoints;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// Covers <c>MapEndpoint&lt;TEndpoint&gt;</c>'s registration-time behaviour.
/// </summary>
public class MapEndpointTests
{
    // Fully qualified deliberately: the library's EndpointNameAttribute collides by simple name
    // with the framework's Microsoft.AspNetCore.Routing.EndpointNameAttribute, and a Web SDK
    // project has the latter in scope via implicit usings. So `[EndpointName("x")]` in ordinary
    // consumer code is a CS0104 ambiguity error. Recorded as D-G26.
    [MintPlayer.AspNetCore.Endpoints.EndpointNameAttribute("health")]
    [Obsolete("kept for the attribute-transfer test")]
    private sealed class HealthEndpoint : IGetEndpoint
    {
        public static string Path => "/health";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok("healthy"));
    }

    private sealed class PreflightEndpoint : IEndpoint
    {
        public static string Path => "/api/{**rest}";
        public static IEnumerable<string> Methods => ["OPTIONS", "HEAD"];
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class ConfiguredEndpoint : IGetEndpoint
    {
        public static string Path => "/configured";
        public static void Configure(RouteHandlerBuilder builder) => builder.WithName("configured-by-hook");
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class CountingEndpoint : IGetEndpoint
    {
        public static int Constructions;
        public CountingEndpoint() => Interlocked.Increment(ref Constructions);
        public static string Path => "/counting";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class UsersGroup : IEndpointGroup
    {
        public static string Prefix => "/api/users";
    }

    private sealed class ListUsersEndpoint : IGetEndpoint, IMemberOf<UsersGroup>
    {
        public static string Path => "/";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private static IReadOnlyList<RouteEndpoint> Map<TEndpoint>() where TEndpoint : class, IEndpoint
    {
        var app = WebApplication.CreateBuilder([]).Build();
        app.MapEndpoint<TEndpoint>();

        return [.. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()];
    }

    [Fact]
    public void MapEndpoint_RegistersRouteAtTheEndpointsPath()
        => Assert.Equal("/health", Assert.Single(Map<HealthEndpoint>()).RoutePattern.RawText);

    [Fact]
    public void MapEndpoint_RegistersTheDeclaredHttpMethod()
    {
        var metadata = Assert.Single(Map<HealthEndpoint>()).Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.Equal(["GET"], metadata!.HttpMethods);
    }

    /// <summary>A multi-method endpoint produces one route carrying both verbs.</summary>
    [Fact]
    public void MapEndpoint_MultiMethodEndpoint_RegistersOneRouteWithAllVerbs()
    {
        var endpoint = Assert.Single(Map<PreflightEndpoint>());
        var metadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.Equal(["OPTIONS", "HEAD"], metadata!.HttpMethods);
    }

    /// <summary>
    /// Returns the route builder, not the <c>RouteHandlerBuilder</c>.
    /// </summary>
    /// <remarks>
    /// Chaining works, but the caller never gets a handle to the mapped route, so conventions can
    /// only be applied through the static <c>Configure</c> hook.
    /// </remarks>
    [Fact]
    public void MapEndpoint_ReturnsTheSameBuilderForChaining()
    {
        var app = WebApplication.CreateBuilder([]).Build();

        Assert.Same(app, app.MapEndpoint<HealthEndpoint>());
    }

    [Fact]
    public void MapEndpoint_TransfersClassLevelAttributesToEndpointMetadata()
    {
        var endpoint = Assert.Single(Map<HealthEndpoint>());

        Assert.Equal("health", endpoint.Metadata.GetMetadata<MintPlayer.AspNetCore.Endpoints.EndpointNameAttribute>()?.Name);
        Assert.NotNull(endpoint.Metadata.GetMetadata<ObsoleteAttribute>());
    }

    /// <summary>
    /// Attribute transfer uses <c>GetCustomAttributes(true)</c>, so compiler-generated attributes
    /// come along too.
    /// </summary>
    /// <remarks>
    /// D-G8. On a nullable-enabled class, <c>NullableAttribute</c>/<c>NullableContextAttribute</c>
    /// become endpoint metadata. Harmless in itself, but it pollutes <c>endpoint.Metadata</c> for
    /// any consumer that enumerates or filters it.
    /// </remarks>
    [Fact]
    public void MapEndpoint_AlsoTransfersCompilerGeneratedAttributes_KnownGap()
    {
        var endpoint = Assert.Single(Map<HealthEndpoint>());

        var hasCompilerGenerated = endpoint.Metadata.Any(metadata =>
            metadata is not null
            && metadata.GetType().Namespace == "System.Runtime.CompilerServices");

        Assert.True(hasCompilerGenerated,
            "expected at least one compiler-generated attribute to have leaked into metadata");
    }

    [Fact]
    public void MapEndpoint_InvokesTheStaticConfigureHook()
    {
        var endpoint = Assert.Single(Map<ConfiguredEndpoint>());

        Assert.Equal("configured-by-hook", endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
    }

    /// <summary>
    /// The endpoint type is not instantiated until a request is dispatched.
    /// </summary>
    /// <remarks>
    /// Registration builds an <c>ObjectFactory</c> but must not construct anything: endpoints are
    /// resolved per request from the request scope, so constructing one at startup would capture
    /// the wrong scope and defeat the design.
    /// </remarks>
    [Fact]
    public void MapEndpoint_DoesNotConstructTheEndpointAtRegistrationTime()
    {
        CountingEndpoint.Constructions = 0;

        _ = Map<CountingEndpoint>();

        Assert.Equal(0, CountingEndpoint.Constructions);
    }

    /// <summary>
    /// Manual registration ignores group membership.
    /// </summary>
    /// <remarks>
    /// D-G14. <c>MapEndpoint</c> maps at <c>TEndpoint.Path</c> verbatim, so an endpoint declaring
    /// <c>IMemberOf&lt;UsersGroup&gt;</c> with <c>Path =&gt; "/"</c> lands at <c>/</c> rather than at
    /// <c>/api/users/</c>. The README advertises manual registration without this caveat, so an
    /// application mixing generated and manual registration gets two different routes for the same
    /// endpoint class depending on how it was mapped.
    /// </remarks>
    [Fact]
    public void MapEndpoint_IgnoresGroupMembership_KnownBug()
    {
        var endpoint = Assert.Single(Map<ListUsersEndpoint>());

        Assert.Equal("/", endpoint.RoutePattern.RawText);
        Assert.DoesNotContain("api/users", endpoint.RoutePattern.RawText);
    }
}
