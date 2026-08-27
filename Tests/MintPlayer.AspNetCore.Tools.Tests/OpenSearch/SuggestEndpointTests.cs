using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

public class SuggestEndpointTests
{
    private const string SuggestionsMediaType = "application/x-suggestions+json";

    [Fact]
    public async Task Suggest_Returns200()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/suggest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_CallsTheServiceExactlyOnce()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        await client.GetAsync("/suggest?q=abc");

        Assert.Single(service.ReceivedSuggestTerms);
    }

    /// <summary>
    /// D-S18, second half, fixed for suggest. The route is the literal <c>/suggest</c>, so
    /// <c>GetRouteValue("searchTerms")</c> was null on every request whatever the query string said;
    /// the handler now reads the configured query-string parameter.
    /// </summary>
    [Theory]
    [InlineData("/suggest?q=abc", "abc")]
    [InlineData("/suggest?q=", "")]
    [InlineData("/suggest?other=abc", null)]
    [InlineData("/suggest", null)]
    public async Task Suggest_PassesTheQueryStringParameterToTheService(string url, string? expected)
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        await client.GetAsync(url);

        Assert.Equal(expected, Assert.Single(service.ReceivedSuggestTerms));
    }

    [Fact]
    public async Task Suggest_ConfiguredSearchTermsParameter_IsTheOneRead()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(o => o.SearchTermsParameter = "query", service);
        using var client = server.CreateNonRedirectingClient();

        await client.GetAsync("/suggest?query=abc&q=ignored");

        Assert.Equal("abc", Assert.Single(service.ReceivedSuggestTerms));
    }

    /// <summary>
    /// The OpenSearch suggestions format is <c>[query, [completions], …]</c>. The first slot now
    /// echoes back the query the client sent, which is what a suggestion UI matches against.
    /// </summary>
    [Fact]
    public async Task Suggest_Body_IsTheQueryFollowedBySuggestions()
    {
        var service = new FakeOpenSearchService { Suggestions = ["alpha", "beta"] };
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/suggest?q=alp"));

        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(2, root.GetArrayLength());
        Assert.Equal("alp", root[0].GetString());
        Assert.Equal(["alpha", "beta"], root[1].EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    /// <summary>With no query at all the first slot is null — there is nothing to echo.</summary>
    [Fact]
    public async Task Suggest_NoQuery_FirstSlotIsNull()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/suggest"));

        Assert.Equal(JsonValueKind.Null, document.RootElement[0].ValueKind);
    }

    [Fact]
    public async Task Suggest_EmptySuggestions_ProducesEmptyInnerArray()
    {
        var service = new FakeOpenSearchService { Suggestions = [] };
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/suggest"));

        Assert.Equal(0, document.RootElement[1].GetArrayLength());
    }

    /// <summary>
    /// D-S22 fixed. The handler used to assign
    /// <c>Response.Headers["Content-Type"] = "application/json"</c> by hand and then write through
    /// <c>ObjectResult</c>, so MVC's JSON formatter negotiated and overwrote it — the assignment was
    /// dead code, and the served type disagreed with the
    /// <c>application/x-suggestions+json</c> this library's own OSDX advertises for this endpoint.
    /// The media type is now declared on the <c>ObjectResult</c>, which the JSON formatter accepts
    /// because it matches <c>application/*+json</c>.
    /// </summary>
    [Fact]
    public async Task Suggest_ContentType_MatchesTheTypeTheOsdxAdvertises()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/suggest");

        Assert.Equal(SuggestionsMediaType, response.Content.Headers.ContentType!.MediaType);

        var advertised = XDocument.Parse(await client.GetStringAsync("/opensearch.xml"))
            .Root!.Elements(OpenSearchTestHost.A9 + "Url")
            .Select(u => (string?)u.Attribute("type"))
            .ToList();
        Assert.Contains(SuggestionsMediaType, advertised);
    }

    /// <summary>
    /// Asserted separately because a duplicated header would be a different (and worse) failure
    /// mode than a wrong one.
    /// </summary>
    [Fact]
    public async Task Suggest_ContentTypeHeader_AppearsExactlyOnce()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/suggest");

        Assert.True(response.Content.Headers.TryGetValues("Content-Type", out var values));
        Assert.Single(values!);
    }

    /// <summary>
    /// The OSDX formatter sits at index 0 and is asked first; it refuses <c>typeof(object[])</c>,
    /// which is what lets this response reach the JSON formatter.
    /// </summary>
    [Fact]
    public async Task Suggest_BrowserAcceptHeader_StillReturns200()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/suggest");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SuggestionsMediaType, response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Suggest_AcceptsTheSuggestionsMediaTypeExplicitly()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/suggest");
        request.Headers.TryAddWithoutValidation("Accept", SuggestionsMediaType);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SuggestionsMediaType, response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Suggest_ConfiguredSuggestUrl_IsUsed()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(o => o.SuggestUrl = "/hints", service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/hints");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(service.ReceivedSuggestTerms);
    }
}
