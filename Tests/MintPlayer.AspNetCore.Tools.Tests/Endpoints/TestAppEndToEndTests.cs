using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// Drives the sample application end to end, which is the only way to exercise the
/// <i>generated</i> mapping code at runtime.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place <see cref="WebApplicationFactory{TEntryPoint}"/> is used rather than an
/// inline <c>TestServer</c> pipeline, because a real entry point running real generated code is
/// precisely what is under test. The SDK's <c>PublicProgramSourceGenerator</c> makes
/// <c>Program</c> public for top-level statements, so no <c>InternalsVisibleTo</c> is needed.
/// </para>
/// <para>
/// Note these tests contribute no coverage to the generator assembly: the generator runs inside
/// <c>csc</c> at build time, and an analyzer execution is not observed by the test host's profiler.
/// Generator coverage comes exclusively from the in-process driver harness in the sibling test
/// project. What these tests cover is the runtime library plus the correctness of what was emitted.
/// </para>
/// </remarks>
public class TestAppEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public TestAppEndToEndTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    private HttpClient Client => factory.CreateClient();

    [Fact]
    public async Task GetHealth_Returns200()
    {
        var response = await Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetGroupedList_Returns200WithPayload()
    {
        var response = await Client.GetAsync("/api/users/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Alice", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetNestedGroupList_Returns200WithPayload()
    {
        var response = await Client.GetAsync("/api/products/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Widget", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Both <c>/api/users/</c> and <c>/api/users</c> match, so D-G19 is <b>not</b> a defect.
    /// </summary>
    /// <remarks>
    /// Measured, and it disproves the prediction. The composed route pattern really is
    /// <c>/api/users/</c>, but ASP.NET Core routing treats a trailing empty path segment as
    /// equivalent, so the bare form matches too. There is nothing to fix, and this test exists to
    /// stop someone "normalising" the pattern on the strength of reading it.
    /// </remarks>
    [Fact]
    public async Task GetGroupedList_MatchesWithAndWithoutTrailingSlash()
    {
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/users/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task GetTypedEndpoint_BindsRouteValueAndReturnsPayload()
    {
        var response = await Client.GetAsync("/api/users/7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"id\":7", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A non-numeric route value produces a 500, because the sample binds with <c>int.Parse</c> and
    /// the library gives it no binding-failure story.
    /// </summary>
    /// <remarks>
    /// Related to D-G5: there is no hook for "binding failed", so the natural implementation throws
    /// and the framework turns that into a 500 where a 400 is correct. Pinned as an observation
    /// about the library's design rather than a defect in the sample.
    /// </remarks>
    [Fact]
    public async Task GetTypedEndpoint_NonNumericRouteValue_Returns500_KnownGap()
    {
        var response = await Client.GetAsync("/api/users/not-a-number");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task PostTypedEndpoint_WithJsonBody_Returns201()
    {
        var response = await Client.PostAsJsonAsync("/api/users/", new { name = "Carol", email = "carol@example.com" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("Carol", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// An empty POST body reaches the handler as null and faults inside user code.
    /// </summary>
    /// <remarks>
    /// D-G5 observed end to end: <c>ReadFromJsonAsync</c> returns null for a zero-length body, the
    /// bridge launders it through <c>request!</c>, and the handler dereferences it. The client sees
    /// a 500 for what is a malformed request.
    /// </remarks>
    [Fact]
    public async Task PostTypedEndpoint_EmptyBody_Returns500_KnownBug()
    {
        using var content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/users/", content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary>Malformed JSON is likewise a 500 rather than a 400. D-G5b.</summary>
    [Fact]
    public async Task PostTypedEndpoint_MalformedJson_Returns500_KnownBug()
    {
        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/users/", content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task PutTypedEndpoint_WithJsonBody_Returns200()
    {
        var response = await Client.PutAsJsonAsync("/api/users/7", new { name = "Updated", email = "u@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTypedEndpoint_Returns204()
    {
        var response = await Client.DeleteAsync("/api/users/7");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("OPTIONS")]
    [InlineData("HEAD")]
    public async Task MultiMethodEndpoint_HandlesBothVerbs(string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/anything");

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The catch-all multi-method endpoint must not shadow the grouped GET routes.
    /// </summary>
    [Fact]
    public async Task MultiMethodCatchAll_DoesNotShadowGroupedGetRoutes()
    {
        var response = await Client.GetAsync("/api/users/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- metadata emitted by the generated mapping code ----------

    private IReadOnlyList<RouteEndpoint> Endpoints()
        => [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];

    /// <summary>
    /// The group's <c>Configure</c> hook ran, so its endpoints carry the group's tag.
    /// </summary>
    [Fact]
    public void GroupConfigureHook_AppliedTagsToItsEndpoints()
    {
        var usersEndpoints = Endpoints()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/users", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(usersEndpoints);
        Assert.All(usersEndpoints, endpoint =>
        {
            var tags = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.ITagsMetadata>();
            Assert.NotNull(tags);
            Assert.Contains("Users", tags!.Tags);
        });
    }

    /// <summary>
    /// The <c>SuccessStatusCode</c> override is plumbed into <c>Produces</c> metadata, which is the
    /// entire reason the typed-with-response level exists.
    /// </summary>
    [Fact]
    public void TypedWithResponseEndpoint_ProducesMetadataUsesTheOverriddenStatusCode()
    {
        var createUser = Endpoints().Single(endpoint =>
            endpoint.RoutePattern.RawText == "/api/users/"
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true);

        var produces = createUser.Metadata
            .OfType<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>()
            .ToArray();

        Assert.Contains(produces, metadata => metadata.StatusCode == 201);
    }

    [Fact]
    public void TypedWithResponseEndpoint_WithoutOverride_ProducesMetadataDefaultsTo200()
    {
        var getUser = Endpoints().Single(endpoint =>
            endpoint.RoutePattern.RawText == "/api/users/{id}"
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);

        var produces = getUser.Metadata
            .OfType<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>()
            .ToArray();

        Assert.Contains(produces, metadata => metadata.StatusCode == 200);
    }

    /// <summary>
    /// A typed endpoint with no declared response type gets no <c>Produces</c> metadata, because
    /// there is no type to describe.
    /// </summary>
    [Fact]
    public void TypedEndpointWithoutResponseType_HasNoProducesMetadata()
    {
        var updateUser = Endpoints().Single(endpoint =>
            endpoint.RoutePattern.RawText == "/api/users/{id}"
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("PUT") == true);

        Assert.Empty(updateUser.Metadata.OfType<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>());
    }

    [Fact]
    public void GeneratedMapping_RegisteredEveryEndpointInTheSample()
    {
        var patterns = Endpoints().Select(endpoint => endpoint.RoutePattern.RawText).ToArray();

        Assert.Contains("/health", patterns);
        Assert.Contains("/api/users/", patterns);
        Assert.Contains("/api/users/{id}", patterns);
        Assert.Contains("/api/products/", patterns);
        Assert.Contains("/api/{**path}", patterns);
    }
}
