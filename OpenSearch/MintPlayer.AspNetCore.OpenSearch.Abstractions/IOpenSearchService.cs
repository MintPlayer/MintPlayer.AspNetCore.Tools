namespace MintPlayer.AspNetCore.OpenSearch.Abstractions;

public interface IOpenSearchService
{
    Task<IEnumerable<string>> ProvideSuggestions(string? searchTerms);

    /// <summary>Resolves the search page the user must be sent to for <paramref name="searchTerms"/>.</summary>
    Task<OpenSearchRedirect> PerformSearch(string? searchTerms);
}
