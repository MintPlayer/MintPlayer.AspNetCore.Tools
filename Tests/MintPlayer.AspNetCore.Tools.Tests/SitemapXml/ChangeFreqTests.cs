using System.Reflection;
using System.Xml.Serialization;
using MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;
using MintPlayer.AspNetCore.SitemapXml.Abstractions.Enums;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

public class ChangeFreqTests
{
    private static string XmlNameOf(ChangeFreq value)
        => typeof(ChangeFreq).GetField(value.ToString(), BindingFlags.Public | BindingFlags.Static)!
            .GetCustomAttribute<XmlEnumAttribute>()!.Name!;

    /// <summary>
    /// The wire values are lowercase per the sitemap protocol. These are the strings crawlers
    /// read, so they are pinned individually rather than derived.
    /// </summary>
    [Theory]
    [InlineData(ChangeFreq.Hourly, "hourly")]
    [InlineData(ChangeFreq.Daily, "daily")]
    [InlineData(ChangeFreq.Monthly, "monthly")]
    [InlineData(ChangeFreq.Yearly, "yearly")]
    [InlineData(ChangeFreq.Always, "always")]
    [InlineData(ChangeFreq.Weekly, "weekly")]
    [InlineData(ChangeFreq.Never, "never")]
    public void EveryMember_CarriesItsLowercaseSpecValue(ChangeFreq value, string expected)
    {
        Assert.Equal(expected, XmlNameOf(value));
    }

    [Theory]
    [InlineData(ChangeFreq.Hourly, "hourly")]
    [InlineData(ChangeFreq.Daily, "daily")]
    [InlineData(ChangeFreq.Monthly, "monthly")]
    [InlineData(ChangeFreq.Yearly, "yearly")]
    [InlineData(ChangeFreq.Always, "always")]
    [InlineData(ChangeFreq.Weekly, "weekly")]
    [InlineData(ChangeFreq.Never, "never")]
    public void EveryMember_SerializesToItsSpecValue(ChangeFreq value, string expected)
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet(
        [
            new Url { Loc = "https://example.org/a", ChangeFreq = value },
        ]));

        Assert.Equal(
            expected,
            document.Root!.Element(Ns.Sitemap + "url")!.Element(Ns.Sitemap + "changefreq")!.Value);
    }

    /// <summary>
    /// PRD defect D-S3: the enum now covers all seven <c>changefreq</c> values the sitemap protocol
    /// defines. <c>weekly</c> — the most-used value in real sitemaps — could not be expressed at
    /// all before.
    /// </summary>
    /// <remarks>
    /// The three additions are APPENDED, not inserted in spec order, so the underlying integers of
    /// the original four are untouched for anyone who persisted them. That is what makes the
    /// declaration order asserted here load-bearing rather than cosmetic.
    /// </remarks>
    [Fact]
    public void ChangeFreq_CoversEverySpecValue()
    {
        var declared = Enum.GetValues<ChangeFreq>().Select(XmlNameOf).ToArray();

        Assert.Equal(["hourly", "daily", "monthly", "yearly", "always", "weekly", "never"], declared);
    }

    /// <summary>
    /// Second half of D-S3, and PRD defect D-S30: reading back a real-world sitemap containing
    /// <c>weekly</c> used to throw rather than fall back — the gap ran in both directions.
    /// </summary>
    [Fact]
    public void Deserializing_AWeeklyChangeFreq_Succeeds()
    {
        const string xml = """
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>a</loc><changefreq>weekly</changefreq></url></urlset>
            """;

        Assert.Equal(ChangeFreq.Weekly, XmlTestHelpers.Deserialize<UrlSet>(xml).Urls[0].ChangeFreq);
    }

    /// <summary>
    /// First half of PRD defect D-S4. <c>Hourly</c> is still member zero — renumbering would break
    /// every persisted value — but <c>Url.ChangeFreq</c> is a <c>ChangeFreq?</c>, so an unset
    /// property is <see langword="null"/> and no longer silently claims hourly churn.
    /// </summary>
    [Fact]
    public void AnUnsetChangeFreq_IsNullRatherThanHourly()
    {
        Assert.Null(new Url { Loc = "https://example.org/a" }.ChangeFreq);
        Assert.Equal(ChangeFreq.Hourly, default(ChangeFreq));
        Assert.Equal(0, (int)ChangeFreq.Hourly);
    }

    /// <summary>
    /// The underlying numeric values are part of the wire format for anyone who persisted them,
    /// which is exactly why the D-S3 additions were appended: appending is safe, inserting in spec
    /// order is not. The original four ordinals must never move.
    /// </summary>
    [Fact]
    public void MemberOrdinals_ArePinnedBecausePersistedValuesDependOnThem()
    {
        Assert.Equal(0, (int)ChangeFreq.Hourly);
        Assert.Equal(1, (int)ChangeFreq.Daily);
        Assert.Equal(2, (int)ChangeFreq.Monthly);
        Assert.Equal(3, (int)ChangeFreq.Yearly);
        Assert.Equal(4, (int)ChangeFreq.Always);
        Assert.Equal(5, (int)ChangeFreq.Weekly);
        Assert.Equal(6, (int)ChangeFreq.Never);
    }

    /// <summary>
    /// An out-of-range cast is rejected at serialization time rather than emitted as a number,
    /// so a bad value fails loudly instead of producing a silently invalid sitemap.
    /// </summary>
    [Fact]
    public void SerializingAnUndefinedEnumValue_Throws()
    {
        var set = new UrlSet([new Url { Loc = "https://example.org/a", ChangeFreq = (ChangeFreq)99 }]);

        var exception = Assert.Throws<InvalidOperationException>(() => XmlTestHelpers.SerializeToString(set));

        Assert.Contains("99", exception.InnerException!.Message);
    }

    [Theory]
    [InlineData("hourly", ChangeFreq.Hourly)]
    [InlineData("daily", ChangeFreq.Daily)]
    [InlineData("monthly", ChangeFreq.Monthly)]
    [InlineData("yearly", ChangeFreq.Yearly)]
    [InlineData("always", ChangeFreq.Always)]
    [InlineData("weekly", ChangeFreq.Weekly)]
    [InlineData("never", ChangeFreq.Never)]
    public void EveryMember_RoundTrips(string wire, ChangeFreq expected)
    {
        var xml = $"""
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>a</loc><changefreq>{wire}</changefreq></url></urlset>
            """;

        Assert.Equal(expected, XmlTestHelpers.Deserialize<UrlSet>(xml).Urls[0].ChangeFreq);
    }
}
