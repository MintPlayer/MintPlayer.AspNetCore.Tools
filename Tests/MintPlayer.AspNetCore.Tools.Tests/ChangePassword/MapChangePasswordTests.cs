using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
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

    /// <summary>
    /// Both overloads hand back an <see cref="IEndpointConventionBuilder"/> for the mapped endpoint.
    /// </summary>
    /// <remarks>
    /// D-M18, fixed. They used to return the <see cref="IEndpointRouteBuilder"/> they were called
    /// on, which is a handle to the whole route table rather than to this endpoint, so no
    /// convention could be attached to it.
    /// </remarks>
    [Fact]
    public void SyncOverload_ReturnsEndpointConventionBuilderForTheMappedEndpoint()
    {
        var app = WebApplication.CreateBuilder([]).Build();

        IEndpointConventionBuilder returned = app.MapChangePassword(() => "/x");

        Assert.NotNull(returned);
    }

    [Fact]
    public void AsyncOverload_ReturnsEndpointConventionBuilderForTheMappedEndpoint()
    {
        var app = WebApplication.CreateBuilder([]).Build();

        IEndpointConventionBuilder returned = app.MapChangePassword(() => Task.FromResult("/x"));

        Assert.NotNull(returned);
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
    /// The caller can exempt the endpoint from a global authorization fallback policy.
    /// </summary>
    /// <remarks>
    /// D-M18, fixed, and the reason the return type changed. An application with a global
    /// authorization fallback policy is exactly the kind of application that needs a
    /// change-password endpoint, and while both overloads returned <c>IEndpointRouteBuilder</c> its
    /// route was blocked with no way to exempt it short of writing the <c>MapGet</c> by hand.
    /// </remarks>
    [Fact]
    public void MappedEndpoint_CanBeGivenAllowAnonymous()
    {
        var endpoints = EndpointsOf(app => app.MapChangePassword(() => "/x").AllowAnonymous());

        Assert.NotNull(Assert.Single(endpoints).Metadata.GetMetadata<IAllowAnonymous>());
    }

    /// <summary>
    /// <c>AllowAnonymous</c> is deliberately NOT applied by default.
    /// </summary>
    /// <remarks>
    /// <c>IAllowAnonymous</c> metadata makes the authorization middleware skip the endpoint's
    /// policy entirely and cannot be undone by the caller afterwards, so applying it by default
    /// would replace one un-exemptable policy with another — an application that genuinely wants
    /// <c>.RequireAuthorization()</c> here could no longer get it. With a
    /// builder for the endpoint returned, opting in costs one call.
    /// </remarks>
    [Fact]
    public void MappedEndpoint_HasNoAuthorizationMetadataByDefault()
    {
        var endpoints = EndpointsOf(app => app.MapChangePassword(() => "/x"));
        var endpoint = Assert.Single(endpoints);

        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Null(endpoint.Metadata.GetMetadata<IAuthorizeData>());
    }

    /// <summary>
    /// The endpoint is named, so it can be found and linked to.
    /// </summary>
    /// <remarks>
    /// Applied by default because a name is additive — a later <c>.WithName(…)</c> replaces it —
    /// and because it makes the endpoint reachable by <c>LinkGenerator</c> and identifiable in
    /// diagnostics, neither of which the caller can retrofit without a handle on the endpoint.
    /// </remarks>
    [Fact]
    public void MappedEndpoint_IsNamedChangePassword()
    {
        var endpoints = EndpointsOf(app => app.MapChangePassword(() => "/x"));

        var metadata = Assert.Single(endpoints).Metadata.GetMetadata<IEndpointNameMetadata>();

        Assert.Equal(MintPlayer.AspNetCore.ChangePassword.EndpointRouteBuilderExtensions.EndpointName, metadata?.EndpointName);
    }

    /// <summary>
    /// A null factory is rejected at map time.
    /// </summary>
    /// <remarks>
    /// D-M20, fixed. It used to be accepted, and the failure landed on the first request instead of
    /// at startup — the one place a configuration mistake is cheap to notice.
    /// </remarks>
    [Fact]
    public void SyncOverload_NullFactory_ThrowsAtMapTime()
    {
        var app = WebApplication.CreateBuilder([]).Build();

        var ex = Assert.Throws<ArgumentNullException>(() => app.MapChangePassword((Func<string>)null!));

        Assert.Equal("changePasswordUrl", ex.ParamName);
    }

    [Fact]
    public void AsyncOverload_NullFactory_ThrowsAtMapTime()
    {
        var app = WebApplication.CreateBuilder([]).Build();

        var ex = Assert.Throws<ArgumentNullException>(() => app.MapChangePassword((Func<Task<string>>)null!));

        Assert.Equal("changePasswordUrl", ex.ParamName);
    }

    /// <summary>
    /// The class extends <see cref="IEndpointRouteBuilder"/>, so it is named after it.
    /// </summary>
    /// <remarks>
    /// D-M21, fixed. It used to be called <c>ApplicationBuilderExtensions</c> while extending
    /// <see cref="IEndpointRouteBuilder"/>, and its docs called the endpoint a "middleware".
    /// Note the fully qualified names in this file: the new name collides by simple name with
    /// <c>Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions</c>, which is reachable
    /// through an implicit global using in every Web SDK project, so naming the class explicitly
    /// requires qualification. Calling the extension methods — the only thing consumers do — is
    /// unaffected, and the sibling <c>MintPlayer.AspNetCore.Endpoints</c> package already carries
    /// the same name.
    /// </remarks>
    [Fact]
    public void ExtensionClass_IsNamedAfterTheTypeItExtends()
    {
        Assert.Equal(
            "EndpointRouteBuilderExtensions",
            typeof(MintPlayer.AspNetCore.ChangePassword.EndpointRouteBuilderExtensions).Name);
    }
}
