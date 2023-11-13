using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Enums;

public enum ChangeFreq
{
    [XmlEnum("hourly")]
    Hourly,
    [XmlEnum("daily")]
    Daily,
    [XmlEnum("monthly")]
    Monthly,
    [XmlEnum("yearly")]
    Yearly
}
