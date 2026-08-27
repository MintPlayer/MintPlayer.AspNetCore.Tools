namespace MintPlayer.AspNetCore.OpenSearch.Abstractions;

/// <summary>Where a search request must be sent, and with which kind of redirect.</summary>
/// <param name="Url">Absolute or site-relative target. Emitted verbatim as the <c>Location</c> header.</param>
/// <param name="Permanent">
/// Whether the client may cache the redirect and stop asking: <see langword="true"/> selects the
/// permanent code (301 or 308), <see langword="false"/> the temporary one (302 or 307). Say
/// <see langword="true"/> only for a target that will not change — a search page whose URL depends
/// on the query does change, and a cached permanent redirect is close to impossible to retract.
/// </param>
/// <param name="PreserveMethod">
/// Whether the client must repeat the original method and body instead of switching to
/// <c>GET</c>: <see langword="true"/> selects the method-preserving code (307 or 308),
/// <see langword="false"/> the classic one (302 or 301). The search endpoint is reached by
/// <c>GET</c>, so this normally makes no observable difference.
/// </param>
/// <remarks>
/// The two flags combine into exactly one status code: 302 by default, 301 when
/// <paramref name="Permanent"/>, 307 when <paramref name="PreserveMethod"/>, and 308 when both.
/// <para>
/// Deliberately not an MVC action result: the contract package stays free of any ASP.NET
/// dependency, so a non-MVC host — or a unit test — can implement and assert on it without
/// referencing the web framework. It replaced a <c>RedirectResult</c> return, which forced
/// every consumer of the abstraction to reference ASP.NET Core MVC just to name the type.
/// </para>
/// </remarks>
public sealed record OpenSearchRedirect(string Url, bool Permanent = false, bool PreserveMethod = false);
