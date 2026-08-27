using System.Xml.Linq;
using MintPlayer.AspNetCore.OpenSearch.Data;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

public class OpenSearchDescriptionSerializationTests
{
    private static OpenSearchDescription Sample() => new()
    {
        ShortName = "MintPlayer",
        Description = "Search MintPlayer",
        InputEncoding = "UTF-8",
        Contact = "support@example.com",
        SearchForm = "https://example.com/",
        Image = new Image { Width = 16, Height = 16, Type = "image/png", Url = "https://example.com/favicon.png" },
        Urls =
        [
            new Url { Type = "text/html", Method = "GET", Template = "https://example.com/search?q={searchTerms}" },
            new Url { Type = "application/opensearchdescription+xml", Relation = "self", Template = "https://example.com/opensearch.xml" },
        ],
    };

    [Fact]
    public void Serialize_RootIsOpenSearchDescriptionInA9Namespace()
    {
        var document = OpenSearchTestHost.Serialize(Sample());

        Assert.Equal("OpenSearchDescription", document.Root!.Name.LocalName);
        Assert.Equal(OpenSearchTestHost.A9, document.Root.Name.Namespace);
    }

    [Theory]
    [InlineData("ShortName")]
    [InlineData("Description")]
    [InlineData("InputEncoding")]
    [InlineData("Contact")]
    [InlineData("Image")]
    [InlineData("Url")]
    [InlineData("SearchForm")]
    public void Serialize_ChildElement_IsInA9Namespace(string localName)
    {
        var document = OpenSearchTestHost.Serialize(Sample());

        var element = document.Root!.Element(OpenSearchTestHost.A9 + localName);

        Assert.NotNull(element);
    }

    [Fact]
    public void Serialize_UrlList_EmitsOneUrlElementPerEntry()
    {
        var document = OpenSearchTestHost.Serialize(Sample());

        var urls = document.Root!.Elements(OpenSearchTestHost.A9 + "Url").ToList();

        Assert.Equal(2, urls.Count);
        Assert.Equal("text/html", (string?)urls[0].Attribute("type"));
        Assert.Equal("GET", (string?)urls[0].Attribute("method"));
        Assert.Equal("https://example.com/search?q={searchTerms}", (string?)urls[0].Attribute("template"));
        Assert.Equal("self", (string?)urls[1].Attribute("rel"));
    }

    /// <summary>
    /// Url's attributes are unqualified — attributes do not inherit the element's default
    /// namespace, so this is correct and must stay that way.
    /// </summary>
    [Fact]
    public void Serialize_UrlAttributes_AreUnqualified()
    {
        var document = OpenSearchTestHost.Serialize(Sample());

        var url = document.Root!.Element(OpenSearchTestHost.A9 + "Url")!;

        Assert.All(url.Attributes(), a => Assert.Equal(XNamespace.None, a.Name.Namespace));
    }

    [Fact]
    public void Serialize_Image_UrlIsTextContentAndSizeIsAttributes()
    {
        var document = OpenSearchTestHost.Serialize(Sample());

        var image = document.Root!.Element(OpenSearchTestHost.A9 + "Image")!;

        Assert.Equal("https://example.com/favicon.png", image.Value);
        Assert.Equal("16", (string?)image.Attribute("width"));
        Assert.Equal("16", (string?)image.Attribute("height"));
        Assert.Equal("image/png", (string?)image.Attribute("type"));
    }

    /// <summary>
    /// <c>int</c> attributes must serialize as ASCII digits regardless of the ambient culture.
    /// </summary>
    /// <remarks>
    /// fa-IR is the classic trap: its native digit shapes would make <c>width="۱۶"</c>, which no
    /// consumer can parse. XmlSerializer routes through XmlConvert, so this passes today — the
    /// test exists to keep it that way if anyone hand-rolls the serialization.
    /// </remarks>
    [Fact]
    public void Serialize_UnderNativeDigitsCulture_ImageSizeIsAsciiDigits()
    {
        OpenSearchTestHost.WithCulture("fa-IR", () =>
        {
            var document = OpenSearchTestHost.Serialize(new OpenSearchDescription
            {
                Image = new Image { Width = 1234, Height = 16, Type = "image/png", Url = "https://example.com/i.png" },
            });

            var image = document.Root!.Element(OpenSearchTestHost.A9 + "Image")!;

            Assert.Equal("1234", (string?)image.Attribute("width"));
            Assert.Equal("16", (string?)image.Attribute("height"));
        });
    }

    /// <summary>
    /// Null string properties are simply omitted, which is why a missing <c>Contact</c> costs
    /// nothing but a missing <c>ShortName</c> would silently produce an incomplete description.
    /// </summary>
    [Fact]
    public void Serialize_NullStringProperties_AreOmitted()
    {
        var document = OpenSearchTestHost.Serialize(new OpenSearchDescription { ShortName = "X" });

        Assert.Null(document.Root!.Element(OpenSearchTestHost.A9 + "Contact"));
        Assert.Null(document.Root.Element(OpenSearchTestHost.A9 + "Description"));
        Assert.Null(document.Root.Element(OpenSearchTestHost.A9 + "Image"));
        Assert.Null(document.Root.Element(OpenSearchTestHost.A9 + "Url"));
        Assert.NotNull(document.Root.Element(OpenSearchTestHost.A9 + "ShortName"));
    }

    /// <summary>
    /// D-S15 does <b>not</b> reproduce: <c>SearchForm</c> lands in the a9 namespace like every
    /// sibling, despite its <c>[XmlElement("SearchForm")]</c> omitting <c>Namespace</c>.
    /// </summary>
    /// <remarks>
    /// XmlSerializer resolves an unqualified member to the namespace of the <i>containing type's</i>
    /// mapping, and <c>OpenSearchDescription</c> carries
    /// <c>[XmlRoot(Namespace = "http://a9.com/-/spec/opensearch/1.1/")]</c>. That is what makes this
    /// case different from the SitemapXml D-S1 defect it was assumed to mirror, where the containing
    /// type declares no namespace at all. Adding an explicit <c>Namespace</c> here would be a no-op —
    /// this test exists so M9 does not "fix" a namespace that is already correct, and so a future
    /// removal of the <c>XmlRoot</c> namespace (which <i>would</i> break it) fails loudly.
    /// </remarks>
    [Fact]
    public void Serialize_SearchForm_InheritsA9NamespaceFromTheRoot()
    {
        var document = OpenSearchTestHost.Serialize(Sample());

        Assert.Null(document.Root!.Element(XNamespace.None + "SearchForm"));

        var searchForm = document.Root.Element(OpenSearchTestHost.A9 + "SearchForm");
        Assert.NotNull(searchForm);
        Assert.Equal("https://example.com/", searchForm.Value);
    }

    /// <summary>
    /// The corollary of the above, asserted lexically: no element carries an <c>xmlns=""</c> reset,
    /// which is the visible symptom D-S15 predicted.
    /// </summary>
    [Fact]
    public void Serialize_NoElementEmitsAnEmptyNamespaceReset()
    {
        var raw = OpenSearchTestHost.Serialize(Sample()).ToString(SaveOptions.DisableFormatting);

        Assert.DoesNotContain("xmlns=\"\"", raw);
    }

    [Fact]
    public void Serialize_EmptyDescription_StillProducesRootElement()
    {
        var document = OpenSearchTestHost.Serialize(new OpenSearchDescription());

        Assert.Equal(OpenSearchTestHost.A9 + "OpenSearchDescription", document.Root!.Name);
        Assert.Empty(document.Root.Elements());
    }
}
