using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.OpenSearch.Data;

/// <summary>
/// The <c>&lt;Image&gt;</c> element of an <see cref="OpenSearchDescription"/>: the icon a client
/// shows beside <see cref="OpenSearchDescription.ShortName"/> in its search-engine list.
/// </summary>
/// <remarks>
/// The dimensions and media type are advertised rather than measured — the client picks an image by
/// them <i>before</i> fetching it, so a mismatch with the actual file leaves the icon scaled wrong
/// or dropped.
/// </remarks>
public class Image
{
    /// <summary>
    /// Absolute URL of the icon. Held as the element's text content, not an attribute, which is why
    /// this is the only member of the type without an XML attribute name.
    /// </summary>
    [XmlText]
    public string? Url { get; set; }

    /// <summary>Advertised width in pixels. Browsers conventionally look for a 16x16 icon for the search-engine list.</summary>
    [XmlAttribute("width")]
    public int Width { get; set; }

    /// <summary>Advertised height in pixels. Browsers conventionally look for a 16x16 icon for the search-engine list.</summary>
    [XmlAttribute("height")]
    public int Height { get; set; }

    /// <summary>Media type of the file at <see cref="Url"/>, for example <c>image/png</c> or <c>image/x-icon</c>.</summary>
    [XmlAttribute("type")]
    public string? Type { get; set; }
}
