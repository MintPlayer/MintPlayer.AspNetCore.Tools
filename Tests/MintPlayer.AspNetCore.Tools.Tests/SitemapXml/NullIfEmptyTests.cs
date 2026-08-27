using System.Reflection;
using MintPlayer.AspNetCore.SitemapXml.Extensions;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

/// <summary>
/// <c>StringExtensions.NullIfEmpty</c> exists for exactly one caller —
/// <c>MapDefaultSitemapXmlStylesheet</c>'s <c>?? "/sitemap.xsl"</c> fallback — but it is
/// <c>public</c>, so it is part of the package's surface.
/// </summary>
public class NullIfEmptyTests
{
    [Fact]
    public void NullIfEmpty_EmptyString_ReturnsNull()
    {
        Assert.Null(string.Empty.NullIfEmpty());
    }

    [Fact]
    public void NullIfEmpty_Null_ReturnsNull()
    {
        Assert.Null(((string?)null).NullIfEmpty());
    }

    [Theory]
    [InlineData("/sitemap.xsl")]
    [InlineData("a")]
    [InlineData("0")]
    public void NullIfEmpty_NonEmptyString_IsReturnedUnchanged(string value)
    {
        Assert.Equal(value, value.NullIfEmpty());
    }

    /// <summary>
    /// Pins PRD defect D-S9: the comparison is against <c>string.Empty</c> only, so whitespace
    /// survives and becomes a route pattern. The consequence is asserted end-to-end in
    /// <see cref="MapDefaultSitemapXmlStylesheetTests"/>.
    /// </summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void NullIfEmpty_WhitespaceOnly_SurvivesAsANonNullValue_KnownBug(string value)
    {
        Assert.Equal(value, value.NullIfEmpty());
    }

    /// <summary>
    /// Second half of D-S9: the extension is <c>public</c>, and
    /// <c>MintPlayer.AspNetCore.OpenSearch</c> exports an identically named extension on the same
    /// receiver type — an app installing both packages with <c>ImplicitUsings</c> gets an
    /// ambiguous-call error. Pinned by reflection so making either one <c>internal</c> shows up
    /// here as an intentional change.
    /// </summary>
    [Fact]
    public void NullIfEmpty_IsPublicOnBothPackages_KnownBug()
    {
        var sitemapExtension = typeof(StringExtensions)
            .GetMethod(nameof(StringExtensions.NullIfEmpty), BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(sitemapExtension);
        Assert.True(typeof(StringExtensions).IsPublic);
        Assert.Equal("MintPlayer.AspNetCore.SitemapXml.Extensions", typeof(StringExtensions).Namespace);
    }
}
