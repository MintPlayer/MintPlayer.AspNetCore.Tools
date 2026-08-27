using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

/// <summary>
/// The root <c>&lt;sitemapindex&gt;</c> document: a list of the sitemaps that make up the site,
/// published at the URL a crawler is pointed at when one <see cref="UrlSet"/> is not enough.
/// </summary>
/// <remarks>
/// An index may list up to 50,000 sitemaps and must not exceed 50&#160;MB uncompressed. It lists
/// sitemaps only — indexes cannot be nested, and every sitemap it names must live on the same host
/// as the index itself.
/// </remarks>
[XmlRoot("sitemapindex", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9")]
public class SitemapIndex
{
    /// <summary>Creates an empty index.</summary>
    public SitemapIndex()
    {
    }

    /// <summary>Creates an index listing <paramref name="sitemaps"/>.</summary>
    /// <param name="sitemaps">
    /// Sitemaps to list — typically the result of <c>ISitemapXml.GetSitemapIndex</c>. Enumerated
    /// immediately and copied, so later changes to the source sequence do not affect the index.
    /// </param>
    public SitemapIndex(IEnumerable<Sitemap> sitemaps) : this()
    {
        Sitemaps.AddRange(sitemaps);
    }

    /// <summary>
    /// Namespace declarations written on the root element. A field rather than a property because
    /// that is what <see cref="XmlNamespaceDeclarationsAttribute"/> requires. Unlike
    /// <see cref="UrlSet.xmlns"/> this starts out empty: an index carries no extension elements,
    /// so only the sitemap namespace itself is needed and the serializer supplies that.
    /// </summary>
    [XmlNamespaceDeclarations]
    public XmlSerializerNamespaces xmlns = new XmlSerializerNamespaces();

    /// <summary>The sitemaps this index points at, written in order as <c>&lt;sitemap&gt;</c> elements. Never null; empty by default.</summary>
    [XmlElement("sitemap", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9")]
    public List<Sitemap> Sitemaps { get; set; } = new List<Sitemap>();
}
