﻿using MintPlayer.AspNetCore.SitemapXml.Abstractions;
using MintPlayer.Timestamps;

namespace MintPlayer.AspNetCore.SitemapXml.DependencyInjection;

internal class SitemapXml : ISitemapXml
{
    private int PageCount(int total, int perPage)
    {
        return (total - 1) / perPage + 1;
    }

    /// <summary>Computes a <code>list</code> of sitemap urls (paging)</summary>
    /// <typeparam name="T">Type of data you want to display in a sitemap</typeparam>
    /// <param name="items">List of all items to display in the sitemap-index</param>
    /// <param name="perPage">Number of items in one sitemap</param>
    /// <param name="urlFunc">Function to compute the url</param>
    /// <returns></returns>
    public IEnumerable<Abstractions.Data.Sitemap> GetSitemapIndex<T>(IEnumerable<T> items, int perPage, Func<int, int, string> urlFunc) where T : IUpdateTimestamp
    {
        if (items.Any())
        {
            var pages = PageCount(items.Count(), perPage);
            return Enumerable.Range(1, pages).Select(page =>
            {
                string loc = urlFunc.Invoke(perPage, page);
                return new Abstractions.Data.Sitemap
                {
                    Loc = loc,
                    LastMod = items.Skip((page - 1) * perPage).Take(perPage).Max(item => item.DateUpdate)
                };
            });
        }
        else
        {
            return Enumerable.Empty<Abstractions.Data.Sitemap>();
        }
    }
}
