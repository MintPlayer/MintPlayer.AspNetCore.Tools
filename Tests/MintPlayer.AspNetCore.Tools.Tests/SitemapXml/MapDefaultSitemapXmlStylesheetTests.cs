using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.SitemapXml;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

public class MapDefaultSitemapXmlStylesheetTests
{
    private const string ManifestResourceName = "MintPlayer.AspNetCore.SitemapXml.Assets.sitemap.xsl";

    /// <summary>
    /// The single cheapest high-value test in this suite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MapDefaultSitemapXmlStylesheet</c> looks the stylesheet up by a hard-coded manifest name
    /// and passes the result straight into a <c>StreamReader</c> with no null check, so a rename,
    /// a moved file, a changed root namespace or a dropped <c>EmbeddedResource</c> item turns into
    /// an <see cref="ArgumentNullException"/> on a live request instead of a build error.
    /// </para>
    /// <para>
    /// It is also a cross-platform guard: the csproj declares the item as
    /// <c>Include="Assets\sitemap.xsl"</c> with a backslash, and manifest names are
    /// case-sensitive — both are things that behave differently on the Linux CI runner than on a
    /// Windows dev box.
    /// </para>
    /// </remarks>
    [Fact]
    public void EmbeddedResource_ExistsWithExpectedManifestName()
    {
        using var stream = typeof(SitemapXmlExtensions).Assembly.GetManifestResourceStream(ManifestResourceName);

        Assert.NotNull(stream);
    }

    [Fact]
    public void EmbeddedResource_IsAnXslStylesheet()
    {
        using var stream = typeof(SitemapXmlExtensions).Assembly.GetManifestResourceStream(ManifestResourceName)!;
        using var reader = new StreamReader(stream);

        var content = reader.ReadToEnd();

        Assert.Contains("xsl:stylesheet", content);
        Assert.Contains("http://www.w3.org/1999/XSL/Transform", content);
    }

    private static IWebHost CreateHost(Action<IServiceCollection>? configureServices = null)
    {
        var host = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSitemapXml();
                configureServices?.Invoke(services);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapDefaultSitemapXmlStylesheet());
            })
            .Build();

        host.Start();
        return host;
    }

    [Fact]
    public async Task MapDefaultSitemapXmlStylesheet_NoUrlConfigured_ServesTheStylesheetAtSitemapXsl()
    {
        using var host = CreateHost();

        var response = await host.GetTestClient().GetAsync("/sitemap.xsl");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("xsl:stylesheet", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The response is served as <c>text/xsl</c>, not <c>text/xml</c> — a browser applying the
    /// stylesheet to a sitemap needs the former.
    /// </summary>
    [Fact]
    public async Task MapDefaultSitemapXmlStylesheet_SetsTheXslContentType()
    {
        using var host = CreateHost();

        var response = await host.GetTestClient().GetAsync("/sitemap.xsl");

        Assert.Equal("text/xsl", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("UTF-8", response.Content.Headers.ContentType.CharSet);
    }

    [Fact]
    public async Task MapDefaultSitemapXmlStylesheet_ServesTheEmbeddedResourceVerbatim()
    {
        using var stream = typeof(SitemapXmlExtensions).Assembly.GetManifestResourceStream(ManifestResourceName)!;
        using var reader = new StreamReader(stream);
        var expected = await reader.ReadToEndAsync();

        using var host = CreateHost();
        var actual = await host.GetTestClient().GetStringAsync("/sitemap.xsl");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task MapDefaultSitemapXmlStylesheet_ConfiguredUrl_IsUsedInsteadOfTheDefault()
    {
        using var host = CreateHost(services => services.Configure<MintPlayer.AspNetCore.SitemapXml.Options.SitemapXmlOptions>(
            options => options.StylesheetUrl = "/assets/my-sitemap.xsl"));

        var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/assets/my-sitemap.xsl")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/sitemap.xsl")).StatusCode);
    }

    /// <summary>
    /// An empty string falls back to the default, which is the whole purpose of the
    /// <c>NullIfEmpty()</c> call in the mapping.
    /// </summary>
    [Fact]
    public async Task MapDefaultSitemapXmlStylesheet_EmptyConfiguredUrl_FallsBackToTheDefault()
    {
        using var host = CreateHost(services => services.Configure<MintPlayer.AspNetCore.SitemapXml.Options.SitemapXmlOptions>(
            options => options.StylesheetUrl = string.Empty));

        Assert.Equal(HttpStatusCode.OK, (await host.GetTestClient().GetAsync("/sitemap.xsl")).StatusCode);
    }

    /// <summary>
    /// Pins PRD defect D-S9: <c>NullIfEmpty</c> compares against <c>string.Empty</c> only, so a
    /// whitespace-only configured URL is NOT treated as unset — it becomes the route pattern, the
    /// default route is never registered, and the stylesheet is only reachable at a URL nobody can
    /// type.
    /// </summary>
    [Fact]
    public async Task MapDefaultSitemapXmlStylesheet_WhitespaceConfiguredUrl_DoesNotFallBackToTheDefault_KnownBug()
    {
        using var host = CreateHost(services => services.Configure<MintPlayer.AspNetCore.SitemapXml.Options.SitemapXmlOptions>(
            options => options.StylesheetUrl = "   "));

        Assert.Equal(HttpStatusCode.NotFound, (await host.GetTestClient().GetAsync("/sitemap.xsl")).StatusCode);
    }

    /// <summary>
    /// Pins PRD defect D-S14: nothing validates the leading slash. Routing happens to normalise it
    /// for the endpoint, so the misconfiguration is invisible here — but the same value is
    /// interpolated into the <c>xml-stylesheet</c> instruction, where a relative href resolves
    /// against the requesting path.
    /// </summary>
    [Fact]
    public async Task MapDefaultSitemapXmlStylesheet_UrlWithoutALeadingSlash_StillRoutes_KnownGap()
    {
        using var host = CreateHost(services => services.Configure<MintPlayer.AspNetCore.SitemapXml.Options.SitemapXmlOptions>(
            options => options.StylesheetUrl = "sitemap.xsl"));

        Assert.Equal(HttpStatusCode.OK, (await host.GetTestClient().GetAsync("/sitemap.xsl")).StatusCode);
    }

    /// <summary>Mapped with <c>MapGet</c>, so anything else is a 405 rather than a 404.</summary>
    [Fact]
    public async Task MapDefaultSitemapXmlStylesheet_Post_IsMethodNotAllowed()
    {
        using var host = CreateHost();

        var response = await host.GetTestClient().PostAsync("/sitemap.xsl", new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    /// <summary>
    /// The mapping resolves <c>IOptions{SitemapXmlOptions}</c> with <c>GetRequiredService</c> at
    /// MAP time, which looks like a hard dependency on <c>AddSitemapXml()</c> but is not:
    /// <c>AddRouting()</c> already brings in the options infrastructure, so an unconfigured
    /// <c>SitemapXmlOptions</c> resolves to its defaults and the stylesheet is served from the
    /// default route regardless.
    /// </summary>
    /// <remarks>
    /// Pinned because it is the opposite of the intuitive expectation, and because it means the
    /// endpoint can be mapped — and answer requests — in an app that never registered the rest of
    /// the library.
    /// </remarks>
    [Fact]
    public async Task MapDefaultSitemapXmlStylesheet_WithoutAddSitemapXml_StillServesTheDefaultRoute()
    {
        using var host = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services => services.AddRouting())
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapDefaultSitemapXmlStylesheet());
            })
            .Build();

        host.Start();

        var response = await host.GetTestClient().GetAsync("/sitemap.xsl");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("xsl:stylesheet", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MapDefaultSitemapXmlStylesheet_ServedTwice_ReturnsTheSameContent()
    {
        using var host = CreateHost();
        var client = host.GetTestClient();

        var first = await client.GetStringAsync("/sitemap.xsl");
        var second = await client.GetStringAsync("/sitemap.xsl");

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }
}
