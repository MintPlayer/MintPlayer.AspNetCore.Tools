namespace MintPlayer.AspNetCore.OpenSearch
{
    public class OpenSearchOptions
    {
        /// <summary>URL where the OpenSearch Description is served. Must start with <c>/</c>. Defaults to <c>/opensearch.xml</c>.</summary>
        public string? OsdxEndpoint { get; set; }

        /// <summary>URL where the user is redirected for the search. Must start with <c>/</c>. Defaults to <c>/search</c>.</summary>
        public string? SearchUrl { get; set; }

        /// <summary>URL to provide suggestions. Must start with <c>/</c>. Defaults to <c>/suggest</c>.</summary>
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

        /// <summary>URL where the image is located. Must start with <c>/</c>. When unset, no <c>&lt;Image&gt;</c> is advertised.</summary>
        public string? ImageUrl { get; set; }

        /// <summary>Width in pixels of the image at <see cref="ImageUrl"/>. Defaults to 16.</summary>
        public int ImageWidth { get; set; } = 16;

        /// <summary>Height in pixels of the image at <see cref="ImageUrl"/>. Defaults to 16.</summary>
        public int ImageHeight { get; set; } = 16;

        /// <summary>Media type of the image at <see cref="ImageUrl"/>. Defaults to <c>image/png</c>.</summary>
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
