using MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

public class SitemapIndexSerializationTests
{
    [Fact]
    public void SitemapIndex_RootIsSitemapindexInTheSitemapNamespace()
    {
        var document = XmlTestHelpers.SerializeToDocument(new SitemapIndex());

        Assert.Equal("sitemapindex", document.Root!.Name.LocalName);
        Assert.Equal(Ns.Sitemap, document.Root.Name.Namespace);
    }

    /// <summary>
    /// A sitemap index references only other sitemaps, so — unlike <c>UrlSet</c> — it declares no
    /// extra prefixes. Pinned because the empty <c>xmlns</c> field looks like an oversight next to
    /// <c>UrlSet</c>'s three-prefix ctor, and "tidying" it would change every index response.
    /// </summary>
    [Fact]
    public void SitemapIndex_DeclaresNoExtraPrefixes()
    {
        var index = new SitemapIndex();
        var document = XmlTestHelpers.SerializeToDocument(index);

        Assert.Equal(0, index.xmlns.Count);
        Assert.Equal(
            [Ns.Sitemap.NamespaceName],
            document.Root!.Attributes().Where(a => a.IsNamespaceDeclaration).Select(a => a.Value).ToArray());
    }

    [Fact]
    public void SitemapIndex_Empty_HasNoChildren()
    {
        var document = XmlTestHelpers.SerializeToDocument(new SitemapIndex());

        Assert.Empty(document.Root!.Elements());
    }

    [Fact]
    public void SitemapIndex_EnumerableCtor_CopiesTheItems()
    {
        var index = new SitemapIndex([new Sitemap { Loc = "a" }, new Sitemap { Loc = "b" }]);

        Assert.Equal(2, index.Sitemaps.Count);
    }

    [Fact]
    public void SitemapIndex_EnumerableCtor_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SitemapIndex(null!));
    }

    [Fact]
    public void SitemapIndex_TwoInstances_DoNotShareTheNamespaceDeclarations()
    {
        Assert.NotSame(new SitemapIndex().xmlns, new SitemapIndex().xmlns);
    }

    [Fact]
    public void Sitemap_ChildrenAreAllInTheSitemapNamespace()
    {
        var document = XmlTestHelpers.SerializeToDocument(new SitemapIndex(
        [
            new Sitemap { Loc = "https://example.org/sitemap-1.xml", LastMod = new DateTime(2024, 3, 4) },
        ]));

        var sitemap = Assert.Single(document.Root!.Elements(Ns.Sitemap + "sitemap"));
        Assert.Equal("https://example.org/sitemap-1.xml", sitemap.Element(Ns.Sitemap + "loc")!.Value);
        Assert.Equal("2024-03-04", sitemap.Element(Ns.Sitemap + "lastmod")!.Value);
    }

    /// <summary>Same D-S4 shape as <c>Url.LastMod</c> — non-nullable, so always emitted.</summary>
    [Fact]
    public void Sitemap_LastModUnset_SerializesAsYearOne_KnownBug()
    {
        var xml = XmlTestHelpers.SerializeToString(new SitemapIndex([new Sitemap { Loc = "https://example.org/s.xml" }]));

        Assert.Contains("<lastmod>0001-01-01</lastmod>", xml);
    }

    /// <summary>
    /// <c>DataType = "date"</c> again: date-only, no offset, identical for every kind. See the
    /// matching test on <c>Url</c> for why this must never assert an offset.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Sitemap_LastMod_IsDateOnlyAndOffsetFree(DateTimeKind kind)
    {
        var xml = XmlTestHelpers.SerializeToString(new SitemapIndex(
        [
            new Sitemap { Loc = "https://example.org/s.xml", LastMod = new DateTime(2024, 12, 31, 23, 59, 0, kind) },
        ]));

        Assert.Contains("<lastmod>2024-12-31</lastmod>", xml);
    }

    [Fact]
    public void Sitemap_LastMod_UnderADutchCulture_StillUsesTheInvariantIsoForm()
    {
        using (XmlTestHelpers.WithCulture("nl-BE"))
        {
            var xml = XmlTestHelpers.SerializeToString(new SitemapIndex(
            [
                new Sitemap { Loc = "https://example.org/s.xml", LastMod = new DateTime(2024, 3, 4) },
            ]));

            Assert.Contains("<lastmod>2024-03-04</lastmod>", xml);
        }
    }

    [Fact]
    public void SitemapIndex_RoundTrips()
    {
        var original = new SitemapIndex(
        [
            new Sitemap { Loc = "https://example.org/sitemap-1.xml", LastMod = new DateTime(2024, 3, 4) },
            new Sitemap { Loc = "https://example.org/sitemap-2.xml", LastMod = new DateTime(2024, 5, 6) },
        ]);

        var restored = XmlTestHelpers.Deserialize<SitemapIndex>(XmlTestHelpers.SerializeToString(original));

        Assert.Equal(2, restored.Sitemaps.Count);
        Assert.Equal("https://example.org/sitemap-2.xml", restored.Sitemaps[1].Loc);
        Assert.Equal(new DateTime(2024, 5, 6), restored.Sitemaps[1].LastMod);
    }

    [Fact]
    public void SitemapIndex_MultipleSitemaps_KeepTheirOrder()
    {
        var document = XmlTestHelpers.SerializeToDocument(new SitemapIndex(
        [
            new Sitemap { Loc = "1" },
            new Sitemap { Loc = "2" },
            new Sitemap { Loc = "3" },
        ]));

        Assert.Equal(
            ["1", "2", "3"],
            document.Root!.Elements(Ns.Sitemap + "sitemap").Select(s => s.Element(Ns.Sitemap + "loc")!.Value).ToArray());
    }
}
