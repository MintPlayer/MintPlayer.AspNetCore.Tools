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
    /// A non-numeric route value is a 400 naming the parameter, the value and the expected type
    /// (R2.11) — not a 500, and not a 404.
    /// </summary>
    /// <remarks>
    /// Since M4 the sample writes no binder at all: <c>GetUser</c> declares <c>[RouteParam] int Id</c>
    /// and the generated <c>BindParameters</c> override rejects the value. Before M4 the sample's own
    /// hand-written binder threw <c>EndpointBindingException</c>; left unsaid, this was a 500. No route
    /// constraint is emitted (R2.10), which is why this is a 400 rather than a 404.
    /// </remarks>
    [Fact]
    public async Task GetTypedEndpoint_NonNumericRouteValue_Returns400WithTheReason()
    {
        var response = await Client.GetAsync("/api/users/abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The route parameter 'Id' must be a valid Int32; 'abc' is not.", await DetailOf(response));
    }

    /// <summary>A nested endpoint in a partial container binds its route value too.</summary>
    [Fact]
    public async Task NestedResponseOnlyEndpoint_BindsRouteValue()
    {
        var response = await Client.GetAsync("/api/users/nested/42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"id\":42", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The raw <c>ListUsers</c> endpoint's <c>[QueryParam] int Page = 1</c>: absent keeps the
    /// initializer, present binds, malformed is a 400.
    /// </summary>
    /// <remarks>
    /// The initializer is the default only because the generator emits the <c>Try…</c> form for a
    /// property that has one. The required form would make the bare URL a 400; the optional form
    /// would overwrite 1 with 0. Raw endpoints bind through the generated explicit
    /// <c>IParameterBinder</c>, which the generated <c>Map&lt;TEndpoint&gt;</c> calls before
    /// <c>HandleAsync</c>.
    /// </remarks>
    [Fact]
    public async Task RawListEndpoint_QueryParameter_DefaultsBindsAndRejects()
    {
        Assert.Contains("\"page\":1", await Client.GetStringAsync("/api/users"));
        Assert.Contains("\"page\":3", await Client.GetStringAsync("/api/users?page=3"));

        var bad = await Client.GetAsync("/api/users?page=xyz");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("The query parameter 'Page' must be a valid Int32; 'xyz' is not.", await DetailOf(bad));
    }

    private static async Task<string?> DetailOf(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString();
    }

    [Fact]
    public async Task PostTypedEndpoint_WithJsonBody_Returns201()
    {
        var response = await Client.PostAsJsonAsync("/api/users/", new { name = "Carol", email = "carol@example.com" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("Carol", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// An empty POST body is a 400.
    /// </summary>
    /// <remarks>
    /// It used to be a 500: <c>ReadFromJsonAsync</c> throws on a zero-length body, nothing caught it,
    /// and the client was told the server had failed.
    /// </remarks>
    [Fact]
    public async Task PostTypedEndpoint_EmptyBody_Returns400()
    {
        using var content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/users/", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Malformed JSON is likewise a 400 rather than a 500.</summary>
    [Fact]
    public async Task PostTypedEndpoint_MalformedJson_Returns400()
    {
        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/users/", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A literal JSON <c>null</c> body is a 400 — the body clients genuinely send that used to be
    /// laundered into user code as a null.
    /// </summary>
    [Fact]
    public async Task PostTypedEndpoint_LiteralJsonNullBody_Returns400()
    {
        using var content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/users/", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A body the endpoint cannot read is a 415, not a 500.
    /// </summary>
    /// <remarks>
    /// The sample registers no MVC formatters, so the JSON fallback is the only reader, and it reports
    /// an unsupported content type as an <c>InvalidOperationException</c> — which reads like an
    /// application bug rather than a request the endpoint will not accept.
    /// </remarks>
    [Fact]
    public async Task PostTypedEndpoint_UnsupportedContentType_Returns415()
    {
        using var content = new StringContent("name=Carol", System.Text.Encoding.UTF8, "text/plain");

        var response = await Client.PostAsync("/api/users/", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task PutTypedEndpoint_WithJsonBody_Returns200()
    {
        var response = await Client.PutAsJsonAsync("/api/users/7", new { name = "Updated", email = "u@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"id\":7", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// P10, closed by construction: the route owns the id. An <c>id</c> in the PUT body cannot
    /// override it, because <c>UpdateUserBody</c> no longer has an <c>Id</c> member at all.
    /// </summary>
    /// <remarks>
    /// Before M4, <c>UpdateUserRequest(Id, Name, Email)</c> was the body, so a client could send
    /// <c>PUT /api/users/1</c> with <c>{"id":999}</c> and the handler saw 999.
    /// </remarks>
    [Fact]
    public async Task PutTypedEndpoint_IdInBody_IsIgnored_RouteWins()
    {
        var response = await Client.PutAsJsonAsync("/api/users/1", new { id = 999, name = "Updated", email = "u@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"id\":1", body);
        Assert.DoesNotContain("999", body);
    }

    /// <summary>
    /// A malformed route value on a body endpoint is a 400 on the route value, even with a valid
    /// body (R2.11) — parameters bind before the body.
    /// </summary>
    [Fact]
    public async Task PutTypedEndpoint_NonNumericRouteValue_WithValidBody_Returns400OnTheRouteValue()
    {
        var response = await Client.PutAsJsonAsync("/api/users/abc", new { name = "Updated", email = "u@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The route parameter 'Id' must be a valid Int32; 'abc' is not.", await DetailOf(response));
    }

    /// <summary>
    /// <c>DeleteUser</c> is now a raw <c>IDeleteEndpoint</c> with a <c>[RouteParam]</c>: 204 on a good
    /// id, 400 on a bad one — which is only possible if the generated <c>IParameterBinder</c> ran.
    /// </summary>
    [Fact]
    public async Task DeleteRawEndpoint_Returns204_AndRejectsABadRouteValue()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync("/api/users/5")).StatusCode);

        var bad = await Client.DeleteAsync("/api/users/abc");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("The route parameter 'Id' must be a valid Int32; 'abc' is not.", await DetailOf(bad));
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

    /// <summary>
    /// The response-only <c>GetUser : IGetEndpoint&lt;UserResponse&gt;</c> gets <c>Produces</c> metadata
    /// through the generated <c>ProducesResponse</c>, at the default 200, naming its response type.
    /// </summary>
    /// <remarks>
    /// Before M4 this endpoint was typed-with-response; the assertion survived the reshape because
    /// the response-only rung must document itself exactly as well.
    /// </remarks>
    [Fact]
    public void ResponseOnlyEndpoint_WithoutOverride_ProducesMetadataDefaultsTo200()
    {
        var getUser = Endpoints().Single(endpoint =>
            endpoint.RoutePattern.RawText == "/api/users/{id}"
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);

        var produces = getUser.Metadata
            .OfType<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>()
            .ToArray();

        Assert.Contains(produces, metadata => metadata.StatusCode == 200 && metadata.Type?.Name == "UserResponse");
    }

    /// <summary>
    /// A typed endpoint with no declared response type gets no <i>typed</i> <c>Produces</c>
    /// metadata, because there is no type to describe — only the untyped default 200 plus the 400
    /// and 415 its body can fail with.
    /// </summary>
    /// <remarks>
    /// Rewritten for M5. It used to assert no produces metadata at all; M5 adds
    /// <c>ProducesProblem(400/415)</c>, and because ApiExplorer only assumes a 200 for an endpoint with
    /// no response metadata, the library restores that 200 itself. The failure this still catches is a
    /// response <i>type</i> being invented for a level that declares none.
    /// </remarks>
    [Fact]
    public void TypedEndpointWithoutResponseType_HasNoTypedProducesMetadata()
    {
        var updateUser = Endpoints().Single(endpoint =>
            endpoint.RoutePattern.RawText == "/api/users/{id}"
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("PUT") == true);

        var produces = updateUser.Metadata.OfType<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>().ToArray();

        Assert.Equal([200, 400, 415], produces.Select(m => m.StatusCode).Order().ToArray());
        var success = Assert.Single(produces, m => m.StatusCode == 200);
        Assert.True(success.Type is null || success.Type == typeof(void));
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
