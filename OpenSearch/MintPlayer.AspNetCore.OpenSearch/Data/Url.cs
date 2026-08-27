using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.OpenSearch.Data;

/// <summary>
/// One <c>&lt;Url&gt;</c> element of an <see cref="OpenSearchDescription"/>: a URL template the
/// client fills in and requests once the user searches. A description document carries several,
/// telling them apart by <see cref="Type"/> and <see cref="Relation"/>.
/// </summary>
public class Url
{
    /// <summary>
    /// Media type of what <see cref="Template"/> returns, and therefore what the client uses this
    /// template <i>for</i>: <c>text/html</c> is the page to navigate the user to,
    /// <c>application/x-suggestions+json</c> is polled for autocomplete as the user types, and
    /// <c>application/opensearchdescription+xml</c> is the description document itself.
    /// </summary>
    [XmlAttribute("type")]
    public string? Type { get; set; }

    /// <summary>HTTP method the client must use for <see cref="Template"/>. <c>GET</c> unless the endpoint genuinely requires otherwise.</summary>
    [XmlAttribute("method")]
    public string? Method { get; set; }

    /// <summary>
    /// The role this template plays, as an OpenSearch <c>rel</c> value. Absent means <c>results</c>
    /// — the template performs a search. <c>self</c> marks the template that points back at the
    /// description document, which is how a client re-fetches or refreshes the engine definition.
    /// </summary>
    [XmlAttribute("rel")]
    public string? Relation { get; set; }

    /// <summary>
    /// The URL, with OpenSearch macros in braces for the client to substitute. This package emits
    /// <c>{searchTerms}</c> — the user's query, encoded per
    /// <see cref="OpenSearchDescription.InputEncoding"/> — as the value of the configured
    /// query-string parameter.
    /// </summary>
    [XmlAttribute("template")]
    public string? Template { get; set; }
}
