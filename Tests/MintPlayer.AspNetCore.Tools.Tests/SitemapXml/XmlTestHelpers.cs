using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

/// <summary>The four XML namespaces the sitemap DTOs are spread across.</summary>
internal static class Ns
{
    public static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";
    public static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
    public static readonly XNamespace Image = "http://www.google.com/schemas/sitemap-image/1.1";
    public static readonly XNamespace Video = "http://www.google.com/schemas/sitemap-video/1.1";
    public static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
}

/// <summary>
/// Serialization helpers shared by the SitemapXml suites.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SerializeToString"/> deliberately reproduces what
/// <c>MintPlayer.AspNetCore.SitemapXml.Formatters.XmlSerializerOutputFormatter.Serialize</c>
/// does — an <see cref="XmlSerializerNamespaces"/> mapping the empty prefix to the empty
/// namespace — because that call is what suppresses the <c>xsi</c>/<c>xsd</c> declarations the
/// serializer otherwise volunteers. Testing the DTOs without it would assert a shape the library
/// never emits.
/// </para>
/// <para>
/// Assertions go through <see cref="SerializeToDocument"/> and
/// <see cref="XName.Namespace"/> wherever the subject is structure. Whole-document string
/// comparison is avoided on purpose: <c>XmlWriterSettings.NewLineHandling</c> interacts with the
/// parser's newline normalisation, so a multi-line golden string is platform-dependent
/// (<c>\r\n</c> on Windows, <c>\n</c> on the Linux runner). Raw strings appear only where the
/// LEXICAL form is itself the subject — dates, decimals, escaping.
/// </para>
/// </remarks>
internal static class XmlTestHelpers
{
    public static string SerializeToString(object value, Type? asType = null)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true,
            Encoding = Encoding.UTF8,
        };

        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, settings))
        {
            var serializer = new XmlSerializer(asType ?? value.GetType());
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add(string.Empty, string.Empty);
            serializer.Serialize(writer, value, namespaces);
        }

        return builder.ToString();
    }

    public static XDocument SerializeToDocument(object value, Type? asType = null)
        => XDocument.Parse(SerializeToString(value, asType));

    /// <summary>Deserializes back, for the round-trip assertions.</summary>
    public static T Deserialize<T>(string xml)
    {
        using var reader = new StringReader(xml);
        return (T)new XmlSerializer(typeof(T)).Deserialize(reader)!;
    }

    /// <summary>
    /// Sets <see cref="CultureInfo.CurrentCulture"/> and
    /// <see cref="CultureInfo.CurrentUICulture"/> for the duration of the returned scope.
    /// </summary>
    /// <remarks>
    /// <c>InvariantGlobalization</c> is off in this repo, so ICU is live on the Linux CI runner and
    /// its data differs from Windows NLS. Every test whose subject is a formatted value therefore
    /// pins the culture explicitly rather than inheriting the machine's.
    /// </remarks>
    public static IDisposable WithCulture(string name) => new CultureScope(name);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo culture;
        private readonly CultureInfo uiCulture;

        public CultureScope(string name)
        {
            culture = CultureInfo.CurrentCulture;
            uiCulture = CultureInfo.CurrentUICulture;
            var replacement = new CultureInfo(name);
            CultureInfo.CurrentCulture = replacement;
            CultureInfo.CurrentUICulture = replacement;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}
