using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

/// <summary>
/// The root <c>&lt;urlset&gt;</c> of a sitemap: the pages of the site, or of one page of a paged
/// sitemap index.
/// </summary>
/// <remarks>
/// The sitemap protocol limits a single <c>&lt;urlset&gt;</c> to 50,000 <see cref="Url"/> entries
/// and 50&#160;MB uncompressed. Past either limit the sitemap has to be split and published behind
/// a <see cref="SitemapIndex"/>; <c>ISitemapXml.GetSitemapIndex</c> does that paging.
/// </remarks>
[XmlRoot("urlset", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9")]
public class UrlSet
{
    /// <summary>
    /// Creates an empty set, with the xhtml, image and video extension namespaces already
    /// declared on the root element so per-<see cref="Url"/> extension elements serialize with
    /// short prefixes instead of repeating the namespace inline.
    /// </summary>
    public UrlSet()
    {
        xmlns.Add("xhtml", "http://www.w3.org/1999/xhtml");
        xmlns.Add("image", "http://www.google.com/schemas/sitemap-image/1.1");
        xmlns.Add("video", "http://www.google.com/schemas/sitemap-video/1.1");
    }

    /// <summary>Creates a set containing <paramref name="urls"/>.</summary>
    /// <param name="urls">
    /// Pages to list. Enumerated immediately and copied, so later changes to the source sequence
    /// do not affect the set. Stay within the protocol's 50,000-entry limit.
    /// </param>
    public UrlSet(IEnumerable<Url> urls) : this()
    {
        Urls.AddRange(urls);
    }

    /// <summary>
    /// Namespace declarations written on the root element. Populated by the constructor; a field
    /// rather than a property because that is what <see cref="XmlNamespaceDeclarationsAttribute"/> requires.
    /// Add to it only to declare a further extension namespace.
    /// </summary>
    [XmlNamespaceDeclarations]
    public XmlSerializerNamespaces xmlns = new XmlSerializerNamespaces();

    /// <summary>The pages of this sitemap, written in order as <c>&lt;url&gt;</c> elements. Never null; empty by default.</summary>
    [XmlElement("url", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9")]
    public List<Url> Urls { get; set; } = new List<Url>();
}
