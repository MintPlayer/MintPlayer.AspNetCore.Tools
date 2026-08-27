using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.OpenSearch;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

public class OsdxEndpointTests
{
    private const string OsdxMediaType = "application/opensearchdescription+xml";

    private static async Task<XDocument> GetDescriptionAsync(Action<OpenSearchOptions> configure)
    {
        using var server = OpenSearchTestHost.Create(configure, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/opensearch.xml");
        response.EnsureSuccessStatusCode();
        return XDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Osdx_NoAcceptHeader_Returns200WithOpenSearchMediaType()
    {
        using var server = OpenSearchTestHost.Create(_ => { }, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/opensearch.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OsdxMediaType, response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Osdx_AcceptOpenSearchMediaType_Returns200()
    {
        using var server = OpenSearchTestHost.Create(_ => { }, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/opensearch.xml");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(OsdxMediaType));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// D-S21 investigated against a real <c>TestServer</c>: the predicted <b>406</b> does
    /// <b>not</b> happen. OBSERVED: <b>200 OK</b> with
    /// <c>Content-Type: application/opensearchdescription+xml</c> and the full description body,
    /// even for a browser's <c>Accept: text/html,…</c>.
    /// </summary>
    /// <remarks>
    /// Two independent reasons, both of which had to be checked against a running server:
    /// <list type="number">
    /// <item>a real browser's Accept header ends in <c>*/*;q=0.8</c>, and that wildcard matches the
    /// OSDX media type, so negotiation <i>succeeds</i> — it never reaches the failure path;</item>
    /// <item><c>MvcOptions.ReturnHttpNotAcceptable</c> defaults to <b>false</b>, so even a
    /// wildcard-free Accept that matches nothing falls back to the first formatter that can write
    /// the type rather than answering 406. <c>RespectBrowserAcceptHeader = true</c> only makes MVC
    /// <i>try</i> to honour the header.</item>
    /// </list>
    /// What remains of D-S21 is narrower than described: the handler's manual
    /// <c>Response.ContentType</c> assignment is <b>dead code</b> — negotiation sets the header
    /// itself and happens to pick the same value — and the endpoint works only because
    /// <c>ReturnHttpNotAcceptable</c> is left at its default. An app that sets it to <c>true</c>
    /// (the documented way to make content negotiation strict) breaks this endpoint for any client
    /// that does not send a wildcard; see
    /// <see cref="Osdx_WildcardFreeAcceptHeader_WithStrictNegotiation_Returns406_KnownBug"/>.
    /// </remarks>
    [Fact]
    public async Task Osdx_BrowserAcceptHeader_Returns200()
    {
        using var server = OpenSearchTestHost.Create(_ => { }, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/opensearch.xml");
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OsdxMediaType, response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("OpenSearchDescription", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A wildcard-free Accept that matches nothing still gets <b>200</b>, because
    /// <c>ReturnHttpNotAcceptable</c> defaults to false and MVC falls back to the first capable
    /// formatter. OBSERVED: 200 with the OSDX media type — i.e. the response deliberately ignores
    /// what the client asked for.
    /// </summary>
    [Fact]
    public async Task Osdx_WildcardFreeAcceptHeader_Returns200IgnoringTheClientsRequest()
    {
        using var server = OpenSearchTestHost.Create(_ => { }, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/opensearch.xml");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OsdxMediaType, response.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>
    /// The half of D-S21 that does bite: <c>ReturnHttpNotAcceptable = true</c> — the documented way
    /// to make content negotiation strict, and a setting the library never mentions — turns a
    /// wildcard-free <c>Accept: text/html</c> into a <b>406 Not Acceptable</b> with an empty body.
    /// The handler's <c>Response.ContentType</c> assignment cannot prevent it, because the
    /// <c>ObjectResult</c> negotiation that follows ignores it. The library gives a consumer no way
    /// to opt out for this one endpoint.
    /// </summary>
    [Fact]
    public async Task Osdx_WildcardFreeAcceptHeader_WithStrictNegotiation_Returns406_KnownBug()
    {
        using var server = OpenSearchTestHost.Create(
            _ => { },
            new FakeOpenSearchService(),
            services => services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.ReturnHttpNotAcceptable = true));
        using var client = server.CreateNonRedirectingClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/opensearch.xml");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A real browser's Accept header ends in <c>*/*;q=0.8</c>, so it survives even strict
    /// negotiation — which is why D-S21 does not reproduce for the endpoint's primary caller.
    /// </summary>
    [Fact]
    public async Task Osdx_BrowserAcceptHeader_WithStrictNegotiation_StillReturns200()
    {
        using var server = OpenSearchTestHost.Create(
            _ => { },
            new FakeOpenSearchService(),
            services => services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.ReturnHttpNotAcceptable = true));
        using var client = server.CreateNonRedirectingClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/opensearch.xml");
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// <c>Accept: application/xml</c> also negotiates to the OSDX formatter by the same fallback,
    /// which is fortunate — the framework's own XML formatters would serialize the description with
    /// different namespace and declaration handling.
    /// </summary>
    [Fact]
    public async Task Osdx_AcceptApplicationXml_Returns200WithOpenSearchMediaType()
    {
        using var server = OpenSearchTestHost.Create(_ => { }, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/opensearch.xml");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OsdxMediaType, response.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>A wildcard Accept negotiates to the one registered formatter.</summary>
    [Fact]
    public async Task Osdx_AcceptWildcard_Returns200()
    {
        using var server = OpenSearchTestHost.Create(_ => { }, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/opensearch.xml");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Osdx_ConfiguredShortNameAndDescription_AreEmitted()
    {
        var document = await GetDescriptionAsync(o =>
        {
            o.ShortName = "MintPlayer";
            o.Description = "Find music";
        });

        Assert.Equal("MintPlayer", document.Root!.Element(OpenSearchTestHost.A9 + "ShortName")!.Value);
        Assert.Equal("Find music", document.Root.Element(OpenSearchTestHost.A9 + "Description")!.Value);
    }

    [Fact]
    public async Task Osdx_DescriptionOmitted_DerivesFromShortName()
    {
        var document = await GetDescriptionAsync(o => o.ShortName = "MintPlayer");

        Assert.Equal("Search MintPlayer", document.Root!.Element(OpenSearchTestHost.A9 + "Description")!.Value);
    }

    [Fact]
    public async Task Osdx_InputEncoding_IsUtf8()
    {
        var document = await GetDescriptionAsync(o => o.ShortName = "X");

        Assert.Equal("UTF-8", document.Root!.Element(OpenSearchTestHost.A9 + "InputEncoding")!.Value);
    }

    [Fact]
    public async Task Osdx_Contact_IsPassedThrough()
    {
        var document = await GetDescriptionAsync(o =>
        {
            o.ShortName = "X";
            o.Contact = "support@example.com";
        });

        Assert.Equal("support@example.com", document.Root!.Element(OpenSearchTestHost.A9 + "Contact")!.Value);
    }

    [Fact]
    public async Task Osdx_EmitsThreeUrlsWithTheExpectedTypesAndSelfRelation()
    {
        var document = await GetDescriptionAsync(o => o.ShortName = "X");

        var urls = document.Root!.Elements(OpenSearchTestHost.A9 + "Url").ToList();

        Assert.Equal(3, urls.Count);
        Assert.Equal("text/html", (string?)urls[0].Attribute("type"));
        Assert.Equal("application/x-suggestions+json", (string?)urls[1].Attribute("type"));
        Assert.Equal(OsdxMediaType, (string?)urls[2].Attribute("type"));
        Assert.Equal("self", (string?)urls[2].Attribute("rel"));
    }

    [Fact]
    public async Task Osdx_TemplatesAreAbsoluteAgainstTheRequestHost()
    {
        var document = await GetDescriptionAsync(o =>
        {
            o.ShortName = "X";
            o.SearchUrl = "/find";
            o.SuggestUrl = "/hints";
            o.OsdxEndpoint = "/opensearch.xml";
        });

        var templates = document.Root!.Elements(OpenSearchTestHost.A9 + "Url")
            .Select(u => (string?)u.Attribute("template"))
            .ToList();

        Assert.Equal("http://localhost/find", templates[0]);
        Assert.Equal("http://localhost/hints", templates[1]);
        Assert.Equal("http://localhost/opensearch.xml", templates[2]);
    }

    /// <summary>
    /// Pins the first half of D-S18: no template carries the <c>{searchTerms}</c> macro (nor any
    /// other OpenSearch macro), so a client that reads this description has nowhere to put the
    /// user's query. The search and suggest templates are bare endpoint URLs.
    /// </summary>
    [Fact]
    public async Task Osdx_Templates_ContainNoSearchTermsMacro_KnownBug()
    {
        var document = await GetDescriptionAsync(o => o.ShortName = "X");

        var templates = document.Root!.Elements(OpenSearchTestHost.A9 + "Url")
            .Select(u => (string?)u.Attribute("template") ?? string.Empty)
            .ToList();

        Assert.All(templates, t => Assert.DoesNotContain("{", t));
        Assert.All(templates, t => Assert.DoesNotContain("searchTerms", t));
        Assert.All(templates, t => Assert.DoesNotContain("?", t));
    }

    /// <summary>
    /// Pins D-S20: a null <c>ImageUrl</c> interpolates to nothing, so the description advertises the
    /// site root as a 16x16 image/png.
    /// </summary>
    [Fact]
    public async Task Osdx_NullImageUrl_AdvertisesBareHostAsImage_KnownBug()
    {
        var document = await GetDescriptionAsync(o => o.ShortName = "X");

        var image = document.Root!.Element(OpenSearchTestHost.A9 + "Image")!;

        Assert.Equal("http://localhost", image.Value);
        Assert.Equal("16", (string?)image.Attribute("width"));
        Assert.Equal("16", (string?)image.Attribute("height"));
        Assert.Equal("image/png", (string?)image.Attribute("type"));
    }

    [Fact]
    public async Task Osdx_ConfiguredImageUrl_IsMadeAbsolute()
    {
        var document = await GetDescriptionAsync(o =>
        {
            o.ShortName = "X";
            o.ImageUrl = "/favicon.png";
        });

        Assert.Equal("http://localhost/favicon.png", document.Root!.Element(OpenSearchTestHost.A9 + "Image")!.Value);
    }

    /// <summary>
    /// The image dimensions are hard-coded 16x16 regardless of what the configured image actually
    /// is; there is no option to declare a different size.
    /// </summary>
    [Fact]
    public async Task Osdx_ImageDimensions_AreHardCoded16x16_KnownGap()
    {
        var document = await GetDescriptionAsync(o =>
        {
            o.ShortName = "X";
            o.ImageUrl = "/logo-512.png";
        });

        var image = document.Root!.Element(OpenSearchTestHost.A9 + "Image")!;
        Assert.Equal("16", (string?)image.Attribute("width"));
        Assert.Equal("16", (string?)image.Attribute("height"));
    }

    /// <summary>
    /// D-S15 as it reaches the wire: it does not reproduce. <c>SearchForm</c> inherits the a9
    /// namespace from the root mapping, and the served document contains no <c>xmlns=""</c> reset.
    /// The <c>SearchForm</c> value is the site root, which is the one thing here that is correct by
    /// accident rather than by configuration — there is no option for it.
    /// </summary>
    [Fact]
    public async Task Osdx_SearchForm_IsInA9NamespaceAndPointsAtSiteRoot()
    {
        using var server = OpenSearchTestHost.Create(o => o.ShortName = "X", new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var raw = await client.GetStringAsync("/opensearch.xml");
        var document = XDocument.Parse(raw);

        Assert.Null(document.Root!.Element(XNamespace.None + "SearchForm"));
        Assert.Equal("http://localhost/", document.Root.Element(OpenSearchTestHost.A9 + "SearchForm")!.Value);
        Assert.DoesNotContain("xmlns=\"\"", raw);
    }

    [Fact]
    public async Task Osdx_SetsContentDispositionAttachmentWithConfiguredShortName()
    {
        using var server = OpenSearchTestHost.Create(o => o.ShortName = "MintPlayer", new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/opensearch.xml");

        Assert.True(response.Content.Headers.TryGetValues("Content-Disposition", out var values));
        Assert.Equal("attachment; filename=MintPlayer.osdx", string.Join(", ", values!));
    }

    /// <summary>
    /// Pins D-S19. With no <c>ShortName</c> configured the fallback is
    /// <c>Assembly.GetExecutingAssembly()?.FullName</c> — the <b>library's</b> own identity, not the
    /// consuming app's — and it is injected unquoted into <c>Content-Disposition: …; filename=</c>.
    /// The result contains commas, spaces and <c>=</c>, none of which are legal in an unquoted
    /// header token, and it names the wrong product in the file the user saves.
    /// </summary>
    [Fact]
    public async Task Osdx_NoShortName_LeaksLibraryAssemblyNameIntoContentDisposition_KnownBug()
    {
        using var server = OpenSearchTestHost.Create(_ => { }, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/opensearch.xml");
        var libraryFullName = typeof(OpenSearchExtensions).Assembly.FullName!;

        Assert.True(response.Content.Headers.TryGetValues("Content-Disposition", out var values));
        var disposition = string.Join(", ", values!);

        Assert.Contains("MintPlayer.AspNetCore.OpenSearch, Version=", disposition);
        Assert.Contains(libraryFullName, disposition);
        Assert.Contains(",", disposition);
        Assert.Contains("=", disposition);
    }

    /// <summary>
    /// Same fallback, same wrong identity, but visible in the description body — the search engine
    /// the browser registers is named after this library.
    /// </summary>
    [Fact]
    public async Task Osdx_NoShortName_ShortNameElementIsTheLibraryAssemblyName_KnownBug()
    {
        var document = await GetDescriptionAsync(_ => { });
        var libraryFullName = typeof(OpenSearchExtensions).Assembly.FullName!;

        Assert.Equal(libraryFullName, document.Root!.Element(OpenSearchTestHost.A9 + "ShortName")!.Value);
        Assert.Equal($"Search {libraryFullName}", document.Root.Element(OpenSearchTestHost.A9 + "Description")!.Value);
    }

    /// <summary>
    /// The <c>"Website"</c> fallback after the assembly name is unreachable: a loaded assembly
    /// always has a <c>FullName</c>.
    /// </summary>
    [Fact]
    public async Task Osdx_WebsiteFallback_IsUnreachable_KnownGap()
    {
        var document = await GetDescriptionAsync(_ => { });

        Assert.NotEqual("Website", document.Root!.Element(OpenSearchTestHost.A9 + "ShortName")!.Value);
    }

    /// <summary>An OSDX file is downloaded and parsed standalone, so the declaration must survive.</summary>
    [Fact]
    public async Task Osdx_XmlDeclaration_IsPresent()
    {
        using var server = OpenSearchTestHost.Create(o => o.ShortName = "X", new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var raw = await client.GetStringAsync("/opensearch.xml");

        Assert.StartsWith("<?xml", raw.TrimStart('﻿'));
    }
}
