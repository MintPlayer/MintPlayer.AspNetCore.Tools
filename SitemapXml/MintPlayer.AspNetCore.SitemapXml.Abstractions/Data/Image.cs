using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

/// <summary>
/// One <c>&lt;image:image&gt;</c> entry of Google's image sitemap extension, describing an image
/// shown on the containing <see cref="Url"/>.
/// </summary>
/// <remarks>
/// Only <see cref="Location"/> is required; the other members are optional and an unset one is
/// omitted from the XML rather than written empty. The extension exists to surface images a
/// crawler might not find in the markup (loaded by script, for instance) — it does not replace
/// the <c>alt</c> text on the page.
/// </remarks>
[XmlType("image", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
public class Image
{
    #region Location
    /// <summary>
    /// URL to the image resource. Required by the image sitemap extension. May be on a different
    /// host than the page — a CDN, for example — provided that host is verified for the site.
    /// </summary>
    [XmlElement("loc", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public string? Location { get; set; }

    /// <summary>Same required-member guard as <see cref="Url.ShouldSerializeLoc"/>.</summary>
    /// <returns>Always <see langword="true"/>; the alternative is an exception.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Location"/> is null, empty or whitespace.</exception>
    public bool ShouldSerializeLocation()
        => string.IsNullOrWhiteSpace(Location)
            ? throw new InvalidOperationException($"{nameof(Image)}.{nameof(Location)} is required: the image sitemap extension requires an <image:loc> for every <image:image>.")
            : true;
    #endregion

    #region Caption
    /// <summary>Caption for the image. Optional; omitted when <see langword="null"/>.</summary>
    [XmlElement("caption", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public string? Caption { get; set; }
    #endregion

    #region GeoLocation
    /// <summary>
    /// Where the image was taken, as a human-readable place name rather than coordinates —
    /// <c>"Limerick, Ireland"</c>. Optional; omitted when <see langword="null"/>.
    /// </summary>
    [XmlElement("geo_location", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public string? GeoLocation { get; set; }
    #endregion

    #region Title
    /// <summary>Title for the image. Optional; omitted when <see langword="null"/>.</summary>
    [XmlElement("title", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public string? Title { get; set; }
    #endregion

    #region License
    /// <summary>
    /// URL of the licence the image is available under — a link to the terms, not the terms
    /// themselves. Optional; omitted when <see langword="null"/>.
    /// </summary>
    [XmlElement("license", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public string? License { get; set; }
    #endregion
}
