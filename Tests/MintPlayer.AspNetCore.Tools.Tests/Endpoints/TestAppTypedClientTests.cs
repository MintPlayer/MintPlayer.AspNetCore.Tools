using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.Endpoints.Generated;
using MintPlayer.AspNetCore.Endpoints.TestApp.Models;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// The typed client generated in <c>MintPlayer.AspNetCore.Endpoints.TestApp.Client</c> — a plain
/// library with no ASP.NET Core and no source access to the TestApp — calling the real TestApp (M9).
/// </summary>
/// <remarks>
/// <para>
/// Every request goes through a recording handler, so the tests assert the URL the client actually
/// sent, not only that the server answered. Each URL is compared with what the app's own
/// <see cref="LinkGenerator"/> produces for the same endpoint name and values: the client builds URLs
/// with the runtime library's <c>EndpointTemplateBinder</c> source, emitted into the client project,
/// and this is where that is observed end to end.
/// </para>
/// <para>
/// A class fixture of its own, so the users these tests create do not reach
/// <see cref="TestAppEndToEndTests"/>' app instance.
/// </para>
/// </remarks>
public class TestAppTypedClientTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public TestAppTypedClientTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    private (TestAppClient Client, RecordingHandler Recorder) CreateClient()
    {
        var recorder = new RecordingHandler();
        return (new TestAppClient(factory.CreateDefaultClient(recorder)), recorder);
    }

    private string LinkTo(string name, object values) =>
        factory.Services.GetRequiredService<LinkGenerator>().GetPathByName(name, values)
        ?? throw new InvalidOperationException($"No link for {name}.");

    [Fact]
    public async Task GetUser_SendsTheTypedRouteValue_AndReadsTheResponse()
    {
        var (client, recorder) = CreateClient();

        var user = await client.GetUserAsync(1);

        Assert.Equal(new UserResponse(1, "Alice", "alice@example.com"), user);
        Assert.Equal(("GET", "/api/users/1"), recorder.Single());
    }

    /// <summary>
    /// A string route value with a space, a plus and an at sign: the space and the plus are escaped,
    /// <c>@</c> is not — <see cref="System.Text.Encodings.Web.UrlEncoder.Default"/>, as routing encodes
    /// it, where <see cref="Uri.EscapeDataString(string)"/> would have escaped the <c>@</c> too — and
    /// the server decodes it back to the same name.
    /// </summary>
    [Fact]
    public async Task EscapedRouteValue_MatchesLinkGenerator_AndRoundTrips()
    {
        var (client, recorder) = CreateClient();
        const string name = "c d@e+f";

        var created = await client.CreateUserAsync(new CreateUserRequest(name, "odd@example.com"));
        var found = await client.FindUserByNameAsync(name);

        Assert.NotNull(created);
        Assert.Equal(new UserResponse(created.Id, name, "odd@example.com"), found);

        var sent = recorder.Requests[1];
        Assert.Equal("/api/users/by-name/c%20d@e%2Bf", sent.PathAndQuery);
        Assert.Equal(LinkTo("FindUserByName", new { name }), sent.PathAndQuery);
    }

    /// <summary>
    /// A slash in a route value is escaped to <c>%2F</c>, exactly as <see cref="LinkGenerator"/>
    /// escapes it, so it cannot split the path into two segments.
    /// </summary>
    /// <remarks>
    /// Only the URL is asserted. ASP.NET Core does not decode <c>%2F</c> back into a route value (the
    /// server sees <c>a%2Fb</c>), so no client can round-trip a slash through a <c>{name}</c> token;
    /// that is the framework's behaviour, not the client's.
    /// </remarks>
    [Fact]
    public async Task SlashInRouteValue_IsEscapedLikeLinkGenerator()
    {
        var (client, recorder) = CreateClient();

        try { await client.FindUserByNameAsync("a/b"); }
        catch (HttpRequestException) { /* 404: see remarks */ }

        Assert.Equal(("GET", "/api/users/by-name/a%2Fb"), recorder.Single());
        Assert.Equal(LinkTo("FindUserByName", new { name = "a/b" }), recorder.Requests[0].PathAndQuery);
    }

    /// <summary>The request body goes out as JSON and the declared response comes back typed.</summary>
    [Fact]
    public async Task CreateUser_SendsTheJsonBody_AndReturnsTheCreatedUser()
    {
        var (client, recorder) = CreateClient();

        var created = await client.CreateUserAsync(new CreateUserRequest("Erin", "erin@example.com"));

        Assert.NotNull(created);
        Assert.Equal("Erin", created.Name);
        Assert.Equal("erin@example.com", created.Email);
        Assert.Equal(("POST", LinkTo("CreateUser", new { })), recorder.Single());
        Assert.Equal(new UserResponse(created.Id, "Erin", "erin@example.com"), await client.GetUserAsync(created.Id));
    }

    /// <summary>
    /// A route value and a body together, on an endpoint without a declared response type: the
    /// response comes back unchecked for the caller to read.
    /// </summary>
    [Fact]
    public async Task UpdateUser_SendsRouteValueAndBody_AndReturnsTheResponseMessage()
    {
        var (client, recorder) = CreateClient();

        using var response = await client.UpdateUserAsync(21, new UpdateUserBody("Frank", "frank@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"id\":21", body);
        Assert.Contains("Frank", body);
        Assert.Equal(("PUT", "/api/users/21"), recorder.Single());
    }

    /// <summary>A query parameter is optional, and goes into the query string when given.</summary>
    [Fact]
    public async Task ListUsers_QueryParameter_IsOptional()
    {
        var (client, recorder) = CreateClient();

        using var withPage = await client.ListUsersAsync(page: 3);
        using var withoutPage = await client.ListUsersAsync();

        Assert.Contains("\"page\":3", await withPage.Content.ReadAsStringAsync());
        Assert.Contains("\"page\":1", await withoutPage.Content.ReadAsStringAsync());
        // The query key is the server's own — the property name "Page", as the typed link
        // Routes.Api.Users.ListUsers(page: 3) writes it too; the server matches it case-insensitively.
        Assert.Equal(LinkTo("ListUsers", new { Page = 3 }), recorder.Requests[0].PathAndQuery);
        Assert.Equal(LinkTo("ListUsers", new { }), recorder.Requests[1].PathAndQuery);
    }

    /// <summary>A declared response type turns a non-success status into an exception carrying it.</summary>
    [Fact]
    public async Task TypedResponse_NotFound_ThrowsWithTheStatusCode()
    {
        var (client, _) = CreateClient();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetUserAsync(987654));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    /// <summary>A multi-verb endpoint gets one method per verb; its catch-all token is optional.</summary>
    [Fact]
    public async Task MultiMethodEndpoint_HasOneMethodPerVerb()
    {
        var (client, recorder) = CreateClient();

        using var options = await client.PreflightEndpointOptionsAsync(path: "x/y");
        using var head = await client.PreflightEndpointHeadAsync();

        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(("OPTIONS", LinkTo("PreflightEndpoint", new { path = "x/y" })), (recorder.Requests[0].Method, recorder.Requests[0].PathAndQuery));
        Assert.Equal("HEAD", recorder.Requests[1].Method);
    }

    [Fact]
    public async Task DeleteUser_Returns204()
    {
        var (client, recorder) = CreateClient();

        using var response = await client.DeleteUserAsync(55);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(("DELETE", "/api/users/55"), recorder.Single());
    }

    /// <summary>Records the method and the raw, still-escaped path and query of every request.</summary>
    private sealed class RecordingHandler : DelegatingHandler
    {
        public List<(string Method, string PathAndQuery)> Requests { get; } = [];

        public (string Method, string PathAndQuery) Single() => Assert.Single(Requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery));
            return base.SendAsync(request, cancellationToken);
        }
    }
}
