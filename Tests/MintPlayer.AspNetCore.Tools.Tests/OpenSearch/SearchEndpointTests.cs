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
    /// D-S18, second half, fixed for search. The handler used to read
    /// <c>GetRouteValue("searchTerms")</c> from a route registered as a literal pattern with no
    /// <c>{searchTerms}</c> token, so the service was handed <c>null</c> on every request — including
    /// <c>?q=abc</c>. It now reads the configured query-string parameter.
    /// </summary>
    [Theory]
    [InlineData("/search?q=abc", "abc")]
    [InlineData("/search?q=abc&q=def", "abc")]
    [InlineData("/search?q=", "")]
    [InlineData("/search?q=hello%20world", "hello world")]
    [InlineData("/search?other=abc", null)]
    [InlineData("/search", null)]
    public async Task Search_PassesTheQueryStringParameterToTheService(string url, string? expected)
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        await client.GetAsync(url);

        Assert.Equal(expected, Assert.Single(service.ReceivedSearchTerms));
    }

    [Fact]
    public async Task Search_ConfiguredSearchTermsParameter_IsTheOneRead()
    {
        var service = new FakeOpenSearchService();
        using var server = OpenSearchTestHost.Create(o => o.SearchTermsParameter = "query", service);
        using var client = server.CreateNonRedirectingClient();

        await client.GetAsync("/search?query=abc&q=ignored");

        Assert.Equal("abc", Assert.Single(service.ReceivedSearchTerms));
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
    /// D-S23 fixed: the handler used to call <c>Response.Redirect(result.Url)</c>, which takes only
    /// the URL, so a service that deliberately asked for a permanent or method-preserving redirect
    /// still got a 302 and its intent was silently discarded.
    /// </summary>
    [Theory]
    [InlineData(false, false, HttpStatusCode.Redirect)]
    [InlineData(true, false, HttpStatusCode.MovedPermanently)]
    [InlineData(false, true, HttpStatusCode.TemporaryRedirect)]
    [InlineData(true, true, HttpStatusCode.PermanentRedirect)]
    public async Task Search_HonoursPermanentAndPreserveMethod(bool permanent, bool preserveMethod, HttpStatusCode expected)
    {
        var service = new FakeOpenSearchService
        {
            RedirectUrl = "/results",
            Permanent = permanent,
            PreserveMethod = preserveMethod,
        };
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/search");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("/results", response.Headers.Location!.OriginalString);
    }

    /// <summary>
    /// A service that breaks the contract fails loudly. <c>Response.Redirect(null)</c> does not
    /// throw — it produces a 302 with no <c>Location</c> header at all, which a browser silently
    /// treats as a dead end.
    /// </summary>
    [Fact]
    public async Task Search_ServiceReturnsNull_Throws()
    {
        var service = new FakeOpenSearchService { ReturnNullRedirect = true };
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("/search"));

        Assert.Contains("PerformSearch", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_ServiceReturnsRedirectWithoutUrl_Throws(string url)
    {
        var service = new FakeOpenSearchService { RedirectUrl = url };
        using var server = OpenSearchTestHost.Create(_ => { }, service);
        using var client = server.CreateNonRedirectingClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("/search"));

        Assert.Contains("without a URL", ex.Message);
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
    /// browser Accept header is irrelevant here.
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
