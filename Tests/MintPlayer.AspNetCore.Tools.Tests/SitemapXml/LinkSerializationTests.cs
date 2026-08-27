using System.Xml.Linq;
using MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

public class LinkSerializationTests
{
    private static XElement SerializeLink(Link link)
    {
        var document = XmlTestHelpers.SerializeToDocument(
            new UrlSet([new Url { Loc = "https://example.org/a", Links = { link } }]));

        return document.Root!
            .Element(Ns.Sitemap + "url")!
            .Element(Ns.Xhtml + "link")!;
    }

    /// <summary>
    /// <c>Link</c> carries no <c>[XmlType]</c> of its own; both its element name and its namespace
    /// come from <c>Url.Links</c>'s <c>[XmlElement("link", Namespace = xhtml)]</c>. Google's
    /// hreflang annotation is only recognised in the xhtml namespace, so this is the whole point
    /// of the type.
    /// </summary>
    [Fact]
    public void Link_IsEmittedAsLinkInTheXhtmlNamespace()
    {
        var link = SerializeLink(new Link { Rel = "alternate", Href = "https://example.org/nl/a", HrefLang = "nl" });

        Assert.Equal("link", link.Name.LocalName);
        Assert.Equal(Ns.Xhtml, link.Name.Namespace);
    }

    /// <summary>
    /// The three members are <c>[XmlAttribute]</c>, not elements — an unprefixed XML attribute is
    /// in no namespace even when its element is namespaced, which is what the spec wants.
    /// </summary>
    [Fact]
    public void Link_MembersAreUnprefixedAttributes()
    {
        var link = SerializeLink(new Link { Rel = "alternate", Href = "https://example.org/nl/a", HrefLang = "nl" });

        Assert.Empty(link.Elements());
        Assert.Equal("alternate", link.Attribute("rel")!.Value);
        Assert.Equal("https://example.org/nl/a", link.Attribute("href")!.Value);
        Assert.Equal("nl", link.Attribute("hreflang")!.Value);
        Assert.All(
            link.Attributes().Where(a => !a.IsNamespaceDeclaration),
            a => Assert.Equal(XNamespace.None, a.Name.Namespace));
    }

    /// <summary>The attribute name is lowercase <c>hreflang</c>, not <c>hrefLang</c>.</summary>
    [Fact]
    public void HrefLang_AttributeNameIsAllLowercase()
    {
        var link = SerializeLink(new Link { HrefLang = "nl-BE" });

        Assert.Contains("hreflang", link.Attributes().Select(a => a.Name.LocalName));
        Assert.DoesNotContain("hrefLang", link.Attributes().Select(a => a.Name.LocalName));
    }

    [Fact]
    public void Link_UnsetAttributes_AreOmitted()
    {
        var link = SerializeLink(new Link { Rel = "alternate" });

        Assert.Equal(["rel"], link.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name.LocalName).ToArray());
    }

    /// <summary>
    /// Attribute values need a different escape set from element text — the quote character must
    /// become <c>&amp;quot;</c> here, where in element text it may stay literal. Lexical subject,
    /// so a raw assertion.
    /// </summary>
    [Fact]
    public void Href_XmlSignificantCharacters_AreEscapedForAnAttribute()
    {
        var xml = XmlTestHelpers.SerializeToString(new UrlSet(
        [
            new Url
            {
                Loc = "https://example.org/a",
                Links = { new Link { Rel = "alternate", Href = "https://example.org/?a=1&b=\"2\"" } },
            },
        ]));

        Assert.Contains("href=\"https://example.org/?a=1&amp;b=&quot;2&quot;\"", xml);
    }

    /// <summary>
    /// <c>Link</c> has neither <c>[XmlType]</c> nor <c>[XmlRoot]</c>, so as a document root it
    /// serializes under its CLR name — <c>&lt;Link /&gt;</c>, capital L, no namespace. Latent, in
    /// the same family as D-S1; the shipped formatter never serializes a bare <c>Link</c>.
    /// </summary>
    [Fact]
    public void Link_AsTheDocumentRoot_UsesTheClrTypeNameAndNoNamespace_KnownGap()
    {
        var document = XmlTestHelpers.SerializeToDocument(new Link { Rel = "alternate" });

        Assert.Equal("Link", document.Root!.Name.LocalName);
        Assert.Equal(XNamespace.None, document.Root.Name.Namespace);
    }

    [Fact]
    public void Link_RoundTrips()
    {
        var original = new UrlSet(
        [
            new Url
            {
                Loc = "https://example.org/a",
                Links =
                {
                    new Link { Rel = "alternate", Href = "https://example.org/nl/a", HrefLang = "nl" },
                    new Link { Rel = "alternate", Href = "https://example.org/fr/a", HrefLang = "fr" },
                },
            },
        ]);

        var restored = XmlTestHelpers.Deserialize<UrlSet>(XmlTestHelpers.SerializeToString(original));

        Assert.Equal(2, restored.Urls[0].Links.Count);
        Assert.Equal("fr", restored.Urls[0].Links[1].HrefLang);
        Assert.Equal("alternate", restored.Urls[0].Links[1].Rel);
    }

    /// <summary>
    /// The xhtml prefix is declared once on the root by <c>UrlSet</c>'s ctor, so a URL with many
    /// alternates does not redeclare it per link.
    /// </summary>
    [Fact]
    public void Link_UsesTheRootLevelXhtmlPrefixDeclaration()
    {
        var xml = XmlTestHelpers.SerializeToString(new UrlSet(
        [
            new Url
            {
                Loc = "https://example.org/a",
                Links = { new Link { HrefLang = "nl" }, new Link { HrefLang = "fr" } },
            },
        ]));

        Assert.Equal(1, xml.Split("xmlns:xhtml=").Length - 1);
        Assert.Equal(2, xml.Split("<xhtml:link").Length - 1);
    }
}
