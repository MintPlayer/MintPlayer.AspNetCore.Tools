namespace MintPlayer.Timestamps;

/// <summary>
/// An entity that records when it last changed.
/// </summary>
/// <remarks>
/// The persistence layer is expected to refresh the timestamp on every write, including the
/// initial insert — so a freshly created entity has a <see cref="DateUpdate"/> equal to its
/// insert timestamp rather than a default <see cref="DateTime"/>. This is the interface
/// <c>ISitemapXml.GetSitemapIndex</c> requires of the items it pages, because a sitemap index
/// has to publish a <c>&lt;lastmod&gt;</c> per page and derives it from the newest item on
/// that page.
/// </remarks>
public interface IUpdateTimestamp
{
    /// <summary>When the entity last changed. Refreshed by the persistence layer on every write.</summary>
    DateTime DateUpdate { get; set; }
}
