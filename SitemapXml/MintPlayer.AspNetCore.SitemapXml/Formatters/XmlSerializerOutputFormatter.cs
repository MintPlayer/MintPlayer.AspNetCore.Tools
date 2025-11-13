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
        if (writer == null)
            throw new ArgumentNullException(nameof(writer));

        if (xmlWriterSettings == null)
            throw new ArgumentNullException(nameof(xmlWriterSettings));

        if (context.HttpContext == null)
            throw new InvalidOperationException($"The {nameof(XmlSerializerOutputFormatter)} can only be used in an HTTP context");

        // We always close the TextWriter, so the XmlWriter shouldn't.
        xmlWriterSettings.CloseOutput = false;

        var xmlWriter = XmlWriter.Create(writer, xmlWriterSettings);
        if (context.HttpContext.RequestServices.GetService<IOptions<SitemapXmlOptions>>() is IOptions<SitemapXmlOptions> options
            && !string.IsNullOrEmpty(options?.Value?.StylesheetUrl))
               xmlWriter.WriteProcessingInstruction("xml-stylesheet", $@"type=""text/xsl"" href=""{options.Value.StylesheetUrl}""");
        
        return xmlWriter;
    }

    protected override bool CanWriteType(Type? type)
    {
        if (type == typeof(SitemapIndex))
        {
            return true;
        }
        else if (type == typeof(UrlSet))
        {
            return true;
        }
        else
        {
            return false;
        }
    }
}
