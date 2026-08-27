using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;
using MintPlayer.AspNetCore.SitemapXml.Options;
using Xunit;
using SitemapFormatter = MintPlayer.AspNetCore.SitemapXml.Formatters.XmlSerializerOutputFormatter;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

/// <summary>
/// Tests for the internal <c>XmlSerializerOutputFormatter</c>. <c>CanWriteType</c> and
/// <c>Serialize</c> are <c>protected</c>, so a probe subclass re-exposes them; the override still
/// dispatches to the library's implementation.
/// </summary>
public class SitemapXmlOutputFormatterTests
{
    private sealed class ProbeFormatter : SitemapFormatter
    {
        public bool CanWrite(Type? type) => CanWriteType(type);

        public void Write(XmlSerializer serializer, XmlWriter writer, object? value)
            => Serialize(serializer, writer, value);
    }

    private sealed class DerivedUrlSet : UrlSet;

    private sealed class DerivedSitemapIndex : SitemapIndex;

    private static OutputFormatterWriteContext CreateContext(IServiceProvider? services = null)
    {
        // DefaultHttpContext.RequestServices is null unless assigned, and the formatter
        // dereferences it unguarded, so every context gets at least an empty provider.
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services ?? new ServiceCollection().BuildServiceProvider(),
        };

        return new OutputFormatterWriteContext(
            httpContext,
            (stream, encoding) => new StreamWriter(stream, encoding),
            typeof(UrlSet),
            new UrlSet());
    }

    private static IServiceProvider ServicesWithStylesheet(string? url) => new ServiceCollection()
        .AddOptions()
        .Configure<SitemapXmlOptions>(options => options.StylesheetUrl = url)
        .BuildServiceProvider();

    // ── media types and settings ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The ctor clears the base formatter's list and adds exactly these two, which is what makes
    /// this formatter win over the built-in one for a sitemap request.
    /// </summary>
    [Fact]
    public void Ctor_SupportsExactlyTextXmlAndApplicationXml()
    {
        var formatter = new ProbeFormatter();

        Assert.Equal(["text/xml", "application/xml"], formatter.SupportedMediaTypes.ToArray());
    }

    /// <summary>
    /// A sitemap has to carry its XML declaration — a crawler reading a bare fragment cannot
    /// determine the encoding. The base formatter omits it by default.
    /// </summary>
    [Fact]
    public void Ctor_KeepsTheXmlDeclaration()
    {
        Assert.False(new ProbeFormatter().WriterSettings.OmitXmlDeclaration);
    }

    // ── CanWriteType ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CanWriteType_UrlSetAndSitemapIndex_AreAccepted()
    {
        var formatter = new ProbeFormatter();

        Assert.True(formatter.CanWrite(typeof(UrlSet)));
        Assert.True(formatter.CanWrite(typeof(SitemapIndex)));
    }

    /// <summary>
    /// Everything else must fall through, so a normal MVC action keeps negotiating to JSON even
    /// though this formatter sits at index 0.
    /// </summary>
    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(object))]
    [InlineData(typeof(Url))]
    [InlineData(typeof(Sitemap))]
    [InlineData(typeof(List<UrlSet>))]
    public void CanWriteType_AnythingElse_IsRejected(Type type)
    {
        Assert.False(new ProbeFormatter().CanWrite(type));
    }

    /// <summary>
    /// PRD defect D-S11: the check used to be exact type equality, so a consumer who subclassed
    /// <c>UrlSet</c> to add their own extension elements silently lost the formatter — and got
    /// JSON, or a 406, with nothing pointing at the cause.
    /// </summary>
    [Fact]
    public void CanWriteType_ASubclassOfUrlSetOrSitemapIndex_IsAccepted()
    {
        var formatter = new ProbeFormatter();

        Assert.True(formatter.CanWrite(typeof(DerivedUrlSet)));
        Assert.True(formatter.CanWrite(typeof(DerivedSitemapIndex)));
    }

    /// <summary>
    /// A null type reaches <c>CanWriteType</c> from the base's <c>CanWriteResult</c> when the
    /// declared type is unknown; it must be rejected rather than throwing.
    /// </summary>
    [Fact]
    public void CanWriteType_Null_IsRejected()
    {
        Assert.False(new ProbeFormatter().CanWrite(null));
    }

    // ── Serialize ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The override exists to pass an <see cref="XmlSerializerNamespaces"/> mapping the empty
    /// prefix to the empty namespace, which suppresses the <c>xsi</c>/<c>xsd</c> declarations
    /// <c>XmlSerializer</c> otherwise volunteers on every root element.
    /// </summary>
    [Fact]
    public void Serialize_SuppressesTheXsiAndXsdDeclarations()
    {
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            new ProbeFormatter().Write(new XmlSerializer(typeof(UrlSet)), writer, new UrlSet());
        }

        var xml = builder.ToString();
        Assert.DoesNotContain("XMLSchema-instance", xml);
        Assert.DoesNotContain("xmlns:xsd", xml);
        Assert.Contains("http://www.sitemaps.org/schemas/sitemap/0.9", xml);
    }

    // ── CreateXmlWriter guards ────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateXmlWriter_NullWriter_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProbeFormatter().CreateXmlWriter(CreateContext(), null!, new XmlWriterSettings()));

        Assert.Equal("writer", exception.ParamName);
    }

    [Fact]
    public void CreateXmlWriter_NullSettings_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProbeFormatter().CreateXmlWriter(CreateContext(), new StringWriter(), null!));

        Assert.Equal("xmlWriterSettings", exception.ParamName);
    }

    /// <summary>
    /// PRD defect D-S24: <c>context</c> is the one argument that can genuinely be null at the call
    /// site, and it used to be the only one with no guard — a null context produced a bare
    /// <see cref="NullReferenceException"/> from the dereference, ahead of the
    /// <c>context.HttpContext == null</c> check that was meant to catch it.
    /// </summary>
    [Fact]
    public void CreateXmlWriter_NullContext_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProbeFormatter().CreateXmlWriter(null!, new StringWriter(), new XmlWriterSettings()));

        Assert.Equal("context", exception.ParamName);
    }

    /// <summary>
    /// Second half of D-S24: the guards now run outermost-argument-first, so the null that made the
    /// call impossible is the one reported. The removed <c>context.HttpContext == null</c> guard was
    /// unreachable — pinned here, because <c>OutputFormatterWriteContext</c> refuses to be
    /// constructed with a null <c>HttpContext</c> in the first place.
    /// </summary>
    [Fact]
    public void CreateXmlWriter_NullContextAndNullWriter_ReportsTheContext()
    {
        Assert.Throws<ArgumentNullException>(() => new OutputFormatterWriteContext(
            null!,
            (stream, encoding) => new StreamWriter(stream, encoding),
            typeof(UrlSet),
            new UrlSet()));

        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProbeFormatter().CreateXmlWriter(null!, null!, new XmlWriterSettings()));

        Assert.Equal("context", exception.ParamName);
    }

    /// <summary>
    /// PRD defect D-S13: the method used to mutate the <see cref="XmlWriterSettings"/> instance it
    /// is handed. The base formatter hands it its own long-lived <c>WriterSettings</c>, so that was
    /// a per-request write to formatter-wide shared state. It now clones before touching anything.
    /// </summary>
    [Fact]
    public void CreateXmlWriter_DoesNotMutateTheSettingsItIsGiven()
    {
        var formatter = new ProbeFormatter();
        formatter.WriterSettings.CloseOutput = true;

        using var writer = formatter.CreateXmlWriter(CreateContext(), new StringWriter(), formatter.WriterSettings);

        Assert.True(formatter.WriterSettings.CloseOutput);
    }

    /// <summary>
    /// The reason the mutation existed in the first place: the base class closes the
    /// <see cref="TextWriter"/> itself, so the <see cref="System.Xml.XmlWriter"/> must not. Set once
    /// in the constructor rather than per request, and re-applied to the clone so a caller passing
    /// their own settings gets it too.
    /// </summary>
    [Fact]
    public void CreateXmlWriter_LeavesTheUnderlyingWriterOpen()
    {
        var formatter = new ProbeFormatter();

        Assert.False(formatter.WriterSettings.CloseOutput);

        var text = new StringWriter();
        using (var writer = formatter.CreateXmlWriter(CreateContext(), text, new XmlWriterSettings { CloseOutput = true, OmitXmlDeclaration = true }))
        {
            writer.WriteStartElement("root");
            writer.WriteEndElement();
        }

        // Would throw ObjectDisposedException had the XmlWriter closed it.
        text.Write("still open");
        Assert.Contains("still open", text.ToString());
    }

    // ── the stylesheet processing instruction ─────────────────────────────────────────────────

    private static string WriteDocument(SitemapFormatter formatter, OutputFormatterWriteContext context)
    {
        var text = new StringWriter();
        using (var writer = formatter.CreateXmlWriter(context, text, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            writer.WriteStartElement("root");
            writer.WriteEndElement();
        }

        return text.ToString();
    }

    [Fact]
    public void CreateXmlWriter_WithAStylesheetUrl_WritesTheProcessingInstruction()
    {
        var xml = WriteDocument(new ProbeFormatter(), CreateContext(ServicesWithStylesheet("/sitemap.xsl")));

        Assert.Contains("<?xml-stylesheet type=\"text/xsl\" href=\"/sitemap.xsl\"?>", xml);
    }

    /// <summary>The instruction belongs in the prolog, ahead of the root element.</summary>
    [Fact]
    public void CreateXmlWriter_WritesTheProcessingInstructionBeforeTheRootElement()
    {
        var xml = WriteDocument(new ProbeFormatter(), CreateContext(ServicesWithStylesheet("/sitemap.xsl")));

        Assert.True(xml.IndexOf("xml-stylesheet", StringComparison.Ordinal) < xml.IndexOf("<root", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreateXmlWriter_WithNoStylesheetUrl_WritesNoProcessingInstruction(string? url)
    {
        var xml = WriteDocument(new ProbeFormatter(), CreateContext(ServicesWithStylesheet(url)));

        Assert.DoesNotContain("xml-stylesheet", xml);
    }

    /// <summary>
    /// PRD defect D-S31: a whitespace-only URL used to produce <c>href="   "</c>, because the guard
    /// was <c>IsNullOrEmpty</c> rather than <c>IsNullOrWhiteSpace</c> — the same blind spot as
    /// D-S9's <c>NullIfEmpty</c>, which now owns the decision for both call sites.
    /// </summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void CreateXmlWriter_WithAWhitespaceStylesheetUrl_WritesNoProcessingInstruction(string url)
    {
        var xml = WriteDocument(new ProbeFormatter(), CreateContext(ServicesWithStylesheet(url)));

        Assert.DoesNotContain("xml-stylesheet", xml);
    }

    /// <summary>
    /// With no options service registered at all the formatter degrades quietly — this is the
    /// path taken by anyone constructing the formatter without <c>AddSitemapXml()</c>.
    /// </summary>
    [Fact]
    public void CreateXmlWriter_WithNoOptionsServiceRegistered_WritesNoProcessingInstruction()
    {
        var xml = WriteDocument(new ProbeFormatter(), CreateContext(new ServiceCollection().BuildServiceProvider()));

        Assert.DoesNotContain("xml-stylesheet", xml);
    }

    /// <summary>
    /// PRD defect D-S12: <c>StylesheetUrl</c> is interpolated into the processing instruction, and
    /// nothing inside a processing instruction is escapable — so a configured value containing a
    /// double quote used to inject arbitrary pseudo-attributes into the emitted instruction. The
    /// quote is now rejected outright rather than escaped, because there is no escape to apply.
    /// </summary>
    [Theory]
    [InlineData("/s.xsl\" alternate=\"yes")]
    [InlineData("/\"")]
    public void CreateXmlWriter_StylesheetUrlContainingAQuote_Throws(string url)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => WriteDocument(new ProbeFormatter(), CreateContext(ServicesWithStylesheet(url))));

        Assert.Contains("double quote", exception.Message);
    }

    /// <summary>
    /// The boundary of D-S12, left as it was. A value containing the instruction terminator does not
    /// break well-formedness, because <see cref="XmlWriter"/> silently rewrites <c>?&gt;</c> as
    /// <c>?&#160;&gt;</c> inside a processing instruction — the document still parses, with a
    /// stylesheet href that is not the configured one.
    /// </summary>
    /// <remarks>
    /// Not rejected by the D-S12 fix, which rejects only the double quote: the writer already limits
    /// the damage here to a corrupted href, and the character is legal in a path. Pinned so the
    /// difference between the two halves stays visible.
    /// </remarks>
    [Fact]
    public void CreateXmlWriter_StylesheetUrlContainingTheInstructionTerminator_IsSilentlyRewritten()
    {
        var xml = WriteDocument(new ProbeFormatter(), CreateContext(ServicesWithStylesheet("/s.xsl?>")));

        Assert.Contains("/s.xsl? >", xml);
        Assert.DoesNotContain("/s.xsl?>", xml);

        var instruction = System.Xml.Linq.XDocument.Parse(xml).Nodes()
            .OfType<System.Xml.Linq.XProcessingInstruction>()
            .Single();
        Assert.Equal("type=\"text/xsl\" href=\"/s.xsl? >\"", instruction.Data);
    }

    /// <summary>
    /// PRD defect D-S14: a relative value used to be emitted as-is, and then resolved against the
    /// depth of the requesting path — so <c>/sitemap.xml</c> found the stylesheet and
    /// <c>/sitemap/100/2</c> did not. The same value is simultaneously a route pattern, so it has to
    /// be rooted; validated in the one place both consumers share.
    /// </summary>
    /// <remarks>
    /// Validated with a plain <c>StartsWith('/')</c> and deliberately NOT with <c>Uri.TryCreate</c>:
    /// for a leading-slash string that answers false on Windows and true on Linux (as
    /// <c>file:///…</c>), which is exactly the shape of bug that passes locally and fails in CI.
    /// </remarks>
    [Theory]
    [InlineData("sitemap.xsl")]
    [InlineData("assets/sitemap.xsl")]
    [InlineData("./sitemap.xsl")]
    public void CreateXmlWriter_RelativeStylesheetUrl_Throws(string url)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => WriteDocument(new ProbeFormatter(), CreateContext(ServicesWithStylesheet(url))));

        Assert.Contains("absolute path", exception.Message);
    }

    /// <summary>
    /// An absolute URL on another host is rooted-only-by-scheme, so it is rejected too. The
    /// stylesheet is served by this application from a route, so a cross-origin href could never
    /// have matched it.
    /// </summary>
    [Fact]
    public void CreateXmlWriter_AbsoluteHttpStylesheetUrl_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => WriteDocument(new ProbeFormatter(), CreateContext(ServicesWithStylesheet("https://cdn.example.org/sitemap.xsl"))));
    }
}
