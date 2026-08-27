namespace MintPlayer.AspNetCore.OpenSearch.Abstractions;

/// <summary>
/// The search behaviour behind the app's OpenSearch engine: what to autocomplete while the user
/// types, and where to send them once they submit. Implement it and register it with
/// <c>AddOpenSearch&lt;T&gt;</c>; the endpoints mapped by <c>MapOpenSearch</c> resolve it per request.
/// </summary>
public interface IOpenSearchService
{
    /// <summary>
    /// Produces the autocomplete entries for a partially typed query. Called from the endpoint the
    /// description document advertises as <c>application/x-suggestions+json</c>, which the browser
    /// polls on every keystroke — so this runs on a hot path and should be cheap.
    /// </summary>
    /// <param name="searchTerms">
    /// What the user has typed so far. <see langword="null"/> or empty when the browser polls
    /// without a query, in which case returning an empty sequence is correct.
    /// </param>
    /// <returns>
    /// The suggestions, in the order the client should display them. Return an empty sequence rather
    /// than <see langword="null"/> for "nothing to suggest".
    /// </returns>
    Task<IEnumerable<string>> ProvideSuggestions(string? searchTerms);

    /// <summary>Resolves the search page the user must be sent to for <paramref name="searchTerms"/>.</summary>
    /// <param name="searchTerms">
    /// The submitted query, taken from the query-string parameter named by
    /// <c>OpenSearchOptions.SearchTermsParameter</c>. <see langword="null"/> or empty when the user
    /// searched for nothing; point them at the plain search page in that case.
    /// </param>
    /// <returns>
    /// Where to redirect, and with which status code. Must be non-null and carry a non-blank URL —
    /// the endpoint has nothing to answer with otherwise, and fails loudly instead of emitting a
    /// redirect with no <c>Location</c>.
    /// </returns>
    Task<OpenSearchRedirect> PerformSearch(string? searchTerms);
}
