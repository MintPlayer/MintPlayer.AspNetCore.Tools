using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.Endpoints.TestLibrary;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// Issue #34 end to end (PRD acceptance 7 and 8): the TestLibrary's endpoints are generic over its
/// user type; the TestApp closes them with <c>[assembly: EndpointTypeArgument&lt;LibUser, AppUser&gt;]</c>
/// and its generated <c>MapTestAppEndpoints()</c> maps them like its own.
/// </summary>
public class TestLibraryEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public TestLibraryEndToEndTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    private static RouteEndpoint[] Endpoints(IServiceProvider services)
        => [.. services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];

    /// <summary>The route value binds through the binder the library's generator emitted into the open class.</summary>
    [Fact]
    public async Task ClosedLibraryEndpoint_BindsItsRouteValue()
    {
        var response = await factory.CreateClient().GetAsync("/lib/auth/passkeys/5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("AppUser:5", await response.Content.ReadFromJsonAsync<string>());
    }

    [Fact]
    public async Task ClosedLibraryEndpoint_RejectsAnUnconvertibleRouteValue()
    {
        var response = await factory.CreateClient().GetAsync("/lib/auth/passkeys/abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RawClosedEndpoint_AndExplicitlyClosedEndpoint_Answer()
    {
        var client = factory.CreateClient();

        Assert.Equal("AppUser", await client.GetFromJsonAsync<string>("/lib/auth/whoami"));
        Assert.Equal("String", await client.GetFromJsonAsync<string>("/lib/echo"));
    }

    /// <summary>PRD D4: a closed endpoint is named <c>{Name}_{TypeArguments}</c>.</summary>
    [Fact]
    public void ClosedEndpoints_AreNamedAfterTheirTypeArguments()
    {
        var names = Endpoints(factory.Services)
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .ToArray();

        Assert.Contains("Passkeys_AppUser", names);
        Assert.Contains("WhoAmI_AppUser", names);
        Assert.Contains("Echo_String", names);
    }

    [Fact]
    public void ClosedEndpoint_HasATypedLink_ThatResolvesByItsName()
    {
        // Routes is internal to the TestApp: Routes.LibAuth.LibPasskeys.Passkeys_AppUser(7), by reflection.
        var links = typeof(Program).Assembly.GetType("MintPlayer.AspNetCore.Endpoints.Generated.Routes+LibAuth+LibPasskeys", throwOnError: true)!;
        var route = (MintPlayer.AspNetCore.Endpoints.EndpointRoute)links.GetMethod("Passkeys_AppUser")!.Invoke(null, [7])!;

        Assert.Equal("/lib/auth/passkeys/7", route.ToString());
        Assert.Equal("/lib/auth/passkeys/7", route.Path(factory.Services.GetRequiredService<LinkGenerator>()));
    }

    /// <summary>The contract a typed client reads names the closed type, its name and the composed route.</summary>
    [Fact]
    public void ClosedEndpoint_HasAContract()
    {
        var contract = Assert.Single(
            typeof(Program).Assembly.GetCustomAttributes(inherit: false),
            attribute => attribute.GetType().Name == "EndpointContractAttribute" &&
                         (string?)attribute.GetType().GetProperty("Name")!.GetValue(attribute) == "Passkeys_AppUser");

        var type = contract.GetType();
        Assert.Equal(typeof(Passkeys<MintPlayer.AspNetCore.Endpoints.TestApp.Models.AppUser>), type.GetProperty("Endpoint")!.GetValue(contract));
        Assert.Equal("/lib/auth/passkeys/{id}", type.GetProperty("Route")!.GetValue(contract));
        Assert.Equal(new[] { typeof(int) }, type.GetProperty("RouteParameterTypes")!.GetValue(contract));
    }

    /// <summary>The generated path documents the path parameter — which the manual MapEndpoint path cannot.</summary>
    [Fact]
    public async Task ClosedEndpoint_IsDocumented_WithItsPathParameter()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json");
        var operation = JsonDocument.Parse(json).RootElement
            .GetProperty("paths").GetProperty("/lib/auth/passkeys/{id}").GetProperty("get");

        Assert.Equal("Passkeys_AppUser", operation.GetProperty("operationId").GetString());
        var parameter = Assert.Single(operation.GetProperty("parameters").EnumerateArray());
        Assert.Equal("id", parameter.GetProperty("name").GetString());
        Assert.Equal("path", parameter.GetProperty("in").GetString());
        Assert.True(parameter.GetProperty("required").GetBoolean());
    }

    /// <summary>
    /// PRD D5: a group whose <c>IsEnabled</c> returns false maps none of its endpoints; its siblings
    /// and its parent's own endpoints are unaffected.
    /// </summary>
    [Fact]
    public async Task DisabledGroup_MapsNoneOfItsEndpoints()
    {
        using var disabled = factory.WithWebHostBuilder(builder => builder.UseSetting(LibPasskeysGroup.EnabledKey, "false"));
        var client = disabled.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/lib/auth/passkeys/5")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/lib/auth/whoami")).StatusCode);
        Assert.DoesNotContain(Endpoints(disabled.Services), endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "Passkeys_AppUser");
    }

    [Fact]
    public async Task EnabledGroup_MapsItsEndpoints()
    {
        using var enabled = factory.WithWebHostBuilder(builder => builder.UseSetting(LibPasskeysGroup.EnabledKey, "true"));

        Assert.Equal(HttpStatusCode.OK, (await enabled.CreateClient().GetAsync("/lib/auth/passkeys/5")).StatusCode);
    }
}
