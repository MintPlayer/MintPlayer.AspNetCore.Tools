using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.SitemapXml;
using MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;
using MintPlayer.AspNetCore.SitemapXml.Abstractions.Enums;
using MintPlayer.AspNetCore.SitemapXml.Options;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

/// <summary>
/// The whole pipeline on a real server loop: content negotiation, the formatter, the stylesheet
/// processing instruction and the bytes on the wire.
/// </summary>
/// <remarks>
/// The response is produced through <c>IActionResultExecutor{ObjectResult}</c> rather than a
/// controller. That is the same path MVC itself takes for an action returning an object, minus
/// application-part discovery — which would otherwise scan this test assembly and pick up every
/// other milestone's fixtures.
/// </remarks>
public class SitemapEndToEndTests
{
    private static IWebHost CreateHost(object model, Action<SitemapXmlOptions>? configure = null)
    {
        var host = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                if (configure is null)
                    services.AddSitemapXml();
                else
                    services.AddSitemapXml(configure);
            })
            .Configure(app => app.Run(async context =>
            {
                var executor = context.RequestServices.GetRequiredService<IActionResultExecutor<ObjectResult>>();
                var actionContext = new ActionContext(context, new RouteData(), new ActionDescriptor());
                await executor.ExecuteAsync(actionContext, new ObjectResult(model));
            }))
            .Build();

        host.Start();
        return host;
    }

    private static async Task<HttpResponseMessage> GetAsync(IWebHost host, string accept)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(accept));
        return await client.GetAsync("/sitemap.xml");
    }

    private static UrlSet SampleUrlSet() => new(
    [
        new Url
        {
            Loc = "https://example.org/a",
            LastMod = new DateTime(2024, 3, 4),
            ChangeFreq = ChangeFreq.Daily,
        },
    ]);

    [Fact]
    public async Task UrlSet_WithApplicationXmlAccept_IsServedAsXml()
    {
        using var host = CreateHost(SampleUrlSet());

        var response = await GetAsync(host, "application/xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task UrlSet_WithTextXmlAccept_IsServedAsTextXml()
    {
        using var host = CreateHost(SampleUrlSet());

        var response = await GetAsync(host, "text/xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/xml", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task UrlSet_OnTheWire_ParsesAsASitemapNamespacedUrlset()
    {
        using var host = CreateHost(SampleUrlSet());

        var body = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();
        var document = XDocument.Parse(body);

        Assert.Equal("urlset", document.Root!.Name.LocalName);
        Assert.Equal(Ns.Sitemap, document.Root.Name.Namespace);
        Assert.Equal(
            "https://example.org/a",
            document.Root.Element(Ns.Sitemap + "url")!.Element(Ns.Sitemap + "loc")!.Value);
    }

    /// <summary>
    /// The declaration is what the ctor's <c>OmitXmlDeclaration = false</c> is for, and its
    /// <c>encoding</c> must match the bytes actually written.
    /// </summary>
    [Fact]
    public async Task UrlSet_OnTheWire_StartsWithAUtf8XmlDeclaration()
    {
        using var host = CreateHost(SampleUrlSet());

        var body = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", body);
    }

    /// <summary>The formatter's <c>Serialize</c> override earning its keep on the wire.</summary>
    [Fact]
    public async Task UrlSet_OnTheWire_CarriesNoXsiOrXsdDeclarations()
    {
        using var host = CreateHost(SampleUrlSet());

        var body = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("XMLSchema-instance", body);
        Assert.DoesNotContain("xmlns:xsd", body);
    }

    [Fact]
    public async Task SitemapIndex_OnTheWire_ParsesAsASitemapNamespacedSitemapindex()
    {
        using var host = CreateHost(new SitemapIndex(
        [
            new Sitemap { Loc = "https://example.org/sitemap-1.xml", LastMod = new DateTime(2024, 3, 4) },
        ]));

        var body = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();
        var document = XDocument.Parse(body);

        Assert.Equal("sitemapindex", document.Root!.Name.LocalName);
        Assert.Equal("2024-03-04", document.Root.Descendants(Ns.Sitemap + "lastmod").Single().Value);
    }

    // ── the stylesheet processing instruction ─────────────────────────────────────────────────

    [Fact]
    public async Task UrlSet_WithAStylesheetConfigured_CarriesTheProcessingInstruction()
    {
        using var host = CreateHost(SampleUrlSet(), options => options.StylesheetUrl = "/sitemap.xsl");

        var body = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();

        Assert.Contains("<?xml-stylesheet type=\"text/xsl\" href=\"/sitemap.xsl\"?>", body);
    }

    /// <summary>
    /// The instruction has to sit between the declaration and the root element, or the document
    /// is not well-formed.
    /// </summary>
    [Fact]
    public async Task UrlSet_WithAStylesheetConfigured_IsStillWellFormedXml()
    {
        using var host = CreateHost(SampleUrlSet(), options => options.StylesheetUrl = "/sitemap.xsl");

        var body = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();
        var document = XDocument.Parse(body);

        var instruction = document.Nodes().OfType<XProcessingInstruction>().Single();
        Assert.Equal("xml-stylesheet", instruction.Target);
        Assert.Equal("urlset", document.Root!.Name.LocalName);
    }

    [Fact]
    public async Task UrlSet_WithNoStylesheetConfigured_CarriesNoProcessingInstruction()
    {
        using var host = CreateHost(SampleUrlSet());

        var body = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("xml-stylesheet", body);
    }

    /// <summary>
    /// Pins PRD defect D-S13 from the outside: the formatter mutates the shared
    /// <c>XmlWriterSettings</c> on every call, so two requests through the same singleton
    /// formatter must still produce identical bytes. This is the regression test that would catch
    /// the mutation turning into an accumulating one.
    /// </summary>
    [Fact]
    public async Task UrlSet_TwoSequentialRequests_ProduceIdenticalBytes()
    {
        using var host = CreateHost(SampleUrlSet(), options => options.StylesheetUrl = "/sitemap.xsl");

        var first = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();
        var second = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();

        Assert.Equal(first, second);
    }

    // ── negotiation ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The formatter sits at index 0 but only claims two media types, so a JSON client still gets
    /// JSON — this is what keeps <c>AddSitemapXml()</c> safe to call in an API app.
    /// </summary>
    [Fact]
    public async Task UrlSet_WithJsonAccept_FallsThroughToTheJsonFormatter()
    {
        using var host = CreateHost(SampleUrlSet());

        var response = await GetAsync(host, "application/json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>
    /// <c>RespectBrowserAcceptHeader = true</c> is NOT the same as <c>ReturnHttpNotAcceptable</c>,
    /// and the library sets only the former. So a browser navigating to the sitemap — sending
    /// <c>text/html</c> — does not get a 406: MVC finds no formatter for the requested media type
    /// and falls back to the first one that can write the object, which is this formatter.
    /// </summary>
    /// <remarks>
    /// Pinned because it is the opposite of what <c>RespectBrowserAcceptHeader</c> reads like, and
    /// because it is the behaviour real browsers and crawlers actually get.
    /// </remarks>
    [Fact]
    public async Task UrlSet_WithTextHtmlAcceptOnly_StillFallsBackToXml()
    {
        using var host = CreateHost(SampleUrlSet());

        var response = await GetAsync(host, "text/html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/xml", response.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>A wildcard <c>Accept</c> lets the first formatter — ours — take the response.</summary>
    [Fact]
    public async Task UrlSet_WithWildcardAccept_IsServedAsXmlByTheSitemapFormatter()
    {
        using var host = CreateHost(SampleUrlSet());

        var response = await GetAsync(host, "*/*");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/xml", response.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>
    /// With no <c>Accept</c> header at all MVC uses the first formatter that can write the type,
    /// which is the sitemap formatter — so a plain <c>curl</c> gets a sitemap.
    /// </summary>
    [Fact]
    public async Task UrlSet_WithNoAcceptHeader_IsServedAsXml()
    {
        using var host = CreateHost(SampleUrlSet());

        var response = await host.GetTestClient().GetAsync("/sitemap.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/xml", response.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>
    /// <c>CanWriteType</c> admits only the two sitemap roots and <c>AddControllersWithViews</c>
    /// registers no other XML formatter, so an unrelated model asked for as XML is answered as
    /// JSON — with a <c>Content-Type</c> the client did not ask for, rather than a 406, because
    /// <c>ReturnHttpNotAcceptable</c> is left at its default.
    /// </summary>
    [Fact]
    public async Task AnUnrelatedModel_AskedForAsXml_IsAnsweredAsJson()
    {
        using var host = CreateHost(new { Name = "not a sitemap" });

        var response = await GetAsync(host, "application/xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>
    /// The wire-level consequence of D-S4: a <c>UrlSet</c> built from a type that has no
    /// modification date at all still tells crawlers year 1 and hourly churn.
    /// </summary>
    [Fact]
    public async Task UrlSet_WithUnsetLastModAndChangeFreq_ServesYearOneAndHourly_KnownBug()
    {
        using var host = CreateHost(new UrlSet([new Url { Loc = "https://example.org/a" }]));

        var body = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();

        Assert.Contains("<lastmod>0001-01-01</lastmod>", body);
        Assert.Contains("<changefreq>hourly</changefreq>", body);
    }

    /// <summary>
    /// The end-to-end counterpart of the D-S1 investigation: the shipped response does NOT carry
    /// the predicted <c>xmlns=""</c> on <c>loc</c>, because the nested mapping supplies the
    /// namespace. Asserted on the wire so a "fix" cannot regress it unnoticed.
    /// </summary>
    [Fact]
    public async Task UrlSet_OnTheWire_HasNoEmptyNamespaceRedeclaration()
    {
        using var host = CreateHost(SampleUrlSet());

        var body = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("xmlns=\"\"", body);
    }

    /// <summary>
    /// A full-fat sitemap — alternates, images and videos on one URL — reaching the wire with
    /// every namespace intact. This is the shape the library exists to produce.
    /// </summary>
    [Fact]
    public async Task UrlSet_WithLinksImagesAndVideos_KeepsEveryNamespaceOnTheWire()
    {
        using var host = CreateHost(new UrlSet(
        [
            new Url
            {
                Loc = "https://example.org/a",
                LastMod = new DateTime(2024, 3, 4),
                ChangeFreq = ChangeFreq.Monthly,
                Links = { new Link { Rel = "alternate", Href = "https://example.org/nl/a", HrefLang = "nl" } },
                Images = { new Image { Location = "https://example.org/i.png", Caption = "c" } },
                Videos = { new Video { Title = "T", Duration = 42, Rating = 4.5 } },
            },
        ]));

        var body = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();
        var url = XDocument.Parse(body).Root!.Element(Ns.Sitemap + "url")!;

        Assert.NotNull(url.Element(Ns.Xhtml + "link"));
        Assert.NotNull(url.Element(Ns.Image + "image"));
        Assert.NotNull(url.Element(Ns.Video + "video"));
        Assert.Equal("4.5", url.Element(Ns.Video + "video")!.Element(Ns.Video + "rating")!.Value);
    }

    /// <summary>
    /// The same request under a comma-decimal culture: the response is culture-invariant because
    /// the serializer is, not because the server happens to run under an invariant culture.
    /// </summary>
    [Fact]
    public async Task UrlSet_ServedUnderADutchCulture_StillUsesInvariantNumbersAndDates()
    {
        using var host = CreateHost(new UrlSet(
        [
            new Url
            {
                Loc = "https://example.org/a",
                LastMod = new DateTime(2024, 3, 4),
                Videos = { new Video { Rating = 4.5 } },
            },
        ]));

        using (XmlTestHelpers.WithCulture("nl-BE"))
        {
            var body = await (await GetAsync(host, "application/xml")).Content.ReadAsStringAsync();

            Assert.Contains("<lastmod>2024-03-04</lastmod>", body);
            Assert.Contains(">4.5<", body);
        }
    }
}
