using MintPlayer.AspNetCore.SitemapXml.Abstractions.Enums;
using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

[XmlRoot("url")]
public class Url
{
    /// <summary>URL of the resource</summary>
    [XmlElement("loc")]
    public string Loc { get; set; }

    /// <summary>Last modification of the resource</summary>
    [XmlElement("lastmod", DataType = "date")]
    public DateTime LastMod { get; set; }

    /// <summary>Change frequency of the resource</summary>
    [XmlElement("changefreq")]
    public ChangeFreq ChangeFreq { get; set; }

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
