using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.OpenSearch.Data;

/// <summary>
/// The root of an OpenSearch description document (OSDX), as defined by the
/// <see href="https://github.com/dewitt/opensearch">OpenSearch 1.1 specification</see>. Serving one
/// is what lets a browser add the site to its list of search engines: the document tells the client
/// what to call the engine, and which URL templates to fill in with the user's query.
/// </summary>
/// <remarks>
/// Serialized by this package's own XML formatter, which writes it without namespace prefixes — a
/// prefixed OSDX is well-formed XML but is rejected by several browsers.
/// </remarks>
[XmlRoot("OpenSearchDescription", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
public class OpenSearchDescription
{
    /// <summary>
    /// The name the client displays for this search engine — the entry the user sees in the
    /// browser's search-engine list. The specification caps it at <b>16 characters</b> and forbids
    /// markup; longer values are emitted unchanged, but clients are free to truncate them.
    /// </summary>
    [XmlElement("ShortName", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public string? ShortName { get; set; }

    /// <summary>
    /// Human-readable explanation of what the engine searches, shown by clients that offer more
    /// room than the search-engine list. The specification caps it at <b>1024 characters</b>.
    /// </summary>
    [XmlElement("Description", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public string? Description { get; set; }

    /// <summary>
    /// Character encoding the client must use when it substitutes the user's query into a
    /// <see cref="Urls"/> template. Always <c>UTF-8</c> as emitted by this package.
    /// </summary>
    [XmlElement("InputEncoding", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public string? InputEncoding { get; set; }

    /// <summary>
    /// The URL templates the client may use, one per representation it might ask for. Distinguished
    /// by <see cref="Url.Type"/>: the <c>text/html</c> one is the search page a user is navigated
    /// to, the <c>application/x-suggestions+json</c> one is polled for autocomplete while typing,
    /// and the <c>rel="self"</c> one points back at this document.
    /// </summary>
    [XmlElement("Url", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public List<Url>? Urls { get; set; }

    /// <summary>
    /// Icon the client shows next to <see cref="ShortName"/>. Left <see langword="null"/> — and so
    /// omitted from the document — when the host app configured no image, because an
    /// <c>&lt;Image&gt;</c> element with a guessed URL advertises whatever the site root happens to
    /// serve as the engine's favicon.
    /// </summary>
    [XmlElement("Image", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public Image? Image { get; set; }

    /// <summary>
    /// URL of a human-facing page where the same search can be performed by hand. Not part of the
    /// OpenSearch namespace (hence no namespace on the element), but widely emitted, and used by
    /// clients that want to link the user to the site rather than run the query themselves.
    /// </summary>
    [XmlElement("SearchForm")]
    public string? SearchForm { get; set; }

    /// <summary>
    /// Email address to reach the maintainer of the search engine. Left <see langword="null"/> —
    /// and so omitted from the document — when unconfigured, rather than published as an empty
    /// element.
    /// </summary>
    [XmlElement("Contact", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public string? Contact { get; set; }
}
