using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

[XmlType("image", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
public class Image
{
    #region Location
    /// <summary>URL to the image resource. Required by the image sitemap extension.</summary>
    [XmlElement("loc", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public string? Location { get; set; }

    /// <summary>Same required-member guard as <see cref="Url.ShouldSerializeLoc"/>.</summary>
    public bool ShouldSerializeLocation()
        => string.IsNullOrWhiteSpace(Location)
            ? throw new InvalidOperationException($"{nameof(Image)}.{nameof(Location)} is required: the image sitemap extension requires an <image:loc> for every <image:image>.")
            : true;
    #endregion

    #region Caption
    /// <summary>Caption for the image</summary>
    [XmlElement("caption", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public string? Caption { get; set; }
    #endregion

    #region GeoLocation
    /// <summary>Geographical coordinates for the image</summary>
    [XmlElement("geo_location", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public string? GeoLocation { get; set; }
    #endregion

    #region Title
    /// <summary>Title for the image</summary>
    [XmlElement("title", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public string? Title { get; set; }
    #endregion

    #region License
    /// <summary>License terms for use of the image</summary>
    [XmlElement("license", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public string? License { get; set; }
    #endregion
}
