using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Enums;

/// <summary>The seven <c>changefreq</c> values defined by the sitemap protocol.</summary>
/// <remarks>
/// <para>
/// The value describes how often the page is <em>expected</em> to change, and is a hint rather
/// than an instruction: a crawler may ignore it, and it says nothing about how often the page
/// will actually be visited. It also describes the page, not the sitemap — a page listed as
/// <see cref="Hourly"/> is not guaranteed to be recrawled hourly.
/// </para>
/// <para>
/// <c>Always</c>, <c>Weekly</c> and <c>Never</c> are APPENDED rather than inserted in spec order
/// (which would read <c>always, hourly, daily, weekly, monthly, yearly, never</c>). The underlying
/// integers are part of the wire format for anyone who persisted them, so inserting would silently
/// change the meaning of stored data; the enum is therefore ordered by the history of the package,
/// not by the protocol. Never renumber these.
/// </para>
/// </remarks>
public enum ChangeFreq
{
    /// <summary>The page is expected to change about once an hour. For news front pages and similar.</summary>
    [XmlEnum("hourly")]
    Hourly,
    /// <summary>The page is expected to change about once a day.</summary>
    [XmlEnum("daily")]
    Daily,
    /// <summary>The page is expected to change about once a month.</summary>
    [XmlEnum("monthly")]
    Monthly,
    /// <summary>The page is expected to change about once a year.</summary>
    [XmlEnum("yearly")]
    Yearly,

    // Appended in the 11.0.0 major — see the remarks above.
    /// <summary>
    /// The page changes on every access, so nothing about it can be cached. Reserve it for
    /// genuinely dynamic pages; claiming it for a static page just wastes crawl budget.
    /// </summary>
    [XmlEnum("always")]
    Always,
    /// <summary>The page is expected to change about once a week.</summary>
    [XmlEnum("weekly")]
    Weekly,
    /// <summary>
    /// The page will not change again — an archived document or a permanent URL. Crawlers may
    /// still revisit it, but they are told not to expect anything new.
    /// </summary>
    [XmlEnum("never")]
    Never
}
