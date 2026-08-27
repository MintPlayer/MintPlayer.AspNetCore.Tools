using System.Net;
using System.Xml;
using System.Xml.Xsl;
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

    /// <summary>
    /// The shipped stylesheet declared <c>version="2.0"</c> while every engine that will ever run it
    /// — <see cref="XslCompiledTransform"/> and the transform built into every browser — implements
    /// XSLT 1.0 only. Nothing in the file uses a 2.0-only construct, so the declaration was simply
    /// wrong, and it is now <c>1.0</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured, and milder than predicted: <see cref="XslCompiledTransform"/> loaded and
    /// transformed the 2.0-declared file without complaint, byte for byte identically, because a
    /// version above the processor is handled in FORWARDS-COMPATIBLE mode. The cost of that mode is
    /// what makes the declaration worth fixing anyway — in it, an unrecognised XSLT element is
    /// silently ignored instead of reported, so a genuine authoring mistake in this file would
    /// produce a quietly wrong page rather than an error.
    /// </para>
    /// <para>
    /// Asserted by actually loading it rather than by reading the attribute: a version assertion
    /// alone would pass just as happily on a file that had since acquired a real 2.0 construct,
    /// which is the case that must fail loudly.
    /// </para>
    /// </remarks>
    [Fact]
    public void EmbeddedResource_LoadsInAnXslt10Processor()
    {
        using var stream = typeof(SitemapXmlExtensions).Assembly.GetManifestResourceStream(ManifestResourceName)!;
        using var reader = XmlReader.Create(stream);

        var transform = new XslCompiledTransform();
        transform.Load(reader);

        Assert.NotNull(transform.OutputSettings);
    }

    /// <summary>Transforming a real sitemap end-to-end, so the templates are exercised and not just parsed.</summary>
    [Fact]
    public void EmbeddedResource_TransformsAUrlSetToHtml()
    {
        using var stylesheet = typeof(SitemapXmlExtensions).Assembly.GetManifestResourceStream(ManifestResourceName)!;
        using var stylesheetReader = XmlReader.Create(stylesheet);

        var transform = new XslCompiledTransform();
        transform.Load(stylesheetReader);

        const string sitemap = """
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>https://example.org/a</loc><changefreq>weekly</changefreq><lastmod>2024-03-04</lastmod></url>
            </urlset>
            """;

        var output = new StringWriter();
        using (var input = XmlReader.Create(new StringReader(sitemap)))
            transform.Transform(input, null, output);

        var html = output.ToString();
        Assert.Contains("https://example.org/a", html);
        Assert.Contains("weekly", html);
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
    /// PRD defect D-S9: <c>NullIfEmpty</c> used to compare against <c>string.Empty</c> only, so a
    /// whitespace-only configured URL was NOT treated as unset — it became the route pattern, the
    /// default route was never registered, and the stylesheet ended up reachable only at a URL
    /// nobody can type.
    /// </summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task MapDefaultSitemapXmlStylesheet_WhitespaceConfiguredUrl_FallsBackToTheDefault(string url)
    {
        using var host = CreateHost(services => services.Configure<MintPlayer.AspNetCore.SitemapXml.Options.SitemapXmlOptions>(
            options => options.StylesheetUrl = url));

        Assert.Equal(HttpStatusCode.OK, (await host.GetTestClient().GetAsync("/sitemap.xsl")).StatusCode);
    }

    /// <summary>
    /// PRD defect D-S14: the missing leading slash is now rejected at MAP time. Routing normalises
    /// it for the endpoint, so the misconfiguration was invisible on this side — but the same value
    /// is interpolated into the <c>xml-stylesheet</c> instruction, where a relative href resolves
    /// against the depth of the requesting path and silently 404s for any nested sitemap route.
    /// </summary>
    /// <remarks>
    /// A startup failure rather than a request-time one is the point: the value is wrong for every
    /// request, so the loudest possible moment to say so is when the endpoint is mapped.
    /// </remarks>
    [Theory]
    [InlineData("sitemap.xsl")]
    [InlineData("assets/sitemap.xsl")]
    public void MapDefaultSitemapXmlStylesheet_UrlWithoutALeadingSlash_ThrowsAtMapTime(string url)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateHost(
            services => services.Configure<MintPlayer.AspNetCore.SitemapXml.Options.SitemapXmlOptions>(
                options => options.StylesheetUrl = url)));

        Assert.Contains("absolute path", exception.Message);
    }

    /// <summary>D-S12's other consumer: the quote is rejected here too, at map time.</summary>
    [Fact]
    public void MapDefaultSitemapXmlStylesheet_UrlContainingAQuote_ThrowsAtMapTime()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateHost(
            services => services.Configure<MintPlayer.AspNetCore.SitemapXml.Options.SitemapXmlOptions>(
                options => options.StylesheetUrl = "/s.xsl\" alternate=\"yes")));

        Assert.Contains("double quote", exception.Message);
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
