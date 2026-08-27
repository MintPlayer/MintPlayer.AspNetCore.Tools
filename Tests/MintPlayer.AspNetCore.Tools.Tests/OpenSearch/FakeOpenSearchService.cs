using MintPlayer.AspNetCore.OpenSearch.Abstractions;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

/// <summary>
/// Hand-written <see cref="IOpenSearchService"/> that records every <c>searchTerms</c> value the
/// library hands it and returns configurable results.
/// </summary>
/// <remarks>
/// The recorded terms are how the search/suggest plumbing is observed from the outside: the
/// endpoints answer 302/200 whether or not the query ever reached the service, so the assertion has
/// to be made on what the service <i>received</i>, not only on the response.
/// </remarks>
internal sealed class FakeOpenSearchService : IOpenSearchService
{
    public List<string?> ReceivedSuggestTerms { get; } = [];

    public List<string?> ReceivedSearchTerms { get; } = [];

    public IEnumerable<string> Suggestions { get; set; } = ["alpha", "beta"];

    public string RedirectUrl { get; set; } = "/results";

    public bool Permanent { get; set; }

    public bool PreserveMethod { get; set; }

    /// <summary>Returns <c>null</c> from <see cref="PerformSearch"/>, which the contract forbids.</summary>
    public bool ReturnNullRedirect { get; set; }

    public Task<IEnumerable<string>> ProvideSuggestions(string? searchTerms)
    {
        ReceivedSuggestTerms.Add(searchTerms);
        return Task.FromResult(Suggestions);
    }

    public Task<OpenSearchRedirect> PerformSearch(string? searchTerms)
    {
        ReceivedSearchTerms.Add(searchTerms);
        return Task.FromResult(ReturnNullRedirect
            ? null!
            : new OpenSearchRedirect(RedirectUrl, Permanent, PreserveMethod));
    }
}
