using MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;
using MintPlayer.Timestamps;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions;

/// <summary>
/// Splits a collection of items across several sitemaps and describes the result as a sitemap
/// index.
/// </summary>
/// <remarks>
/// The sitemap protocol caps a single sitemap at 50,000 URLs (and 50&#160;MB uncompressed), so a
/// large site publishes a <c>&lt;sitemapindex&gt;</c> pointing at paged sitemaps instead of one
/// document. This service does the paging arithmetic and the per-page <c>&lt;lastmod&gt;</c>;
/// rendering is left to the caller's controller actions.
/// </remarks>
public interface ISitemapXml
{
    /// <summary>Number of pages needed to hold <paramref name="total"/> items at <paramref name="perPage"/> items per page.</summary>
    /// <param name="total">Total number of items. Zero yields zero pages.</param>
    /// <param name="perPage">
    /// Items per sitemap. Keep this at or below the protocol's 50,000-URL limit.
    /// </param>
    /// <returns>The page count, rounded up.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="total"/> is negative, or <paramref name="perPage"/> is zero or negative.
    /// </exception>
    int PageCount(int total, int perPage);

    /// <summary>
    /// Builds one <see cref="Sitemap"/> entry per page of <paramref name="items"/>, ready to hand
    /// to <see cref="Data.SitemapIndex"/>.
    /// </summary>
    /// <typeparam name="T">
    /// Item type. It must expose <see cref="IUpdateTimestamp.DateUpdate"/>, because each page's
    /// <c>&lt;lastmod&gt;</c> is the newest update timestamp among the items on that page.
    /// </typeparam>
    /// <param name="items">
    /// All items to be spread across the index, in the order they should be paged. Enumerated
    /// exactly once.
    /// </param>
    /// <param name="perPage">Number of items per sitemap; the same value is passed back to <paramref name="urlFunc"/>.</param>
    /// <param name="urlFunc">
    /// Produces the <c>&lt;loc&gt;</c> of one paged sitemap. Called as
    /// <c>urlFunc(perPage, page)</c> — page size first, then the 1-based page number — and must
    /// return an absolute URL on the same host as the index.
    /// </param>
    /// <returns>
    /// One entry per page, in page order; empty when <paramref name="items"/> is empty. The result
    /// is fully materialized before returning, so the source may be a single-pass sequence.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="urlFunc"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="perPage"/> is zero or negative.</exception>
    IEnumerable<Sitemap> GetSitemapIndex<T>(IEnumerable<T> items, int perPage, Func<int, int, string> urlFunc) where T : IUpdateTimestamp;
}
