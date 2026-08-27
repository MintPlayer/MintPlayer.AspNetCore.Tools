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
    public void EveryMember_CarriesItsLowercaseSpecValue(ChangeFreq value, string expected)
    {
        Assert.Equal(expected, XmlNameOf(value));
    }

    [Theory]
    [InlineData(ChangeFreq.Hourly, "hourly")]
    [InlineData(ChangeFreq.Daily, "daily")]
    [InlineData(ChangeFreq.Monthly, "monthly")]
    [InlineData(ChangeFreq.Yearly, "yearly")]
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
    /// Pins PRD defect D-S3: the sitemap protocol defines seven values and the enum ships four.
    /// <c>weekly</c> is the most-used value in real sitemaps and cannot be expressed at all.
    /// </summary>
    [Fact]
    public void ChangeFreq_IsMissingTheAlwaysWeeklyAndNeverSpecValues_KnownGap()
    {
        var declared = Enum.GetValues<ChangeFreq>().Select(XmlNameOf).ToArray();

        Assert.Equal(["hourly", "daily", "monthly", "yearly"], declared);
        Assert.DoesNotContain("weekly", declared);
        Assert.DoesNotContain("always", declared);
        Assert.DoesNotContain("never", declared);
    }

    /// <summary>
    /// Second half of D-S3 — the practical consequence: an existing sitemap using <c>weekly</c>
    /// cannot be read back at all, it throws rather than falling back.
    /// </summary>
    [Fact]
    public void Deserializing_AWeeklyChangeFreq_Throws_KnownGap()
    {
        const string xml = """
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>a</loc><changefreq>weekly</changefreq></url></urlset>
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => XmlTestHelpers.Deserialize<UrlSet>(xml));

        Assert.Contains("weekly", exception.InnerException!.Message);
    }

    /// <summary>
    /// Pins the first half of PRD defect D-S4: <c>Hourly</c> is member zero, so it is what every
    /// unset <c>Url.ChangeFreq</c> reports. The fix is a nullable property or a
    /// <c>ShouldSerializeChangeFreq</c>, not a reordering — reordering silently changes the
    /// meaning of persisted ints.
    /// </summary>
    [Fact]
    public void DefaultChangeFreq_IsHourly_KnownBug()
    {
        Assert.Equal(ChangeFreq.Hourly, default(ChangeFreq));
        Assert.Equal(0, (int)ChangeFreq.Hourly);
    }

    /// <summary>
    /// The underlying numeric values are part of the wire format for anyone who persisted them,
    /// which is exactly why D-S3 is marked <c>[decision]</c>: appending is safe, inserting in spec
    /// order is not.
    /// </summary>
    [Fact]
    public void MemberOrdinals_ArePinnedBecausePersistedValuesDependOnThem()
    {
        Assert.Equal(0, (int)ChangeFreq.Hourly);
        Assert.Equal(1, (int)ChangeFreq.Daily);
        Assert.Equal(2, (int)ChangeFreq.Monthly);
        Assert.Equal(3, (int)ChangeFreq.Yearly);
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
    public void EveryMember_RoundTrips(string wire, ChangeFreq expected)
    {
        var xml = $"""
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>a</loc><changefreq>{wire}</changefreq></url></urlset>
            """;

        Assert.Equal(expected, XmlTestHelpers.Deserialize<UrlSet>(xml).Urls[0].ChangeFreq);
    }
}
