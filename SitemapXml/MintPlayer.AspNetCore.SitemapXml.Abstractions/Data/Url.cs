using MintPlayer.AspNetCore.SitemapXml.Abstractions.Enums;
using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

[XmlRoot("url")]
public class Url
{
    /// <summary>URL of the resource. Required by the sitemap protocol.</summary>
    [XmlElement("loc")]
    public string? Loc { get; set; }

    /// <summary>
    /// Guards the one member the sitemap protocol makes mandatory.
    /// </summary>
    /// <remarks>
    /// <see cref="XmlSerializer"/> silently skips a null <see cref="string"/>, so without this a
    /// <c>&lt;url&gt;</c> with no <c>&lt;loc&gt;</c> reaches the crawler and is rejected there,
    /// hours later and out of sight. Failing while writing turns that into a stack trace at the
    /// request that produced it.
    /// </remarks>
    public bool ShouldSerializeLoc()
        => string.IsNullOrWhiteSpace(Loc)
            ? throw new InvalidOperationException($"{nameof(Url)}.{nameof(Loc)} is required: the sitemap protocol requires a <loc> for every <url>.")
            : true;

    /// <summary>Last modification of the resource. Optional; omitted when null.</summary>
    [XmlElement("lastmod", DataType = "date")]
    public DateTime? LastMod { get; set; }

    public bool ShouldSerializeLastMod() => LastMod != null;

    /// <summary>Change frequency of the resource. Optional; omitted when null.</summary>
    [XmlElement("changefreq")]
    public ChangeFreq? ChangeFreq { get; set; }

    public bool ShouldSerializeChangeFreq() => ChangeFreq != null;

    /// <summary>List of alternate links</summary>
    [XmlElement("link", Namespace = "http://www.w3.org/1999/xhtml")]
    public List<Link> Links { get; set; } = new List<Link>();

    /// <summary>List of images</summary>
    [XmlElement("image", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public List<Image> Images { get; set; } = new List<Image>();

    /// <summary>List of videos</summary>
    [XmlElement("video", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public List<Video> Videos { get; set; } = new List<Video>();
}
