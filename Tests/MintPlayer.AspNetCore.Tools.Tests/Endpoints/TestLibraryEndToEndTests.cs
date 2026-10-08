using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.Endpoints;
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

    /// <summary>The library's own startup exception, unwrapped from whatever the host factory wrapped it in.</summary>
    private static Exception? StartupFailure(Exception? thrown)
    {
        for (var current = thrown; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException && current.Message.Contains("GetPath", StringComparison.Ordinal))
                return current;
            if (current is AggregateException { InnerExceptions.Count: 1 } aggregate)
                return StartupFailure(aggregate.InnerExceptions[0]);
        }

        return thrown;
    }

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

    /// <summary>
    /// Issue #38 (AC3): an open library endpoint the application closed is found by its generic
    /// definition, through the container's data source — the way a capabilities endpoint asks.
    /// </summary>
    [Fact]
    public void ClosedLibraryEndpoint_IsReportedAsMapped_ByItsOpenDefinition()
    {
        using var enabled = factory.WithWebHostBuilder(builder => builder.UseSetting(LibPasskeysGroup.EnabledKey, "true"));
        enabled.CreateClient();
        var endpoints = enabled.Services.GetRequiredService<EndpointDataSource>();

        Assert.True(endpoints.IsEndpointMapped(typeof(Passkeys<>)));
        Assert.True(endpoints.IsEndpointMapped(typeof(WhoAmI<>)));
    }

    /// <summary>
    /// AC2 (#36): a group convention that reads configuration in <c>Configure(group, services)</c> is
    /// present when the option is on and absent when it is off — and the routes exist both ways, which
    /// is what <c>IsEnabled</c> could not give.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfigurationDrivenGroupTag_FollowsTheOption_AndTheRoutesExistEitherWay(bool audited)
    {
        using var configured = factory.WithWebHostBuilder(builder => builder.UseSetting(LibPasskeysGroup.EnabledKey, "true").UseSetting(LibAuthGroup.AuditTagKey, audited ? "true" : "false"));
        var client = configured.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/lib/auth/whoami")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/lib/auth/passkeys/5")).StatusCode);

        var whoAmI = Assert.Single(Endpoints(configured.Services), endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "WhoAmI_AppUser");
        var tags = whoAmI.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Http.Metadata.ITagsMetadata>().SelectMany(metadata => metadata.Tags).ToArray();

        Assert.Contains("Library", tags);
        Assert.Equal(audited, tags.Contains("LibraryAudit"));
    }

    /// <summary>
    /// AC4 and AC6 (#37), generated mapping: the closed library endpoint whose <c>GetPath</c> reads
    /// configuration answers on the configured route, and its literal <c>Path</c> 404s.
    /// </summary>
    [Fact]
    public async Task GetPathFromConfiguration_AnswersOnTheConfiguredRoute_NotOnPath()
    {
        using var configured = factory.WithWebHostBuilder(builder => builder.UseSetting(ConfiguredHook<LibUser>.PathKey, "/custom-hooks/{id}"));
        var client = configured.CreateClient();

        Assert.Equal("hook:AppUser:5", await client.GetFromJsonAsync<string>("/lib/auth/custom-hooks/5"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/lib/auth/hooks/5")).StatusCode);
    }

    /// <summary>AC4: with nothing configured, <c>GetPath</c> returns null and the endpoint maps at <c>Path</c>.</summary>
    [Fact]
    public async Task GetPathReturningNull_MapsAtPath()
    {
        var response = await factory.CreateClient().GetAsync("/lib/auth/hooks/5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hook:AppUser:5", await response.Content.ReadFromJsonAsync<string>());
    }

    /// <summary>
    /// AC10 (R2.10), generated mapping: a configured route whose parameter names differ from the
    /// default's fails at startup, naming the endpoint and both patterns.
    /// </summary>
    [Fact]
    public void GetPathWithOtherParameters_FailsAtStartup()
    {
        using var mismatched = factory.WithWebHostBuilder(builder => builder.UseSetting(ConfiguredHook<LibUser>.PathKey, "/x/{key}"));

        // The host factory runs Program on its own thread and may hand the failure back wrapped.
        var thrown = Record.Exception(() => mismatched.CreateClient());
        var failure = Assert.IsAssignableFrom<InvalidOperationException>(StartupFailure(thrown));

        Assert.Contains("ConfiguredHook", failure.Message);
        Assert.Contains("/x/{key}", failure.Message);
        Assert.Contains("/hooks/{id}", failure.Message);
    }

    /// <summary>AC10: literal segments, a constraint and the parameter's case may differ.</summary>
    [Fact]
    public async Task GetPathWithOtherLiteralsConstraintsOrCase_Starts()
    {
        using var configured = factory.WithWebHostBuilder(builder => builder.UseSetting(ConfiguredHook<LibUser>.PathKey, "/custom/{ID:int}"));

        Assert.Equal(HttpStatusCode.OK, (await configured.CreateClient().GetAsync("/lib/auth/custom/7")).StatusCode);
    }

    /// <summary>
    /// AC5 (#37): an endpoint that chooses its route at map time gets no typed link and no client
    /// contract — its composed <c>Path</c> is only the default — and its descriptor is flagged. Its
    /// siblings keep theirs.
    /// </summary>
    [Fact]
    public void GetPathEndpoint_HasNoLinkAndNoContract_AndAFlaggedDescriptor()
    {
        var links = typeof(Program).Assembly.GetType("MintPlayer.AspNetCore.Endpoints.Generated.Routes+LibAuth", throwOnError: true)!;
        Assert.Null(links.GetMethod("ConfiguredHook_AppUser"));
        Assert.NotNull(links.GetMethod("WhoAmI_AppUser"));

        var contractNames = typeof(Program).Assembly.GetCustomAttributes(inherit: false)
            .Where(attribute => attribute.GetType().Name == "EndpointContractAttribute")
            .Select(attribute => (string?)attribute.GetType().GetProperty("Name")!.GetValue(attribute))
            .ToArray();
        Assert.DoesNotContain("ConfiguredHook_AppUser", contractNames);
        Assert.Contains("WhoAmI_AppUser", contractNames);

        var descriptors = (IReadOnlyList<EndpointDescriptor>)typeof(Program).Assembly
            .GetType("MintPlayer.AspNetCore.Endpoints.Generated.TestAppEndpointsExtensions", throwOnError: true)!
            .GetProperty("Endpoints")!.GetValue(null)!;
        var hook = Assert.Single(descriptors, descriptor => descriptor.Name == "ConfiguredHook_AppUser");
        Assert.True(hook.IsPathConfigurable);
        Assert.Equal("/lib/auth/hooks/{id}", hook.Path);
        // ManageAccount<TUser> also overrides GetPath (it counts its calls for the R5 tests), so it is
        // configurable too; every endpoint without an override is not.
        var manage = Assert.Single(descriptors, descriptor => descriptor.Name == "ManageAccount_AppUser");
        Assert.True(manage.IsPathConfigurable);
        Assert.All(descriptors.Where(descriptor => descriptor != hook && descriptor != manage), descriptor => Assert.False(descriptor.IsPathConfigurable));
    }

    /// <summary>
    /// AC11 (R5), generated mapping: an endpoint whose own <c>IsEnabled</c> returns false is not mapped
    /// — 404, reported as not mapped — its <c>GetPath</c> and <c>Configure</c> never run, and its group
    /// siblings are still mapped.
    /// </summary>
    [Fact]
    public async Task DisabledEndpoint_IsNotMapped_AndItsHooksNeverRun()
    {
        using var disabled = factory.WithWebHostBuilder(builder => builder.UseSetting(ManageAccountCalls.EnabledKey, "false"));
        var client = disabled.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/lib/auth/manage")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/lib/auth/whoami")).StatusCode);

        var calls = ManageAccountCalls.For(disabled.Services);
        Assert.Equal(0, calls.GetPath);
        Assert.Equal(0, calls.Configure);

        var endpoints = disabled.Services.GetRequiredService<EndpointDataSource>();
        Assert.False(endpoints.IsEndpointMapped(typeof(ManageAccount<>)));
        Assert.True(endpoints.IsEndpointMapped(typeof(WhoAmI<>)));
    }

    /// <summary>AC11: enabled, the same endpoint is mapped, and each of its hooks runs exactly once.</summary>
    [Fact]
    public async Task EnabledEndpoint_IsMapped_AndItsHooksRunOnce()
    {
        using var enabled = factory.WithWebHostBuilder(builder => builder.UseSetting(ManageAccountCalls.EnabledKey, "true"));
        var client = enabled.CreateClient();

        Assert.Equal("AppUser", await client.GetFromJsonAsync<string>("/lib/auth/manage"));

        var calls = ManageAccountCalls.For(enabled.Services);
        Assert.Equal(1, calls.GetPath);
        Assert.Equal(1, calls.Configure);
        Assert.True(enabled.Services.GetRequiredService<EndpointDataSource>().IsEndpointMapped(typeof(ManageAccount<>)));
    }

    /// <summary>Issue #38 (AC3, R4): a disabled group's endpoint is not mapped; its sibling still is.</summary>
    [Fact]
    public void DisabledGroup_IsReportedAsNotMapped()
    {
        using var disabled = factory.WithWebHostBuilder(builder => builder.UseSetting(LibPasskeysGroup.EnabledKey, "false"));
        disabled.CreateClient();
        var endpoints = disabled.Services.GetRequiredService<EndpointDataSource>();

        Assert.False(endpoints.IsEndpointMapped(typeof(Passkeys<>)));
        Assert.True(endpoints.IsEndpointMapped(typeof(WhoAmI<>)));
    }
}
