using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

[XmlRoot("sitemap", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9")]
public class Sitemap
{
    /// <summary>URL of the sitemap. Required by the sitemap protocol.</summary>
    [XmlElement("loc", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9")]
    public string? Loc { get; set; }

    /// <summary>Same required-member guard as <see cref="Url.ShouldSerializeLoc"/>.</summary>
    public bool ShouldSerializeLoc()
        => string.IsNullOrWhiteSpace(Loc)
            ? throw new InvalidOperationException($"{nameof(Sitemap)}.{nameof(Loc)} is required: the sitemap protocol requires a <loc> for every <sitemap>.")
            : true;

    /// <summary>DateTime of last modification. Optional; omitted when null.</summary>
    [XmlElement("lastmod", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9", DataType = "date")]
    public DateTime? LastMod { get; set; }

    public bool ShouldSerializeLastMod() => LastMod != null;
}
