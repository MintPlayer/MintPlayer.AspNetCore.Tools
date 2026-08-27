using System.Xml.Linq;
using MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

public class ImageSerializationTests
{
    private static XElement SerializeImage(Image image)
    {
        var document = XmlTestHelpers.SerializeToDocument(
            new UrlSet([new Url { Loc = "https://example.org/a", Images = { image } }]));

        return document.Root!
            .Element(Ns.Sitemap + "url")!
            .Element(Ns.Image + "image")!;
    }

    [Fact]
    public void Image_AndAllItsChildren_AreInTheImageNamespace()
    {
        var image = SerializeImage(new Image
        {
            Location = "https://example.org/i.png",
            Caption = "c",
            GeoLocation = "Limerick, Ireland",
            Title = "t",
            License = "https://example.org/license",
        });

        Assert.Equal(Ns.Image, image.Name.Namespace);
        Assert.All(image.Elements(), e => Assert.Equal(Ns.Image, e.Name.Namespace));
    }

    [Fact]
    public void Image_EmitsItsChildrenInDeclarationOrder()
    {
        var image = SerializeImage(new Image
        {
            Location = "https://example.org/i.png",
            Caption = "c",
            GeoLocation = "g",
            Title = "t",
            License = "l",
        });

        Assert.Equal(
            ["loc", "caption", "geo_location", "title", "license"],
            image.Elements().Select(e => e.Name.LocalName).ToArray());
    }

    /// <summary>
    /// <c>Image</c> has no <c>ShouldSerialize*</c> companions at all, but every member is a
    /// <c>string</c>, so nulls are skipped anyway — unlike <c>Video</c>'s nullable value types.
    /// </summary>
    [Fact]
    public void Image_UnsetMembers_AreOmitted()
    {
        var image = SerializeImage(new Image { Location = "https://example.org/i.png" });

        Assert.Equal(["loc"], image.Elements().Select(e => e.Name.LocalName).ToArray());
    }

    /// <summary>
    /// The image sitemap requires <c>image:loc</c>, but a null <c>Location</c> produces an empty
    /// <c>image:image</c> element instead of an error. Same class as the null-<c>loc</c> gap on
    /// <c>Url</c>; not in the PRD register.
    /// </summary>
    [Fact]
    public void Image_NullLocation_EmitsAnEmptyImageElement_KnownGap()
    {
        var image = SerializeImage(new Image());

        Assert.Empty(image.Elements());
    }

    /// <summary>
    /// Captions are free text, so the escaping is load-bearing; the lexical form is the subject.
    /// </summary>
    [Fact]
    public void Caption_XmlSignificantCharacters_AreEscaped()
    {
        var xml = XmlTestHelpers.SerializeToString(new UrlSet(
        [
            new Url
            {
                Loc = "https://example.org/a",
                Images = { new Image { Location = "https://example.org/i.png", Caption = "a & b < c > d" } },
            },
        ]));

        Assert.Contains("<image:caption>a &amp; b &lt; c &gt; d</image:caption>", xml);
    }

    /// <summary>
    /// Non-ASCII text must survive as itself rather than as a numeric entity — the writer is
    /// configured for UTF-8.
    /// </summary>
    [Fact]
    public void Caption_NonAsciiText_SurvivesUnescaped()
    {
        var image = SerializeImage(new Image
        {
            Location = "https://example.org/i.png",
            Caption = "Café — Ø 日本語",
        });

        Assert.Equal("Café — Ø 日本語", image.Element(Ns.Image + "caption")!.Value);
    }

    /// <summary>Same latent root-namespace gap as <c>Video</c>: <c>[XmlType]</c>, no <c>[XmlRoot]</c>.</summary>
    [Fact]
    public void Image_AsTheDocumentRoot_HasNoNamespaceOnTheRootElement_KnownGap()
    {
        var document = XmlTestHelpers.SerializeToDocument(new Image { Location = "https://example.org/i.png" });

        Assert.Equal(XNamespace.None, document.Root!.Name.Namespace);
        Assert.Equal(Ns.Image, document.Root.Element(Ns.Image + "loc")!.Name.Namespace);
    }

    [Fact]
    public void Image_RoundTrips()
    {
        var original = new UrlSet(
        [
            new Url
            {
                Loc = "https://example.org/a",
                Images =
                {
                    new Image
                    {
                        Location = "https://example.org/i.png",
                        Caption = "c",
                        GeoLocation = "g",
                        Title = "t",
                        License = "l",
                    },
                },
            },
        ]);

        var restored = XmlTestHelpers.Deserialize<UrlSet>(XmlTestHelpers.SerializeToString(original));

        var image = Assert.Single(restored.Urls[0].Images);
        Assert.Equal("https://example.org/i.png", image.Location);
        Assert.Equal("c", image.Caption);
        Assert.Equal("g", image.GeoLocation);
        Assert.Equal("t", image.Title);
        Assert.Equal("l", image.License);
    }

    [Fact]
    public void Image_MultipleImagesOnOneUrl_AreAllEmitted()
    {
        var document = XmlTestHelpers.SerializeToDocument(new UrlSet(
        [
            new Url
            {
                Loc = "https://example.org/a",
                Images = { new Image { Location = "1" }, new Image { Location = "2" } },
            },
        ]));

        Assert.Equal(
            ["1", "2"],
            document.Root!.Element(Ns.Sitemap + "url")!
                .Elements(Ns.Image + "image")
                .Select(i => i.Element(Ns.Image + "loc")!.Value)
                .ToArray());
    }
}
