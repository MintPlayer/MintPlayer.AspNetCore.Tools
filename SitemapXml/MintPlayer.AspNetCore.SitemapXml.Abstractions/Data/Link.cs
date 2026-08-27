using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

/// <summary>
/// An xhtml <c>&lt;link&gt;</c> attached to a <see cref="Url"/>, used mainly to declare the
/// alternate-language versions of a page so a crawler can treat them as one document.
/// </summary>
/// <remarks>
/// The set of alternates is expected to be self-referential and complete: each page in a group
/// lists every page in the group, itself included. All three members are XML attributes rather
/// than elements, and an unset one is simply absent.
/// </remarks>
public class Link
{
    /// <summary>
    /// Relation between the containing page and <see cref="Href"/>. For alternate-language
    /// versions this is <c>"alternate"</c>.
    /// </summary>
    [XmlAttribute("rel")]
    public string? Rel { get; set; }

    /// <summary>Absolute URL of the linked page.</summary>
    [XmlAttribute("href")]
    public string? Href { get; set; }

    /// <summary>
    /// Language (and optionally region) of the linked page as an IETF language tag — <c>"nl"</c>,
    /// <c>"nl-BE"</c> — or <c>"x-default"</c> for the version to serve when no tag matches.
    /// </summary>
    [XmlAttribute("hreflang")]
    public string? HrefLang { get; set; }
}
