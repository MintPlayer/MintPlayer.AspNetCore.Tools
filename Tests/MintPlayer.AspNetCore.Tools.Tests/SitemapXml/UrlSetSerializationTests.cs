using System.Xml.Linq;
using MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;
using MintPlayer.AspNetCore.SitemapXml.Abstractions.Enums;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

public class UrlSetSerializationTests
{
    private static Url SampleUrl() => new()
    {
        Loc = "https://example.org/a",
        LastMod = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc),
        ChangeFreq = ChangeFreq.Daily,
    };

    [Fact]
    public void UrlSet_RootIsUrlsetInTheSitemapNamespace()
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet());

        Assert.Equal("urlset", document.Root!.Name.LocalName);
        Assert.Equal(Ns.Sitemap, document.Root.Name.Namespace);
    }

    /// <summary>
    /// The three prefixes the ctor pre-declares on the root, so the per-URL children never have
    /// to redeclare them.
    /// </summary>
    [Fact]
    public void UrlSet_DeclaresXhtmlImageAndVideoPrefixesOnTheRoot()
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet());

        var declarations = document.Root!.Attributes()
            .Where(a => a.IsNamespaceDeclaration)
            .ToDictionary(a => a.Name.LocalName, a => a.Value);

        Assert.Equal(Ns.Xhtml.NamespaceName, declarations["xhtml"]);
        Assert.Equal(Ns.Image.NamespaceName, declarations["image"]);
        Assert.Equal(Ns.Video.NamespaceName, declarations["video"]);
    }

    [Fact]
    public void UrlSet_Empty_SerializesToASelfClosingRootWithNoUrlChildren()
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet());

        Assert.Empty(document.Root!.Elements());
    }

    [Fact]
    public void UrlSet_EnumerableCtor_CopiesTheItems()
    {
        var set = new UrlSet([SampleUrl(), SampleUrl()]);

        Assert.Equal(2, set.Urls.Count);
    }

    /// <summary>The ctor delegates straight to <c>List{T}.AddRange</c>, which guards for null.</summary>
    [Fact]
    public void UrlSet_EnumerableCtor_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new UrlSet(null!));
    }

    /// <summary>
    /// The pre-declared prefixes live on an instance field, so two UrlSets must not share the
    /// <see cref="System.Xml.Serialization.XmlSerializerNamespaces"/> — otherwise one response
    /// could accumulate another's declarations.
    /// </summary>
    [Fact]
    public void UrlSet_TwoInstances_DoNotShareTheNamespaceDeclarations()
    {
        var first = new UrlSet();
        var second = new UrlSet();

        Assert.NotSame(first.xmlns, second.xmlns);
        Assert.Equal(3, first.xmlns.Count);
    }

    [Fact]
    public void Url_IsEmittedAsUrlInTheSitemapNamespace()
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet([SampleUrl()]));

        var url = Assert.Single(document.Root!.Elements());
        Assert.Equal("url", url.Name.LocalName);
        Assert.Equal(Ns.Sitemap, url.Name.Namespace);
    }

    /// <summary>
    /// Contradicts the D-S1 prediction, and is the reason this is asserted rather than assumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRD defect D-S1 predicts <c>&lt;url xmlns="…/0.9"&gt;&lt;loc xmlns=""&gt;</c>, because
    /// <c>Data/Url.cs</c> is <c>[XmlRoot("url")]</c> with no namespace and its <c>loc</c> /
    /// <c>lastmod</c> / <c>changefreq</c> declare none either. That does <b>not</b> happen when
    /// <c>Url</c> is reached as a CHILD of <c>UrlSet</c>: an <c>[XmlElement]</c> with no explicit
    /// namespace inherits the namespace of the mapping it is reached through, and
    /// <c>UrlSet.Urls</c> declares the sitemap namespace. So the shipped output is valid.
    /// </para>
    /// <para>
    /// The defect is real but latent — see
    /// <see cref="Url_SerializedAsTheDocumentRoot_PutsChildrenInTheEmptyNamespace_KnownBug"/>.
    /// Keeping both assertions is what stops a "fix" for D-S1 from being applied blind.
    /// </para>
    /// </remarks>
    [Fact]
    public void UrlChildren_NestedInAUrlSet_AreInTheSitemapNamespace()
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet([SampleUrl()]));
        var url = document.Root!.Elements().Single();

        Assert.All(
            new[] { "loc", "lastmod", "changefreq" },
            name => Assert.Equal(Ns.Sitemap, url.Element(Ns.Sitemap + name)!.Name.Namespace));
    }

    /// <summary>
    /// Pins PRD defect D-S1 at the one place it actually bites: <c>Url</c> used as a document
    /// root, where <c>[XmlRoot("url")]</c>'s missing namespace applies and every child lands in
    /// the empty namespace. Not reachable through the shipped formatter — <c>CanWriteType</c>
    /// admits only <c>UrlSet</c> and <c>SitemapIndex</c> — but reachable by any consumer holding
    /// an <c>XmlSerializer(typeof(Url))</c>.
    /// </summary>
    [Fact]
    public void Url_SerializedAsTheDocumentRoot_PutsChildrenInTheEmptyNamespace_KnownBug()
    {
        var document = XmlTestHelpers.SerializeToDocument(SampleUrl());

        Assert.Equal(XNamespace.None, document.Root!.Name.Namespace);
        Assert.All(
            new[] { "loc", "lastmod", "changefreq" },
            name => Assert.NotNull(document.Root.Element(XName.Get(name))));
        Assert.Null(document.Root.Element(Ns.Sitemap + "loc"));
    }

    /// <summary>
    /// <c>Sitemap.cs</c> declares the namespace on every element and therefore does not have
    /// D-S1 as a root either. This asymmetry with <c>Url.cs</c> is what hid the defect.
    /// </summary>
    [Fact]
    public void Sitemap_SerializedAsTheDocumentRoot_KeepsChildrenInTheSitemapNamespace()
    {
        var document = XmlTestHelpers.SerializeToDocument(new Sitemap { Loc = "https://example.org/s.xml" });

        Assert.Equal(Ns.Sitemap, document.Root!.Name.Namespace);
        Assert.NotNull(document.Root.Element(Ns.Sitemap + "loc"));
    }

    [Fact]
    public void Loc_IsWrittenAsTheRawUrl()
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet([SampleUrl()]));

        Assert.Equal(
            "https://example.org/a",
            document.Root!.Element(Ns.Sitemap + "url")!.Element(Ns.Sitemap + "loc")!.Value);
    }

    /// <summary>
    /// Query separators and angle brackets must survive as XML entities — a sitemap of
    /// parameterised URLs is otherwise unparseable. The lexical form is the subject here, so this
    /// one asserts on the raw text.
    /// </summary>
    [Fact]
    public void Loc_XmlSignificantCharacters_AreEscaped()
    {
        var xml = XmlTestHelpers.SerializeToString(
            new UrlSet([new Url { Loc = "https://example.org/?a=1&b=2<3>" }]));

        Assert.Contains("<loc>https://example.org/?a=1&amp;b=2&lt;3&gt;</loc>", xml);
    }

    /// <summary>
    /// <c>loc</c> is REQUIRED by the sitemap protocol, but a null <c>Loc</c> silently produces a
    /// <c>&lt;url&gt;</c> with no <c>&lt;loc&gt;</c> at all rather than failing.
    /// </summary>
    /// <remarks>
    /// Not in the PRD register — reported as a new finding by the M5 milestone. Any
    /// <c>string</c> member left null is skipped by <c>XmlSerializer</c>, so the invalid document
    /// is produced with no exception and no warning.
    /// </remarks>
    [Fact]
    public void Loc_Null_EmitsAUrlWithNoLocAtAll_KnownGap()
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet([new Url()]));

        var url = document.Root!.Elements().Single();
        Assert.Null(url.Element(Ns.Sitemap + "loc"));
        Assert.NotNull(url.Element(Ns.Sitemap + "lastmod"));
    }

    /// <summary>
    /// Pins PRD defect D-S4: <c>LastMod</c> is a non-nullable <see cref="DateTime"/> with no
    /// <c>ShouldSerializeLastMod</c>, so a URL nobody set a date on claims it changed in year 1.
    /// </summary>
    [Fact]
    public void LastMod_Unset_SerializesAsYearOne_KnownBug()
    {
        var xml = XmlTestHelpers.SerializeToString(new UrlSet([new Url { Loc = "https://example.org/a" }]));

        Assert.Contains("<lastmod>0001-01-01</lastmod>", xml);
    }

    /// <summary>
    /// Second half of D-S4: <c>default(ChangeFreq)</c> is <c>Hourly</c>, so every URL whose
    /// author never touched the property tells crawlers to come back every hour.
    /// </summary>
    [Fact]
    public void ChangeFreq_Unset_SerializesAsHourly_KnownBug()
    {
        var xml = XmlTestHelpers.SerializeToString(new UrlSet([new Url { Loc = "https://example.org/a" }]));

        Assert.Contains("<changefreq>hourly</changefreq>", xml);
    }

    /// <summary>
    /// <c>lastmod</c> carries <c>DataType = "date"</c>, so the time-of-day is dropped and no UTC
    /// offset is ever appended. That is what makes it safe to assert as a literal in CI — the
    /// output is identical for every <see cref="DateTimeKind"/> and every machine time zone.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void LastMod_IsDateOnlyAndOffsetFree_ForEveryDateTimeKind(DateTimeKind kind)
    {
        var xml = XmlTestHelpers.SerializeToString(new UrlSet(
        [
            new Url { Loc = "https://example.org/a", LastMod = new DateTime(2024, 3, 4, 22, 30, 0, kind) },
        ]));

        Assert.Contains("<lastmod>2024-03-04</lastmod>", xml);
    }

    /// <summary>
    /// XSD <c>date</c> is culture-invariant by contract. Asserted under a culture whose native
    /// date format is <c>4-3-2024</c>, because ICU (Linux CI) and NLS (Windows dev) disagree on
    /// the details of that format and a regression here would only show up on one of them.
    /// </summary>
    [Fact]
    public void LastMod_UnderADutchCulture_StillUsesTheInvariantIsoForm()
    {
        using (XmlTestHelpers.WithCulture("nl-BE"))
        {
            var xml = XmlTestHelpers.SerializeToString(new UrlSet(
            [
                new Url { Loc = "https://example.org/a", LastMod = new DateTime(2024, 3, 4) },
            ]));

            Assert.Contains("<lastmod>2024-03-04</lastmod>", xml);
        }
    }

    [Fact]
    public void UrlSet_RoundTrips()
    {
        var original = new UrlSet(
        [
            new Url
            {
                Loc = "https://example.org/a",
                LastMod = new DateTime(2024, 3, 4),
                ChangeFreq = ChangeFreq.Monthly,
                Links = { new Link { Rel = "alternate", Href = "https://example.org/nl/a", HrefLang = "nl" } },
                Images = { new Image { Location = "https://example.org/i.png" } },
                Videos = { new Video { Title = "T" } },
            },
        ]);

        var restored = XmlTestHelpers.Deserialize<UrlSet>(XmlTestHelpers.SerializeToString(original));

        var url = Assert.Single(restored.Urls);
        Assert.Equal("https://example.org/a", url.Loc);
        Assert.Equal(new DateTime(2024, 3, 4), url.LastMod);
        Assert.Equal(ChangeFreq.Monthly, url.ChangeFreq);
        Assert.Single(url.Links);
        Assert.Single(url.Images);
        Assert.Single(url.Videos);
    }

    /// <summary>
    /// The time-of-day loss from <c>DataType = "date"</c> is asymmetric: it survives
    /// serialization only as a date, so a round trip is not value-preserving.
    /// </summary>
    [Fact]
    public void LastMod_RoundTrip_LosesTheTimeOfDay()
    {
        var original = new UrlSet(
        [
            new Url { Loc = "https://example.org/a", LastMod = new DateTime(2024, 3, 4, 22, 30, 0, DateTimeKind.Utc) },
        ]);

        var restored = XmlTestHelpers.Deserialize<UrlSet>(XmlTestHelpers.SerializeToString(original));

        Assert.Equal(new DateTime(2024, 3, 4, 0, 0, 0), restored.Urls[0].LastMod);
    }

    [Fact]
    public void UrlSet_MultipleUrls_KeepTheirOrder()
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet(
        [
            new Url { Loc = "https://example.org/1" },
            new Url { Loc = "https://example.org/2" },
            new Url { Loc = "https://example.org/3" },
        ]));

        Assert.Equal(
            ["https://example.org/1", "https://example.org/2", "https://example.org/3"],
            document.Root!.Elements(Ns.Sitemap + "url").Select(u => u.Element(Ns.Sitemap + "loc")!.Value));
    }

    /// <summary>A null collection is skipped rather than throwing, which keeps the root valid.</summary>
    [Fact]
    public void UrlSet_NullUrlsList_SerializesToAnEmptyRoot()
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet { Urls = null! });

        Assert.Empty(document.Root!.Elements());
    }
}
