using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.OpenSearch;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

public class MapOpenSearchValidationTests
{
    /// <summary>
    /// Minimal <see cref="IEndpointRouteBuilder"/> for the cases where <c>MapOpenSearch</c> is
    /// expected to throw before it ever reaches <c>MapGet</c>.
    /// </summary>
    private sealed class BareEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    /// <summary>
    /// The <c>"OpenSearchOptions not initialized"</c> guard fires only when the options
    /// infrastructure is missing entirely — a service collection with no <c>AddOptions</c> at all.
    /// </summary>
    [Fact]
    public void MapOpenSearch_WithoutAnyOptionsInfrastructure_Throws()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var routes = new BareEndpointRouteBuilder(provider);

        var ex = Assert.Throws<InvalidOperationException>(() => routes.MapOpenSearch());

        Assert.Contains("OpenSearchOptions not initialized", ex.Message);
    }

    /// <summary>
    /// Pins D-S16: the guard is unreachable in practice. Once anything has pulled in the options
    /// infrastructure — which <c>AddOpenSearch</c> itself does via <c>AddControllersWithViews</c> —
    /// <c>GetService&lt;IOptions&lt;OpenSearchOptions&gt;&gt;()</c> returns a live instance whose
    /// <c>Value</c> is a default-constructed <c>OpenSearchOptions</c> with every property null. So
    /// forgetting <c>AddOpenSearch</c>'s options overload produces silent defaults, never the
    /// diagnostic the message promises.
    /// </summary>
    [Fact]
    public void OptionsAccessor_WithoutConfigure_IsNonNullWithAllNullProperties_KnownBug()
    {
        var provider = new ServiceCollection().AddOptions().BuildServiceProvider();

        var accessor = provider.GetService<IOptions<OpenSearchOptions>>();

        Assert.NotNull(accessor);
        Assert.NotNull(accessor.Value);
        Assert.Null(accessor.Value.OsdxEndpoint);
        Assert.Null(accessor.Value.SearchUrl);
        Assert.Null(accessor.Value.SuggestUrl);
        Assert.Null(accessor.Value.ImageUrl);
        Assert.Null(accessor.Value.ShortName);
        Assert.Null(accessor.Value.Description);
        Assert.Null(accessor.Value.Contact);
    }

    /// <summary>
    /// The other half of D-S16: <c>AddOpenSearch</c> without the options overload maps the default
    /// routes rather than throwing.
    /// </summary>
    [Fact]
    public async Task MapOpenSearch_WithoutConfigure_MapsDefaultRoutes_KnownBug()
    {
        using var server = OpenSearchTestHost.Create(configure: null, service: new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var osdx = await client.GetAsync("/opensearch.xml");
        var suggest = await client.GetAsync("/suggest");
        var search = await client.GetAsync("/search");

        Assert.NotEqual(HttpStatusCode.NotFound, osdx.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, suggest.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, search.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task MapOpenSearch_EmptyOrNullOsdxEndpoint_FallsBackToDefault(string? osdxEndpoint)
    {
        using var server = OpenSearchTestHost.Create(o => o.OsdxEndpoint = osdxEndpoint!, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/opensearch.xml");

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Pins D-S17: the leading-slash check throws the bare base <see cref="Exception"/> type, which
    /// a caller cannot catch selectively and which no analyzer-clean codebase would accept.
    /// <c>Assert.Throws&lt;Exception&gt;</c> matches exactly, so this fails the moment the type is
    /// narrowed.
    /// </summary>
    [Fact]
    public void MapOpenSearch_OsdxEndpointWithoutLeadingSlash_ThrowsBareException_KnownBug()
    {
        var provider = new ServiceCollection()
            .AddOptions()
            .Configure<OpenSearchOptions>(o => o.OsdxEndpoint = "opensearch.xml")
            .BuildServiceProvider();
        var routes = new BareEndpointRouteBuilder(provider);

        var ex = Assert.Throws<Exception>(() => routes.MapOpenSearch());

        Assert.Equal(@"OpenSearch endpoint must start with ""/""", ex.Message);
    }

    /// <summary>
    /// The other half of D-S17: only <c>OsdxEndpoint</c> is validated. A <c>SearchUrl</c> without a
    /// leading slash is accepted and concatenated straight into an absolute URL, producing
    /// <c>http://localhostsearch</c> — a template advertised to every search client. Same for
    /// <c>SuggestUrl</c>.
    /// </summary>
    [Fact]
    public async Task MapOpenSearch_SearchAndSuggestUrlWithoutLeadingSlash_AreNotValidated_KnownBug()
    {
        using var server = OpenSearchTestHost.Create(
            o =>
            {
                o.SearchUrl = "search";
                o.SuggestUrl = "suggest";
            },
            new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var document = XDocument.Parse(await client.GetStringAsync("/opensearch.xml"));
        var templates = document.Root!.Elements(OpenSearchTestHost.A9 + "Url")
            .Select(u => (string?)u.Attribute("template"))
            .ToList();

        Assert.Contains("http://localhostsearch", templates);
        Assert.Contains("http://localhostsuggest", templates);
    }

    /// <summary>
    /// And <c>ImageUrl</c> too — it is never validated and never checked for null.
    /// </summary>
    [Fact]
    public async Task MapOpenSearch_ImageUrlWithoutLeadingSlash_IsNotValidated_KnownBug()
    {
        using var server = OpenSearchTestHost.Create(o => o.ImageUrl = "favicon.png", new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var document = XDocument.Parse(await client.GetStringAsync("/opensearch.xml"));

        Assert.Equal("http://localhostfavicon.png", document.Root!.Element(OpenSearchTestHost.A9 + "Image")!.Value);
    }

    [Fact]
    public async Task MapOpenSearch_CustomEndpoints_AreMappedAtTheConfiguredPaths()
    {
        using var server = OpenSearchTestHost.Create(
            o =>
            {
                o.OsdxEndpoint = "/osdx.xml";
                o.SearchUrl = "/find";
                o.SuggestUrl = "/hints";
            },
            new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/opensearch.xml")).StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, (await client.GetAsync("/osdx.xml")).StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, (await client.GetAsync("/hints")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/find")).StatusCode);
    }

    /// <summary>Only GET is mapped; a POST to any of the three routes is a 405.</summary>
    [Fact]
    public async Task MapOpenSearch_MapsGetOnly()
    {
        using var server = OpenSearchTestHost.Create(_ => { }, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        foreach (var path in new[] { "/opensearch.xml", "/suggest", "/search" })
        {
            var response = await client.PostAsync(path, new StringContent(string.Empty));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }

    [Fact]
    public void MapOpenSearch_ReturnsSameRouteBuilder()
    {
        var provider = new ServiceCollection()
            .AddOptions()
            .AddLogging()
            .AddRouting()
            .BuildServiceProvider();
        var routes = new BareEndpointRouteBuilder(provider);

        var returned = routes.MapOpenSearch();

        Assert.Same(routes, returned);
    }
}
