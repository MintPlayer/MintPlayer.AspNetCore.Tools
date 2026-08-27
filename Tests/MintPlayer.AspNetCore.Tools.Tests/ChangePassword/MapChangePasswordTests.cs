using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MintPlayer.AspNetCore.ChangePassword;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.ChangePassword;

/// <summary>
/// Route-table assertions for both <c>MapChangePassword</c> overloads.
/// </summary>
/// <remarks>
/// <c>WebApplication</c> implements <see cref="IEndpointRouteBuilder"/>, so building one gives a
/// real endpoint builder and a real route table without starting a server or binding a socket.
/// </remarks>
public class MapChangePasswordTests
{
    private const string WellKnownPath = "/.well-known/change-password";

    private static IReadOnlyList<RouteEndpoint> EndpointsOf(Action<IEndpointRouteBuilder> map)
    {
        var app = WebApplication.CreateBuilder([]).Build();
        map(app);

        return [.. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()];
    }

    [Fact]
    public void SyncOverload_MapsRouteAtWellKnownChangePassword()
    {
        var endpoints = EndpointsOf(app => app.MapChangePassword(() => "/account/password"));

        Assert.Equal(WellKnownPath, Assert.Single(endpoints).RoutePattern.RawText);
    }

    [Fact]
    public void AsyncOverload_MapsRouteAtWellKnownChangePassword()
    {
        var endpoints = EndpointsOf(app => app.MapChangePassword(() => Task.FromResult("/account/password")));

        Assert.Equal(WellKnownPath, Assert.Single(endpoints).RoutePattern.RawText);
    }

    /// <summary>
    /// The path is fixed by the well-known URI registry, so a typo here is a silent feature
    /// regression: password managers would simply never find the endpoint.
    /// </summary>
    [Fact]
    public void MappedPath_MatchesTheWellKnownUriRegistryExactly()
    {
        var endpoints = EndpointsOf(app => app.MapChangePassword(() => "/x"));

        Assert.Equal("/.well-known/change-password", Assert.Single(endpoints).RoutePattern.RawText);
    }

    [Fact]
    public void SyncOverload_MapsGetMethodOnly()
    {
        var endpoints = EndpointsOf(app => app.MapChangePassword(() => "/x"));

        var metadata = Assert.Single(endpoints).Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal(["GET"], metadata!.HttpMethods);
    }

    [Fact]
    public void AsyncOverload_MapsGetMethodOnly()
    {
        var endpoints = EndpointsOf(app => app.MapChangePassword(() => Task.FromResult("/x")));

        var metadata = Assert.Single(endpoints).Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal(["GET"], metadata!.HttpMethods);
    }

    [Fact]
    public void SyncOverload_ReturnsSameBuilderForChaining()
    {
        var app = WebApplication.CreateBuilder([]).Build();

        var returned = app.MapChangePassword(() => "/x");

        Assert.Same(app, returned);
    }

    [Fact]
    public void AsyncOverload_ReturnsSameBuilderForChaining()
    {
        var app = WebApplication.CreateBuilder([]).Build();

        var returned = app.MapChangePassword(() => Task.FromResult("/x"));

        Assert.Same(app, returned);
    }

    [Fact]
    public void BothOverloads_RegisterExactlyOneEndpointEach()
    {
        var endpoints = EndpointsOf(app =>
        {
            app.MapChangePassword(() => "/sync");
            app.MapChangePassword(() => Task.FromResult("/async"));
        });

        Assert.Equal(2, endpoints.Count);
    }

    /// <summary>
    /// The endpoint carries no authorization metadata and cannot be given any.
    /// </summary>
    /// <remarks>
    /// D-M18. Both overloads return <see cref="IEndpointRouteBuilder"/> rather than
    /// <c>RouteHandlerBuilder</c>, so the caller has no handle to attach conventions to — in
    /// particular no way to call <c>.AllowAnonymous()</c>. An application with a global
    /// authorization fallback policy, which is exactly the kind of application that needs a
    /// change-password endpoint, will therefore have this route blocked with no way to exempt it
    /// short of writing the <c>MapGet</c> by hand.
    /// </remarks>
    [Fact]
    public void MappedEndpoint_HasNoAuthorizationMetadataAndCannotBeGivenAny_KnownGap()
    {
        var endpoints = EndpointsOf(app => app.MapChangePassword(() => "/x"));
        var endpoint = Assert.Single(endpoints);

        Assert.Null(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>());

        // The return type is the builder itself, not a RouteHandlerBuilder, which is what makes
        // the gap unfixable from the caller's side.
        var app2 = WebApplication.CreateBuilder([]).Build();
        Assert.IsAssignableFrom<IEndpointRouteBuilder>(app2.MapChangePassword(() => "/x"));
    }
}
