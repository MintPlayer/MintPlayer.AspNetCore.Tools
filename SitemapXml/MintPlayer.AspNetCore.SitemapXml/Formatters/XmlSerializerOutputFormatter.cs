using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;
using MintPlayer.AspNetCore.SitemapXml.Options;
using System.Xml;
using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Formatters;

/// <summary>This formatter adds an XML stylesheet reference to each application/xml response</summary>
internal class XmlSerializerOutputFormatter : Microsoft.AspNetCore.Mvc.Formatters.XmlSerializerOutputFormatter
{
    public XmlSerializerOutputFormatter()
    {
        this.WriterSettings.OmitXmlDeclaration = false;

        // We always close the TextWriter, so the XmlWriter shouldn't. Set once here rather than
        // per request: WriterSettings is a single long-lived instance shared by every response.
        this.WriterSettings.CloseOutput = false;

        this.SupportedMediaTypes.Clear();
        this.SupportedMediaTypes.Add("text/xml");
        this.SupportedMediaTypes.Add("application/xml");
    }

    protected override void Serialize(XmlSerializer xmlSerializer, XmlWriter xmlWriter, object? value)
    {
        var ns = new XmlSerializerNamespaces();
        ns.Add(string.Empty, string.Empty);

        xmlSerializer.Serialize(xmlWriter, value, ns);
    }

    public override XmlWriter CreateXmlWriter(OutputFormatterWriteContext context, TextWriter writer, XmlWriterSettings xmlWriterSettings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(xmlWriterSettings);

        // Clone before touching anything: the base class hands us its own shared WriterSettings,
        // so mutating the instance would be a per-request write to formatter-wide state.
        var settings = xmlWriterSettings.Clone();
        settings.CloseOutput = false;

        var xmlWriter = XmlWriter.Create(writer, settings);

        var options = context.HttpContext.RequestServices?.GetService<IOptions<SitemapXmlOptions>>();
        if (StylesheetUrl.Resolve(options?.Value?.StylesheetUrl) is string stylesheetUrl)
            xmlWriter.WriteProcessingInstruction("xml-stylesheet", $@"type=""text/xsl"" href=""{stylesheetUrl}""");

        return xmlWriter;
    }

    /// <remarks>
    /// <c>IsAssignableFrom</c> rather than type equality, so a consumer subclassing
    /// <see cref="UrlSet"/> or <see cref="SitemapIndex"/> to add their own extension elements
    /// keeps this formatter instead of silently falling through to the default XML formatter.
    /// </remarks>
    protected override bool CanWriteType(Type? type)
        => type is not null
            && (typeof(SitemapIndex).IsAssignableFrom(type) || typeof(UrlSet).IsAssignableFrom(type));
}
