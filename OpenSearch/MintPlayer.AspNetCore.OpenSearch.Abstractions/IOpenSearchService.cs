using Microsoft.AspNetCore.Mvc;

namespace MintPlayer.AspNetCore.OpenSearch.Abstractions;

public interface IOpenSearchService
{
    Task<IEnumerable<string>> ProvideSuggestions(string? searchTerms);
    Task<RedirectResult> PerformSearch(string? searchTerms);
}