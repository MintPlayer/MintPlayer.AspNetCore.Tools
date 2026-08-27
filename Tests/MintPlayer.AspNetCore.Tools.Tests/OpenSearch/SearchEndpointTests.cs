using System.Net;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

public class SearchEndpointTests
{
    [Fact]
    public async Task Search_RedirectsToTheServiceUrl()
    {
        var service = new FakeOpenSearchService { RedirectUrl = "/results?q=abc" };
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/search");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/results?q=abc", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Search_CallsTheServiceExactlyOnce()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        await client.GetAsync("/search?q=abc");

        Assert.Single(service.ReceivedSearchTerms);
    }

    /// <summary>
    /// Pins the second half of D-S18 for search — the same literal-route/route-value mismatch as
    /// suggest. The user's query never reaches <c>PerformSearch</c>, so the service cannot build a
    /// meaningful redirect target no matter how it is implemented.
    /// </summary>
    [Theory]
    [InlineData("/search")]
    [InlineData("/search?q=abc")]
    [InlineData("/search?searchTerms=abc")]
    [InlineData("/search?q=abc&searchTerms=def")]
    public async Task Search_SearchTermsIsAlwaysNull_KnownBug(string url)
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        await client.GetAsync(url);

        Assert.Null(Assert.Single(service.ReceivedSearchTerms));
    }

    [Fact]
    public async Task Search_AbsoluteRedirectUrl_IsPassedThroughUnchanged()
    {
        var service = new FakeOpenSearchService { RedirectUrl = "https://example.com/results" };
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/search");

        Assert.Equal("https://example.com/results", response.Headers.Location!.OriginalString);
    }

    /// <summary>
    /// Pins D-S23. The handler calls <c>context.Response.Redirect(result.Url)</c>, which takes only
    /// the URL: <see cref="Microsoft.AspNetCore.Mvc.RedirectResult.Permanent"/> is read off the
    /// result by nobody, so a service that deliberately returns a permanent redirect still gets a
    /// 302. Search engines and browsers will not cache it, and the service's intent is silently
    /// discarded.
    /// </summary>
    [Fact]
    public async Task Search_PermanentRedirect_IsDowngradedTo302_KnownBug()
    {
        var service = new FakeOpenSearchService { RedirectUrl = "/results", Permanent = true };
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/search");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
    }

    /// <summary>
    /// Same defect, <c>PreserveMethod</c> half: a 307/308 is never emitted either.
    /// </summary>
    [Fact]
    public async Task Search_PreserveMethodRedirect_IsDowngradedTo302_KnownBug()
    {
        var service = new FakeOpenSearchService { RedirectUrl = "/results", Permanent = true, PreserveMethod = true };
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/search");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.TemporaryRedirect, response.StatusCode);
    }

    /// <summary>
    /// A null <c>RedirectResult.Url</c> is impossible — the constructor rejects it — so the handler's
    /// unguarded <c>result.Url</c> dereference is safe. Pinned because the handler relies on it
    /// without saying so.
    /// </summary>
    [Fact]
    public void RedirectResult_RejectsNullUrl()
    {
        Assert.Throws<ArgumentNullException>(() => new Microsoft.AspNetCore.Mvc.RedirectResult(null!));
    }

    [Fact]
    public async Task Search_ConfiguredSearchUrl_IsUsed()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(o => o.SearchUrl = "/find", service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/find");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Single(service.ReceivedSearchTerms);
    }

    /// <summary>
    /// The redirect is written directly to the response, so no content negotiation happens and a
    /// browser Accept header is irrelevant here — unlike the OSDX endpoint (D-S21).
    /// </summary>
    [Fact]
    public async Task Search_BrowserAcceptHeader_StillRedirects()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/search");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Search_ResponseBody_IsEmpty()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/search");

        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
