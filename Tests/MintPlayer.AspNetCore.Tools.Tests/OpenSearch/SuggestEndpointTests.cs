using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

public class SuggestEndpointTests
{
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
    /// Pins the second half of D-S18 for suggest. The route is the literal <c>/suggest</c> with no
    /// <c>{searchTerms}</c> token, so <c>GetRouteValue("searchTerms")</c> is null on every request —
    /// whatever the query string says. The service can never see the user's query.
    /// </summary>
    [Theory]
    [InlineData("/suggest")]
    [InlineData("/suggest?q=abc")]
    [InlineData("/suggest?searchTerms=abc")]
    [InlineData("/suggest?q=abc&searchTerms=def")]
    public async Task Suggest_SearchTermsIsAlwaysNull_KnownBug(string url)
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        await client.GetAsync(url);

        Assert.Null(Assert.Single(service.ReceivedSuggestTerms));
    }

    /// <summary>
    /// The OpenSearch suggestions format is <c>[query, [completions], …]</c>. The first slot is
    /// null here for the same reason — D-S18 — so a client cannot even echo back what it asked.
    /// </summary>
    [Fact]
    public async Task Suggest_Body_IsNullQueryFollowedBySuggestions_KnownBug()
    {
        var service = new FakeOpenSearchService { Suggestions = ["alpha", "beta"] };
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/suggest?q=alp"));

        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(2, root.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, root[0].ValueKind);
        Assert.Equal(["alpha", "beta"], root[1].EnumerateArray().Select(e => e.GetString()!).ToArray());
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
    /// Pins D-S22. The handler assigns <c>Response.Headers["Content-Type"] = "application/json"</c>
    /// by hand and then writes through <c>ObjectResult</c>, so MVC's JSON formatter negotiates and
    /// overwrites it — the hand-written assignment is dead. Worse, the OpenSearch spec wants
    /// <c>application/x-suggestions+json</c>, which is exactly what this library's own OSDX
    /// advertises for this endpoint, so the description and the response disagree.
    /// </summary>
    [Fact]
    public async Task Suggest_ContentType_IsPlainJsonNotXSuggestionsJson_KnownBug()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/suggest");
        var contentType = response.Content.Headers.ContentType!;

        Assert.Equal("application/json", contentType.MediaType);
        Assert.Equal("utf-8", contentType.CharSet);

        var advertised = XDocument.Parse(await client.GetStringAsync("/opensearch.xml"))
            .Root!.Elements(OpenSearchTestHost.A9 + "Url")
            .Select(u => (string?)u.Attribute("type"))
            .ToList();
        Assert.Contains("application/x-suggestions+json", advertised);
    }

    /// <summary>
    /// The hand-set <c>Content-Type</c> is replaced, not appended — asserted separately because a
    /// duplicated header would be a different (and worse) failure mode.
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
    /// The OSDX formatter sits at index 0 and would be asked first; it refuses
    /// <c>typeof(object[])</c>, which is what lets this response reach the JSON formatter. A browser
    /// Accept header is therefore satisfiable here, unlike on the OSDX endpoint (D-S21).
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
