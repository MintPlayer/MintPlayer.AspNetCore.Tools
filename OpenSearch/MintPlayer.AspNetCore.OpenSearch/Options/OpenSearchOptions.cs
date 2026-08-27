namespace MintPlayer.AspNetCore.OpenSearch
{
    /// <summary>
    /// How the app's OpenSearch engine describes itself to a browser, and where its three endpoints
    /// live. Configure through <c>AddOpenSearch</c>; the values are read once, when
    /// <c>MapOpenSearch</c> builds the endpoints.
    /// </summary>
    /// <remarks>
    /// <see cref="OsdxEndpoint"/>, <see cref="SearchUrl"/> and <see cref="SuggestUrl"/> are mapped
    /// as <b>route patterns</b>, so each must start with <c>/</c> and must not contain a query
    /// string; name the query-string parameter with <see cref="SearchTermsParameter"/> instead.
    /// </remarks>
    public class OpenSearchOptions
    {
        /// <summary>
        /// Path serving the OpenSearch description document — the URL a page's
        /// <c>&lt;link rel="search"&gt;</c> points at. Mapped as a route pattern: must start with
        /// <c>/</c>, no query string. Defaults to <c>/opensearch.xml</c>.
        /// </summary>
        public string? OsdxEndpoint { get; set; }

        /// <summary>
        /// Path advertised as the engine's <c>text/html</c> search template, and handled by asking
        /// <c>IOpenSearchService.PerformSearch</c> where to redirect the user. Mapped as a route
        /// pattern: must start with <c>/</c>, no query string. Defaults to <c>/search</c>.
        /// </summary>
        public string? SearchUrl { get; set; }

        /// <summary>
        /// Path advertised as the engine's <c>application/x-suggestions+json</c> template, which the
        /// browser polls for autocomplete while the user types. Mapped as a route pattern: must start
        /// with <c>/</c>, no query string. Defaults to <c>/suggest</c>.
        /// </summary>
        public string? SuggestUrl { get; set; }

        /// <summary>
        /// Name of the query-string parameter that carries the user's query on
        /// <see cref="SearchUrl"/> and <see cref="SuggestUrl"/>. Defaults to <c>q</c>.
        /// </summary>
        /// <remarks>
        /// This is what the emitted templates put the OpenSearch <c>{searchTerms}</c> macro in, and
        /// what the endpoints read the query back out of, so it has to match whatever the host
        /// app's own search page expects.
        /// </remarks>
        public string SearchTermsParameter { get; set; } = "q";

        /// <summary>
        /// Site-relative path of the icon shown next to <see cref="ShortName"/> in the browser's
        /// search-engine list; made absolute against the requesting origin. Must start with <c>/</c>
        /// — unlike the three path options it is not routed, so a query string is allowed. When
        /// unset, the <c>&lt;Image&gt;</c> element is omitted from the document altogether rather
        /// than pointed at a guessed location.
        /// </summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// Width in pixels <i>advertised</i> for the image at <see cref="ImageUrl"/>; the file is
        /// never measured. Clients choose an image on this value before fetching it, so a wrong one
        /// leaves the icon scaled badly or ignored. Defaults to 16, the size browsers look for.
        /// </summary>
        public int ImageWidth { get; set; } = 16;

        /// <summary>
        /// Height in pixels <i>advertised</i> for the image at <see cref="ImageUrl"/>; the file is
        /// never measured. Defaults to 16, the size browsers look for.
        /// </summary>
        public int ImageHeight { get; set; } = 16;

        /// <summary>
        /// Media type <i>advertised</i> for the image at <see cref="ImageUrl"/>; the file is never
        /// sniffed. Set it to match what the host actually serves — <c>image/x-icon</c> for a
        /// <c>.ico</c>, <c>image/svg+xml</c> for an SVG. Defaults to <c>image/png</c>.
        /// </summary>
        public string ImageType { get; set; } = "image/png";

        /// <summary>
        /// Short name of your search engine. The OpenSearch 1.1 specification caps this at
        /// <b>16 characters</b>; longer values are emitted unchanged but may be truncated by clients.
        /// Defaults to the entry assembly's simple name.
        /// </summary>
        public string? ShortName { get; set; }

        /// <summary>
        /// Description for your search engine. The OpenSearch 1.1 specification caps this at
        /// <b>1024 characters</b>. Defaults to <c>Search {ShortName}</c>.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>Email to contact when there is an issue with the search engine. Omitted when unset.</summary>
        public string? Contact { get; set; }
    }
}
