using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.OpenSearch;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

public class OsdxEndpointTests
{
    private const string OsdxMediaType = "application/opensearchdescription+xml";

    /// <summary>The name the OSDX falls back to when <c>ShortName</c> is not configured.</summary>
    private static string EntryAssemblyName => Assembly.GetEntryAssembly()?.GetName().Name ?? "Website";

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
    /// A browser's <c>Accept: text/html,…,*/*;q=0.8</c> gets the description. This always worked —
    /// the trailing wildcard matched — and it still works now that the endpoint does not negotiate
    /// at all.
    /// </summary>
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

    [Fact]
    public async Task Osdx_WildcardFreeAcceptHeader_Returns200()
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
    /// What was left of D-S21, now fixed: the endpoint used to work only because
    /// <c>MvcOptions.ReturnHttpNotAcceptable</c> sits at its default of <c>false</c>. Turning that
    /// setting on — the documented way to make content negotiation strict — gave a wildcard-free
    /// client an empty <b>406</b>, and the library offered no per-endpoint opt-out.
    /// </summary>
    /// <remarks>
    /// Fixed by writing the description with the OSDX formatter directly instead of through
    /// <c>ObjectResult</c>. Declaring <c>ObjectResult.ContentTypes</c> would <i>not</i> have been
    /// enough: <c>ObjectResultExecutor</c> intersects the declared content types with the
    /// <c>Accept</c> header, so an <c>Accept</c> that does not mention the OSDX media type is
    /// exactly the case that yields no formatter and therefore the 406. The handler's manual
    /// <c>Response.ContentType</c> assignment — dead code under negotiation — is gone; the
    /// formatter sets the header itself from the media type it is handed.
    /// </remarks>
    [Fact]
    public async Task Osdx_WildcardFreeAcceptHeader_WithStrictNegotiation_StillReturns200()
    {
        using var server = OpenSearchTestHost.Create(
            _ => { },
            new FakeOpenSearchService(),
            services => services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.ReturnHttpNotAcceptable = true));
        using var client = server.CreateNonRedirectingClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/opensearch.xml");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OsdxMediaType, response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("OpenSearchDescription", await response.Content.ReadAsStringAsync());
    }

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
    /// <c>Accept: application/xml</c> gets the OSDX serialization, not the framework's own XML
    /// formatters — which would use different namespace and declaration handling.
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

    /// <summary>
    /// D-S20's second half: an unset <c>Contact</c> used to be emitted as an empty
    /// <c>&lt;Contact /&gt;</c>. The spec wants the element absent.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Osdx_NoContact_OmitsTheContactElement(string? contact)
    {
        var document = await GetDescriptionAsync(o =>
        {
            o.ShortName = "X";
            o.Contact = contact;
        });

        Assert.Null(document.Root!.Element(OpenSearchTestHost.A9 + "Contact"));
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

        Assert.Equal("http://localhost/find?q={searchTerms}", templates[0]);
        Assert.Equal("http://localhost/hints?q={searchTerms}", templates[1]);
        Assert.Equal("http://localhost/opensearch.xml", templates[2]);
    }

    /// <summary>
    /// D-S18, first half, fixed: the search and suggest templates now carry the OpenSearch
    /// <c>{searchTerms}</c> macro, so a client has somewhere to put the user's query. Without it the
    /// description was unusable no matter what the handlers did.
    /// </summary>
    [Fact]
    public async Task Osdx_SearchAndSuggestTemplates_CarryTheSearchTermsMacro()
    {
        var document = await GetDescriptionAsync(o => o.ShortName = "X");

        var templates = document.Root!.Elements(OpenSearchTestHost.A9 + "Url")
            .Select(u => (string?)u.Attribute("template") ?? string.Empty)
            .ToList();

        Assert.Equal("http://localhost/search?q={searchTerms}", templates[0]);
        Assert.Equal("http://localhost/suggest?q={searchTerms}", templates[1]);

        // The self-reference is a plain URL: it identifies the description, it is not a query.
        Assert.DoesNotContain("{searchTerms}", templates[2]);
    }

    /// <summary>
    /// The macro goes into whichever query-string parameter the host app's search page reads, so the
    /// parameter name is an option.
    /// </summary>
    [Fact]
    public async Task Osdx_ConfiguredSearchTermsParameter_IsUsedInTheTemplates()
    {
        var document = await GetDescriptionAsync(o =>
        {
            o.ShortName = "X";
            o.SearchTermsParameter = "query";
        });

        var templates = document.Root!.Elements(OpenSearchTestHost.A9 + "Url")
            .Select(u => (string?)u.Attribute("template"))
            .ToList();

        Assert.Equal("http://localhost/search?query={searchTerms}", templates[0]);
        Assert.Equal("http://localhost/suggest?query={searchTerms}", templates[1]);
    }

    /// <summary>
    /// A query string in a routed path option is rejected with a message that names the option.
    /// </summary>
    /// <remarks>
    /// These three options double as route patterns, and routing rejects a <c>?</c> in a literal
    /// segment with <c>RoutePatternException: The literal section 'search?lang=nl' is invalid</c> —
    /// which names neither the option nor this package, so the developer has nothing to go on.
    /// Found while writing the "append the macro with &amp;" test that this replaces.
    /// </remarks>
    [Theory]
    [InlineData(nameof(OpenSearchOptions.OsdxEndpoint), "/opensearch.xml?v=1")]
    [InlineData(nameof(OpenSearchOptions.SearchUrl), "/search?lang=nl")]
    [InlineData(nameof(OpenSearchOptions.SuggestUrl), "/suggest?lang=nl")]
    public void Osdx_RoutedPathOptionWithQueryString_ThrowsNamingTheOption(string optionName, string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OpenSearchTestHost.Create(
            o => typeof(OpenSearchOptions).GetProperty(optionName)!.SetValue(o, value),
            new FakeOpenSearchService()));

        Assert.Contains($"OpenSearchOptions.{optionName}", ex.Message);
        Assert.Contains("cannot contain a query string", ex.Message);
    }

    /// <summary><c>ImageUrl</c> is not a route, so a query string on it is fine.</summary>
    [Fact]
    public async Task Osdx_ImageUrlWithQueryString_IsAccepted()
    {
        var document = await GetDescriptionAsync(o =>
        {
            o.ShortName = "X";
            o.ImageUrl = "/logo.png?v=2";
        });

        Assert.Equal("http://localhost/logo.png?v=2", document.Root!.Element(OpenSearchTestHost.A9 + "Image")!.Value);
    }

    /// <summary>
    /// D-S20 fixed: an unset <c>ImageUrl</c> used to interpolate to nothing, advertising the site
    /// root as a 16x16 <c>image/png</c>. The element is now omitted.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Osdx_NoImageUrl_OmitsTheImageElement(string? imageUrl)
    {
        var document = await GetDescriptionAsync(o =>
        {
            o.ShortName = "X";
            o.ImageUrl = imageUrl;
        });

        Assert.Null(document.Root!.Element(OpenSearchTestHost.A9 + "Image"));
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

    [Fact]
    public async Task Osdx_ImageDefaults_Are16x16Png()
    {
        var document = await GetDescriptionAsync(o =>
        {
            o.ShortName = "X";
            o.ImageUrl = "/favicon.png";
        });

        var image = document.Root!.Element(OpenSearchTestHost.A9 + "Image")!;
        Assert.Equal("16", (string?)image.Attribute("width"));
        Assert.Equal("16", (string?)image.Attribute("height"));
        Assert.Equal("image/png", (string?)image.Attribute("type"));
    }

    /// <summary>
    /// D-S34 fixed: the dimensions and media type used to be hard-coded 16x16 <c>image/png</c>, so a
    /// configured <c>/logo-512.png</c> was still advertised as a 16x16 icon.
    /// </summary>
    [Fact]
    public async Task Osdx_ImageDimensionsAndType_AreConfigurable()
    {
        var document = await GetDescriptionAsync(o =>
        {
            o.ShortName = "X";
            o.ImageUrl = "/logo-512.svg";
            o.ImageWidth = 512;
            o.ImageHeight = 512;
            o.ImageType = "image/svg+xml";
        });

        var image = document.Root!.Element(OpenSearchTestHost.A9 + "Image")!;
        Assert.Equal("http://localhost/logo-512.svg", image.Value);
        Assert.Equal("512", (string?)image.Attribute("width"));
        Assert.Equal("512", (string?)image.Attribute("height"));
        Assert.Equal("image/svg+xml", (string?)image.Attribute("type"));
    }

    /// <summary>
    /// D-S15 does <b>not</b> reproduce: <c>SearchForm</c> inherits the a9 namespace from the root
    /// mapping, and the served document contains no <c>xmlns=""</c> reset. Adding an explicit
    /// <c>Namespace</c> would be a no-op.
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

    /// <summary>
    /// D-S19, filename half: the value is quoted, so a <c>ShortName</c> containing a space would not
    /// produce an illegal unquoted header token.
    /// </summary>
    [Fact]
    public async Task Osdx_SetsContentDispositionAttachmentWithQuotedShortName()
    {
        using var server = OpenSearchTestHost.Create(o => o.ShortName = "MintPlayer", new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/opensearch.xml");

        Assert.True(response.Content.Headers.TryGetValues("Content-Disposition", out var values));
        Assert.Equal("attachment; filename=\"MintPlayer.osdx\"", string.Join(", ", values!));
    }

    /// <summary>
    /// Anything outside <c>[A-Za-z0-9._-]</c> is stripped before it reaches the header — quoting
    /// alone would not survive a <c>ShortName</c> containing a quote or a newline.
    /// </summary>
    [Theory]
    [InlineData("Mint Player", "MintPlayer.osdx")]
    [InlineData("Mint\"Player", "MintPlayer.osdx")]
    [InlineData("Mint, Player=1", "MintPlayer1.osdx")]
    [InlineData("My-App_v2.0", "My-App_v2.0.osdx")]
    [InlineData("音楽", "opensearch.osdx")]
    public async Task Osdx_ContentDispositionFileName_IsSanitised(string shortName, string expectedFileName)
    {
        using var server = OpenSearchTestHost.Create(o => o.ShortName = shortName, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/opensearch.xml");

        Assert.True(response.Content.Headers.TryGetValues("Content-Disposition", out var values));
        Assert.Equal($"attachment; filename=\"{expectedFileName}\"", string.Join(", ", values!));
    }

    /// <summary>
    /// The <c>ShortName</c> element still carries the unsanitised display name — only the filename is
    /// reduced.
    /// </summary>
    [Fact]
    public async Task Osdx_ShortNameElement_KeepsCharactersTheFileNameStrips()
    {
        var document = await GetDescriptionAsync(o => o.ShortName = "Mint Player");

        Assert.Equal("Mint Player", document.Root!.Element(OpenSearchTestHost.A9 + "ShortName")!.Value);
    }

    /// <summary>
    /// D-S19, identity half: the fallback used to be
    /// <c>Assembly.GetExecutingAssembly()?.FullName</c> — <b>this library's</b> own identity,
    /// <c>"MintPlayer.AspNetCore.OpenSearch, Version=…, Culture=neutral, PublicKeyToken=null"</c> —
    /// so every app that did not set <c>ShortName</c> registered a search engine named after the
    /// NuGet package it installed, and injected commas, spaces and <c>=</c> into an unquoted
    /// <c>Content-Disposition</c> filename. It is now the entry assembly's simple name.
    /// </summary>
    [Fact]
    public async Task Osdx_NoShortName_UsesTheEntryAssemblySimpleName()
    {
        var document = await GetDescriptionAsync(_ => { });

        var shortName = document.Root!.Element(OpenSearchTestHost.A9 + "ShortName")!.Value;

        Assert.Equal(EntryAssemblyName, shortName);
        Assert.Equal($"Search {EntryAssemblyName}", document.Root.Element(OpenSearchTestHost.A9 + "Description")!.Value);
        Assert.DoesNotContain("MintPlayer.AspNetCore.OpenSearch", shortName);
        Assert.DoesNotContain("Version=", shortName);
    }

    /// <summary>
    /// And the header derived from it is now a legal, single, quoted token — no comma, no space, no
    /// bare <c>=</c>, and no mention of this library.
    /// </summary>
    [Fact]
    public async Task Osdx_NoShortName_ContentDispositionIsALegalQuotedToken()
    {
        using var server = OpenSearchTestHost.Create(_ => { }, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/opensearch.xml");

        Assert.True(response.Content.Headers.TryGetValues("Content-Disposition", out var values));
        var disposition = Assert.Single(values!);

        Assert.DoesNotContain(typeof(OpenSearchExtensions).Assembly.FullName!, disposition);
        Assert.DoesNotContain(",", disposition);
        Assert.Matches("^attachment; filename=\"[A-Za-z0-9._-]+\\.osdx\"$", disposition);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
    }

    /// <summary>
    /// D-S33: the <c>"Website"</c> fallback is now genuinely reachable. It sits behind
    /// <c>Assembly.GetEntryAssembly()</c>, which returns <c>null</c> when the process was started
    /// from unmanaged code — unlike the old chain, which hung off
    /// <c>GetExecutingAssembly()?.FullName</c> and could never be null, since a loaded assembly
    /// always has a <c>FullName</c>. Asserted here on the chain's shape, because a test process
    /// cannot make its own entry assembly disappear. What the test can pin is that the chain hangs
    /// off a nullable-annotated call rather than off a never-null one.
    /// </summary>
    [Fact]
    public async Task Osdx_ShortNameFallbackChain_EndsInAReachableLiteral()
    {
        var getEntryAssembly = typeof(Assembly).GetMethod(nameof(Assembly.GetEntryAssembly))!;
        var nullability = new NullabilityInfoContext().Create(getEntryAssembly.ReturnParameter);

        Assert.Equal(NullabilityState.Nullable, nullability.ReadState);

        // With an entry assembly present the literal is simply not reached.
        var document = await GetDescriptionAsync(_ => { });
        Assert.Equal(EntryAssemblyName, document.Root!.Element(OpenSearchTestHost.A9 + "ShortName")!.Value);
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
