using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

[XmlRoot("sitemap", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9")]
public class Sitemap
{
    /// <summary>URL of the sitemap</summary>
    [XmlElement("loc", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9")]
    public string Loc { get; set; }

    /// <summary>DateTime of last modification</summary>
    [XmlElement("lastmod", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9", DataType = "date")]
    public DateTime LastMod { get; set; }
}
