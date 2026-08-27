using System.Xml.Linq;
using MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

public class VideoSerializationTests
{
    /// <summary>
    /// Videos are only ever reached through <c>UrlSet → Url → Videos</c> in the shipped formatter,
    /// so every structural assertion goes through that path rather than serializing a bare
    /// <c>Video</c>: the namespace a member lands in depends on the mapping it is reached through.
    /// </summary>
    private static XElement SerializeVideo(Video video)
    {
        var document = XmlTestHelpers.SerializeToDocument(
            new UrlSet([new Url { Loc = "https://example.org/a", Videos = { video } }]));

        return document.Root!
            .Element(Ns.Sitemap + "url")!
            .Element(Ns.Video + "video")!;
    }

    [Fact]
    public void Video_AndAllItsChildren_AreInTheVideoNamespace()
    {
        var video = SerializeVideo(new Video
        {
            Title = "T",
            Description = "D",
            ThumbnailLocation = "https://example.org/t.png",
            ContentLocation = "https://example.org/v.mp4",
            PlayerLocation = "https://example.org/p",
            Duration = 42,
            ViewCount = 7,
            Rating = 4.5,
            FamilyFriendly = true,
            Live = false,
            PublicationDate = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc),
            ExpirationDate = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Unspecified),
        });

        Assert.Equal(Ns.Video, video.Name.Namespace);
        Assert.All(video.Elements(), e => Assert.Equal(Ns.Video, e.Name.Namespace));
    }

    /// <summary>
    /// The element order is the declaration order of the properties, which is also what the
    /// video sitemap XSD requires (it is a <c>xs:sequence</c>, not an <c>xs:all</c>).
    /// </summary>
    [Fact]
    public void Video_EmitsItsChildrenInDeclarationOrder()
    {
        var video = SerializeVideo(new Video
        {
            Title = "T",
            Description = "D",
            ThumbnailLocation = "https://example.org/t.png",
            ContentLocation = "https://example.org/v.mp4",
            PlayerLocation = "https://example.org/p",
            Duration = 42,
            ViewCount = 7,
            Rating = 4.5,
            FamilyFriendly = true,
            Live = false,
            PublicationDate = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc),
            ExpirationDate = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Unspecified),
        });

        Assert.Equal(
            [
                "thumbnail_loc", "title", "description", "content_loc", "player_loc", "duration",
                "expiration_date", "rating", "view_count", "publication_date", "family_friendly", "live",
            ],
            video.Elements().Select(e => e.Name.LocalName).ToArray());
    }

    /// <summary>
    /// Every optional member with a <c>ShouldSerialize*</c> companion disappears when unset.
    /// </summary>
    [Theory]
    [InlineData("thumbnail_loc")]
    [InlineData("duration")]
    [InlineData("expiration_date")]
    [InlineData("rating")]
    [InlineData("view_count")]
    [InlineData("publication_date")]
    [InlineData("family_friendly")]
    [InlineData("live")]
    public void Video_UnsetOptionalMembers_AreOmitted(string elementName)
    {
        var video = SerializeVideo(new Video { Title = "T" });

        Assert.Null(video.Element(Ns.Video + elementName));
    }

    /// <summary>
    /// PRD defect D-S28: <c>FamilyFriendly</c> and <c>Live</c> were the two nullable members with no
    /// <c>ShouldSerialize*</c> companion, so EVERY video emitted two <c>xsi:nil="true"</c> elements
    /// — which the video sitemap schema does not allow, and which no other optional member on the
    /// type did.
    /// </summary>
    /// <remarks>
    /// It also dragged the <c>xsi</c> namespace declaration into every response containing a video,
    /// defeating the formatter's <c>ns.Add(string.Empty, string.Empty)</c>. That half is asserted
    /// here too, because the declaration is what a schema validator trips over first.
    /// </remarks>
    [Fact]
    public void Video_UnsetFamilyFriendlyAndLive_AreOmitted()
    {
        var video = SerializeVideo(new Video { Title = "T" });

        Assert.Equal(["title"], video.Elements().Select(e => e.Name.LocalName).ToArray());

        var xml = XmlTestHelpers.SerializeToString(new UrlSet(
        [
            new Url { Loc = "https://example.org/a", Videos = { new Video { Title = "T" } } },
        ]));

        Assert.DoesNotContain("nil=\"true\"", xml);
        Assert.DoesNotContain("XMLSchema-instance", xml);
    }

    /// <summary>The other side of the D-S28 guard: a value that WAS set still reaches the wire.</summary>
    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Video_SetFamilyFriendlyAndLive_AreEmitted(bool value, string expected)
    {
        var video = SerializeVideo(new Video { Title = "T", FamilyFriendly = value, Live = value });

        Assert.Equal(expected, video.Element(Ns.Video + "family_friendly")!.Value);
        Assert.Equal(expected, video.Element(Ns.Video + "live")!.Value);
    }

    [Fact]
    public void Video_NullStringMembers_AreOmitted()
    {
        var video = SerializeVideo(new Video());

        Assert.Null(video.Element(Ns.Video + "title"));
        Assert.Null(video.Element(Ns.Video + "description"));
        Assert.Null(video.Element(Ns.Video + "content_loc"));
        Assert.Null(video.Element(Ns.Video + "player_loc"));
    }

    // ── dates ─────────────────────────────────────────────────────────────────────────────────
    //
    // Unlike Sitemap.LastMod and Url.LastMod, these two carry no DataType, so XmlSerializer
    // writes them as xsd:dateTime with "o"/RoundtripKind semantics: the MACHINE's UTC offset is
    // appended for a Local-kind value. Hard-coding an offset here would pass on a +01:00 dev box
    // and fail on the UTC CI runner — so Utc and Unspecified are used for the fixed expectations,
    // and the one Local test derives its expectation from TimeZoneInfo.

    [Fact]
    public void PublicationDate_UtcKind_IsWrittenWithATrailingZ()
    {
        var video = SerializeVideo(new Video
        {
            PublicationDate = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc),
        });

        Assert.Equal("2024-03-04T05:06:07Z", video.Element(Ns.Video + "publication_date")!.Value);
    }

    [Fact]
    public void PublicationDate_UnspecifiedKind_IsWrittenWithNoOffsetAtAll()
    {
        var video = SerializeVideo(new Video
        {
            PublicationDate = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Unspecified),
        });

        Assert.Equal("2024-03-04T05:06:07", video.Element(Ns.Video + "publication_date")!.Value);
    }

    /// <summary>
    /// The one deliberately time-zone-dependent test: a <see cref="DateTimeKind.Local"/> value
    /// gets the machine's offset appended, so the expectation is derived from
    /// <see cref="TimeZoneInfo.Local"/> rather than written down.
    /// </summary>
    /// <remarks>
    /// Asserted as a parsed offset plus an instant rather than as a literal string, because the
    /// literal form of a zero offset is itself platform-dependent — the CI runner is UTC, a dev
    /// box is not, and pinning either spelling would fail on the other.
    /// </remarks>
    [Fact]
    public void PublicationDate_LocalKind_AppendsTheMachinesUtcOffset()
    {
        var value = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Local);
        var offset = TimeZoneInfo.Local.GetUtcOffset(value);

        var text = SerializeVideo(new Video { PublicationDate = value })
            .Element(Ns.Video + "publication_date")!.Value;

        // "2024-03-04T05:06:07" is 19 characters; anything longer carries a Z or an offset.
        Assert.True(text.Length > 19, $"expected an offset suffix, got '{text}'");

        var parsed = DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(offset, parsed.Offset);
        Assert.Equal(value.ToUniversalTime(), parsed.UtcDateTime);
    }

    [Fact]
    public void ExpirationDate_UtcKind_IsWrittenWithATrailingZ()
    {
        var video = SerializeVideo(new Video
        {
            ExpirationDate = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        });

        Assert.Equal("2025-01-02T03:04:05Z", video.Element(Ns.Video + "expiration_date")!.Value);
    }

    /// <summary>
    /// Unlike <c>lastmod</c>, these keep the time-of-day — pinned because "harmonising" them onto
    /// <c>DataType = "date"</c> would silently drop it.
    /// </summary>
    [Fact]
    public void VideoDates_KeepTheTimeOfDay()
    {
        var video = SerializeVideo(new Video
        {
            PublicationDate = new DateTime(2024, 3, 4, 22, 30, 15, DateTimeKind.Unspecified),
        });

        Assert.Contains("T22:30:15", video.Element(Ns.Video + "publication_date")!.Value);
    }

    // ── numbers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The highest-value culture test in this suite: <c>Rating</c> is a <c>double?</c>, and
    /// <c>nl-BE</c> uses a comma as its decimal separator. XSD <c>double</c> is invariant, so the
    /// serializer must ignore the ambient culture — a regression would emit <c>4,5</c>, which no
    /// crawler can parse.
    /// </summary>
    [Theory]
    [InlineData("nl-BE")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    public void Rating_IsInvariantRegardlessOfTheAmbientCulture(string culture)
    {
        using (XmlTestHelpers.WithCulture(culture))
        {
            var video = SerializeVideo(new Video { Rating = 4.5 });

            Assert.Equal("4.5", video.Element(Ns.Video + "rating")!.Value);
        }
    }

    /// <summary>
    /// Group separators and native digits are the other half of the culture risk: <c>ar-SA</c>
    /// historically rendered digits as Arabic-Indic under NLS, and thousands separators differ
    /// between ICU and NLS.
    /// </summary>
    [Theory]
    [InlineData("nl-BE")]
    [InlineData("ar-SA")]
    public void IntegerMembers_AreInvariantRegardlessOfTheAmbientCulture(string culture)
    {
        using (XmlTestHelpers.WithCulture(culture))
        {
            var video = SerializeVideo(new Video { Duration = 1234, ViewCount = 98765 });

            Assert.Equal("1234", video.Element(Ns.Video + "duration")!.Value);
            Assert.Equal("98765", video.Element(Ns.Video + "view_count")!.Value);
        }
    }

    [Fact]
    public void BooleanMembers_UseLowercaseXsdBooleans()
    {
        var video = SerializeVideo(new Video { FamilyFriendly = true, Live = false });

        Assert.Equal("true", video.Element(Ns.Video + "family_friendly")!.Value);
        Assert.Equal("false", video.Element(Ns.Video + "live")!.Value);
    }

    /// <summary>
    /// <c>Video</c> has <c>[XmlType]</c> but no <c>[XmlRoot]</c>, so used as a document root its
    /// own element lands in the empty namespace while its children stay in the video namespace.
    /// Same class of defect as D-S1 on <c>Url</c>, and equally unreachable through the shipped
    /// formatter.
    /// </summary>
    [Fact]
    public void Video_AsTheDocumentRoot_HasNoNamespaceOnTheRootElement_KnownGap()
    {
        var document = XmlTestHelpers.SerializeToDocument(new Video { Title = "T" });

        Assert.Equal(XNamespace.None, document.Root!.Name.Namespace);
        Assert.Equal(Ns.Video, document.Root.Element(Ns.Video + "title")!.Name.Namespace);
    }

    [Fact]
    public void Video_RoundTrips()
    {
        var original = new UrlSet(
        [
            new Url
            {
                Loc = "https://example.org/a",
                Videos =
                {
                    new Video
                    {
                        Title = "T",
                        Description = "D",
                        Duration = 42,
                        Rating = 4.5,
                        ViewCount = 7,
                        FamilyFriendly = true,
                        Live = false,
                        PublicationDate = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc),
                    },
                },
            },
        ]);

        var restored = XmlTestHelpers.Deserialize<UrlSet>(XmlTestHelpers.SerializeToString(original));

        var video = Assert.Single(restored.Urls[0].Videos);
        Assert.Equal("T", video.Title);
        Assert.Equal(42, video.Duration);
        Assert.Equal(4.5, video.Rating);
        Assert.True(video.FamilyFriendly);
        Assert.False(video.Live);
        Assert.Equal(new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc), video.PublicationDate!.Value.ToUniversalTime());
    }

    [Fact]
    public void Video_MultipleVideosOnOneUrl_AreAllEmitted()
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet(
        [
            new Url
            {
                Loc = "https://example.org/a",
                Videos = { new Video { Title = "1" }, new Video { Title = "2" } },
            },
        ]));

        Assert.Equal(
            ["1", "2"],
            document.Root!.Element(Ns.Sitemap + "url")!
                .Elements(Ns.Video + "video")
                .Select(v => v.Element(Ns.Video + "title")!.Value)
                .ToArray());
    }
}
