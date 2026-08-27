using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Enums;

/// <summary>The seven <c>changefreq</c> values defined by the sitemap protocol.</summary>
/// <remarks>
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
    [XmlEnum("hourly")]
    Hourly,
    [XmlEnum("daily")]
    Daily,
    [XmlEnum("monthly")]
    Monthly,
    [XmlEnum("yearly")]
    Yearly,

    // Appended in the 11.0.0 major — see the remarks above.
    [XmlEnum("always")]
    Always,
    [XmlEnum("weekly")]
    Weekly,
    [XmlEnum("never")]
    Never
}
