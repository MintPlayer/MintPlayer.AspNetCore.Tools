using System.Reflection;
using MintPlayer.AspNetCore.SitemapXml.Extensions;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

/// <summary>
/// <c>StringExtensions.NullIfEmpty</c> is the single owner of "a blank stylesheet URL means not
/// configured". It is <c>internal</c> (see D-S9 below) and reached here through
/// <c>InternalsVisibleTo</c>.
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
    /// PRD defect D-S9: the comparison used to be against <c>string.Empty</c> only, so whitespace
    /// survived and became a route pattern nobody could type. The consequence is asserted
    /// end-to-end in <see cref="MapDefaultSitemapXmlStylesheetTests"/>.
    /// </summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void NullIfEmpty_WhitespaceOnly_ReturnsNull(string value)
    {
        Assert.Null(value.NullIfEmpty());
    }

    /// <summary>
    /// Second half of D-S9: the extension is no longer part of the package's public surface.
    /// <c>MintPlayer.AspNetCore.OpenSearch</c> exports an identically named extension on the same
    /// receiver type, so while both were <c>public</c> an app installing both packages with
    /// <c>ImplicitUsings</c> on got an ambiguous-call error on <c>value.NullIfEmpty()</c>.
    /// </summary>
    /// <remarks>
    /// Asserted by reflection rather than by a compile-time reference, because the thing being
    /// pinned is the visibility itself — a future "make this public again, it's handy" would
    /// silently re-break every consumer of both packages.
    /// </remarks>
    [Fact]
    public void NullIfEmpty_IsNotPartOfThePublicSurface()
    {
        Assert.False(typeof(StringExtensions).IsPublic);
        Assert.False(typeof(StringExtensions).IsVisible);
        Assert.Empty(typeof(StringExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.NotNull(typeof(StringExtensions)
            .GetMethod(nameof(StringExtensions.NullIfEmpty), BindingFlags.NonPublic | BindingFlags.Static));
    }
}
