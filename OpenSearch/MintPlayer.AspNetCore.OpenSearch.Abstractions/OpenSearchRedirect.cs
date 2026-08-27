namespace MintPlayer.AspNetCore.OpenSearch.Abstractions;

/// <summary>Where a search request must be sent, and with which kind of redirect.</summary>
/// <param name="Url">Absolute or site-relative target. Emitted verbatim as the <c>Location</c> header.</param>
/// <param name="Permanent">301/308 instead of 302/307.</param>
/// <param name="PreserveMethod">307/308 instead of 302/301.</param>
/// <remarks>
/// Deliberately not an MVC action result: the contract package stays free of any ASP.NET
/// dependency, so a non-MVC host — or a unit test — can implement and assert on it without
/// referencing the web framework.
/// </remarks>
public sealed record OpenSearchRedirect(string Url, bool Permanent = false, bool PreserveMethod = false);
