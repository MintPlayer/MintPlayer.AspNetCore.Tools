using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

/// <summary>
/// One <c>&lt;sitemap&gt;</c> entry of a <see cref="SitemapIndex"/>: a pointer to another sitemap
/// document, not to a page. The page-level counterpart is <see cref="Url"/>.
/// </summary>
[XmlRoot("sitemap", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9")]
public class Sitemap
{
    /// <summary>
    /// Absolute URL of the sitemap document. Required by the sitemap protocol, must be under 2,048
    /// characters, and must be on the same host as the index that lists it.
    /// </summary>
    [XmlElement("loc", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9")]
    public string? Loc { get; set; }

    /// <summary>Same required-member guard as <see cref="Url.ShouldSerializeLoc"/>.</summary>
    /// <returns>Always <see langword="true"/>; the alternative is an exception.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Loc"/> is null, empty or whitespace.</exception>
    public bool ShouldSerializeLoc()
        => string.IsNullOrWhiteSpace(Loc)
            ? throw new InvalidOperationException($"{nameof(Sitemap)}.{nameof(Loc)} is required: the sitemap protocol requires a <loc> for every <sitemap>.")
            : true;

    /// <summary>
    /// When the referenced sitemap itself last changed — not when the pages inside it did.
    /// Optional; <see langword="null"/> omits the <c>&lt;lastmod&gt;</c> element. Serialized as a
    /// W3C date (<c>YYYY-MM-DD</c>), so any time-of-day component is dropped. A crawler may use it
    /// to skip re-fetching sitemaps it has already seen, so it is worth setting on a large index.
    /// </summary>
    [XmlElement("lastmod", Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9", DataType = "date")]
    public DateTime? LastMod { get; set; }

    /// <inheritdoc cref="Url.ShouldSerializeChangeFreq"/>
    public bool ShouldSerializeLastMod() => LastMod != null;
}
