using MintPlayer.AspNetCore.SitemapXml.Abstractions.Enums;
using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

/// <summary>
/// One <c>&lt;url&gt;</c> entry of a sitemap: a page on the site, plus the optional hints the
/// sitemap protocol lets you attach to it.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="Loc"/> is required. Every other member is optional, and an optional member
/// left unset is simply not written — the element is absent from the XML rather than emitted
/// empty or as <c>xsi:nil</c>. That is the point of the <c>ShouldSerialize*</c> methods below.
/// </para>
/// <para>
/// The <see cref="Links"/>, <see cref="Images"/> and <see cref="Videos"/> collections come from
/// extensions layered onto the sitemap schema (xhtml alternate links, and Google's image and
/// video sitemap extensions). Their namespaces are declared by <see cref="UrlSet"/>, so a
/// <see cref="Url"/> serialized outside a <see cref="UrlSet"/> will carry the declarations
/// inline instead.
/// </para>
/// </remarks>
[XmlRoot("url")]
public class Url
{
    /// <summary>
    /// Absolute URL of the page, including the protocol and a trailing slash where the server
    /// uses one. Required by the sitemap protocol, must be under 2,048 characters, and must sit
    /// on the same host as the sitemap that contains it.
    /// </summary>
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
    /// <returns>Always <see langword="true"/>; the alternative is an exception.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Loc"/> is null, empty or whitespace.</exception>
    public bool ShouldSerializeLoc()
        => string.IsNullOrWhiteSpace(Loc)
            ? throw new InvalidOperationException($"{nameof(Url)}.{nameof(Loc)} is required: the sitemap protocol requires a <loc> for every <url>.")
            : true;

    /// <summary>
    /// Date the page last changed. Optional; <see langword="null"/> means "no claim", and the
    /// <c>&lt;lastmod&gt;</c> element is left out entirely. Serialized as a W3C date
    /// (<c>YYYY-MM-DD</c>), so any time-of-day component is dropped.
    /// </summary>
    [XmlElement("lastmod", DataType = "date")]
    public DateTime? LastMod { get; set; }

    /// <inheritdoc cref="ShouldSerializeChangeFreq"/>
    public bool ShouldSerializeLastMod() => LastMod != null;

    /// <summary>
    /// How often the page is expected to change. Optional, and only ever a hint — crawlers treat
    /// it as advisory. <see langword="null"/> omits the <c>&lt;changefreq&gt;</c> element.
    /// </summary>
    [XmlElement("changefreq")]
    public ChangeFreq? ChangeFreq { get; set; }

    /// <summary>
    /// Tells <see cref="XmlSerializer"/> to omit this optional element when it is unset, rather
    /// than write an empty one. Every optional member here has such a method; a nullable value
    /// type would otherwise be serialized as its default.
    /// </summary>
    /// <returns><see langword="true"/> when the member has a value.</returns>
    public bool ShouldSerializeChangeFreq() => ChangeFreq != null;

    /// <summary>
    /// Alternate-language or otherwise related versions of this page, written as xhtml
    /// <c>&lt;link&gt;</c> elements. Empty by default, and an empty list writes nothing.
    /// </summary>
    [XmlElement("link", Namespace = "http://www.w3.org/1999/xhtml")]
    public List<Link> Links { get; set; } = new List<Link>();

    /// <summary>
    /// Images shown on this page, from the image sitemap extension. Empty by default. Google caps
    /// this at 1,000 images per page.
    /// </summary>
    [XmlElement("image", Namespace = "http://www.google.com/schemas/sitemap-image/1.1")]
    public List<Image> Images { get; set; } = new List<Image>();

    /// <summary>
    /// Videos hosted on this page, from the video sitemap extension. Empty by default.
    /// </summary>
    [XmlElement("video", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public List<Video> Videos { get; set; } = new List<Video>();
}
