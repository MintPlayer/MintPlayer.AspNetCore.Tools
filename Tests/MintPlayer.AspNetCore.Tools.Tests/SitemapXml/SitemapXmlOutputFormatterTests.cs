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
    /// Pins PRD defect D-S11: the check is exact type equality, so a consumer who subclasses
    /// <c>UrlSet</c> to add their own extension elements silently loses the formatter — and gets
    /// JSON, or a 406, with nothing pointing at the cause.
    /// </summary>
    [Fact]
    public void CanWriteType_ASubclassOfUrlSet_IsRejected_KnownBug()
    {
        Assert.False(new ProbeFormatter().CanWrite(typeof(DerivedUrlSet)));
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
    /// Pins PRD defect D-S24: the <c>context.HttpContext == null</c> guard is dead code.
    /// <c>OutputFormatterWriteContext</c> refuses to be constructed with a null
    /// <c>HttpContext</c> in the first place, so the <see cref="InvalidOperationException"/> it
    /// raises is unreachable — while <c>context</c> itself, which IS nullable at the call site, is
    /// dereferenced with no guard at all.
    /// </summary>
    [Fact]
    public void CreateXmlWriter_TheHttpContextGuardIsUnreachable_KnownBug()
    {
        Assert.Throws<ArgumentNullException>(() => new OutputFormatterWriteContext(
            null!,
            (stream, encoding) => new StreamWriter(stream, encoding),
            typeof(UrlSet),
            new UrlSet()));

        Assert.Throws<NullReferenceException>(
            () => new ProbeFormatter().CreateXmlWriter(null!, new StringWriter(), new XmlWriterSettings()));
    }

    /// <summary>
    /// Second half of D-S24: the guards run in the wrong order to be useful — <c>writer</c> is
    /// validated before <c>context</c> is even looked at, so the one argument that can actually be
    /// null in production is the one checked last (never).
    /// </summary>
    [Fact]
    public void CreateXmlWriter_NullContextAndNullWriter_ReportsTheWriter_KnownBug()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProbeFormatter().CreateXmlWriter(null!, null!, new XmlWriterSettings()));

        Assert.Equal("writer", exception.ParamName);
    }

    /// <summary>
    /// Pins PRD defect D-S13: the method mutates the <see cref="XmlWriterSettings"/> instance it
    /// is handed. The base formatter hands it its own long-lived <c>WriterSettings</c>, so this is
    /// a per-request write to shared state.
    /// </summary>
    [Fact]
    public void CreateXmlWriter_MutatesTheSettingsItIsGiven_KnownBug()
    {
        var formatter = new ProbeFormatter();
        formatter.WriterSettings.CloseOutput = true;

        using var writer = formatter.CreateXmlWriter(CreateContext(), new StringWriter(), formatter.WriterSettings);

        Assert.False(formatter.WriterSettings.CloseOutput);
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
    /// A whitespace-only URL still produces a stylesheet reference, because the check is
    /// <c>IsNullOrEmpty</c> rather than <c>IsNullOrWhiteSpace</c> — the same blind spot as D-S9's
    /// <c>NullIfEmpty</c>.
    /// </summary>
    [Fact]
    public void CreateXmlWriter_WithAWhitespaceStylesheetUrl_StillWritesTheInstruction_KnownGap()
    {
        var xml = WriteDocument(new ProbeFormatter(), CreateContext(ServicesWithStylesheet("   ")));

        Assert.Contains("href=\"   \"", xml);
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
    /// Pins PRD defect D-S12: <c>StylesheetUrl</c> is interpolated into the processing
    /// instruction with no escaping, so a configured value containing a double quote injects
    /// arbitrary pseudo-attributes into the emitted instruction.
    /// </summary>
    [Fact]
    public void CreateXmlWriter_StylesheetUrlContainingAQuote_IsInjectedRaw_KnownBug()
    {
        var xml = WriteDocument(new ProbeFormatter(), CreateContext(ServicesWithStylesheet("/s.xsl\" alternate=\"yes")));

        Assert.Contains("href=\"/s.xsl\" alternate=\"yes\"", xml);
    }

    /// <summary>
    /// Second half of D-S12: a value containing the instruction terminator does not break
    /// well-formedness, because <see cref="XmlWriter"/> silently rewrites <c>?&gt;</c> as
    /// <c>?&#160;&gt;</c> inside a processing instruction. So the document still parses — with a
    /// stylesheet href that is not the configured one, and no error anywhere.
    /// </summary>
    /// <remarks>
    /// Pinned as the boundary of D-S12: the writer limits the damage to a corrupted href, which
    /// is why the injection in the test above (a bare double quote, which the writer does NOT
    /// rewrite) is the severe case.
    /// </remarks>
    [Fact]
    public void CreateXmlWriter_StylesheetUrlContainingTheInstructionTerminator_IsSilentlyRewritten_KnownBug()
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
    /// Nothing validates that the URL is even a URL, so a relative value is emitted as-is and
    /// resolves differently depending on the depth of the requesting path (D-S14).
    /// </summary>
    [Fact]
    public void CreateXmlWriter_RelativeStylesheetUrl_IsEmittedWithoutALeadingSlash_KnownGap()
    {
        var xml = WriteDocument(new ProbeFormatter(), CreateContext(ServicesWithStylesheet("sitemap.xsl")));

        Assert.Contains("href=\"sitemap.xsl\"", xml);
    }
}
