using MintPlayer.AspNetCore.SitemapXml.Abstractions;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.Timestamps;

namespace MintPlayer.AspNetCore.SitemapXml.DependencyInjection;

[Register(typeof(ISitemapXml), ServiceLifetime.Scoped, "SitemapXmlServices", EGeneratedAccessibility.Internal)]
internal class SitemapXml : ISitemapXml
{
    public int PageCount(int total, int perPage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perPage);

        // Not (total + perPage - 1) / perPage: that overflows for large arguments.
        return total / perPage + (total % perPage == 0 ? 0 : 1);
    }

    /// <summary>Computes a <code>list</code> of sitemap urls (paging)</summary>
    /// <typeparam name="T">Type of data you want to display in a sitemap</typeparam>
    /// <param name="items">List of all items to display in the sitemap-index</param>
    /// <param name="perPage">Number of items in one sitemap</param>
    /// <param name="urlFunc">Function to compute the url</param>
    /// <returns></returns>
    /// <remarks>
    /// The source is walked exactly once and the result is fully computed before returning. The
    /// caller's sequence is typically an <c>IQueryable</c>, where the previous lazy
    /// <c>Any()</c> + <c>Count()</c> + per-page <c>Skip</c>/<c>Take</c> shape cost N+2 database
    /// round trips and could not run at all against a single-pass source.
    /// </remarks>
    public IEnumerable<Abstractions.Data.Sitemap> GetSitemapIndex<T>(IEnumerable<T> items, int perPage, Func<int, int, string> urlFunc) where T : IUpdateTimestamp
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(urlFunc);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perPage);

        var materialized = items as IList<T> ?? [.. items];
        var pages = PageCount(materialized.Count, perPage);

        var sitemaps = new Abstractions.Data.Sitemap[pages];
        for (var page = 1; page <= pages; page++)
        {
            var start = (page - 1) * perPage;
            var end = Math.Min(start + perPage, materialized.Count);

            var lastMod = materialized[start].DateUpdate;
            for (var index = start + 1; index < end; index++)
            {
                if (materialized[index].DateUpdate > lastMod)
                    lastMod = materialized[index].DateUpdate;
            }

            sitemaps[page - 1] = new Abstractions.Data.Sitemap
            {
                Loc = urlFunc.Invoke(perPage, page),
                LastMod = lastMod,
            };
        }

        return sitemaps;
    }
}
