using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.SubDirectoryViews;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SubDirectoryViews;

/// <summary>
/// Covers the view-location rewrite in isolation.
/// </summary>
/// <remarks>
/// <para>
/// <c>ConfigureViewsInSubfolder</c> registers an <see cref="IPostConfigureOptions{T}"/> callback, so
/// the transformation can be driven directly against a hand-seeded
/// <see cref="RazorViewEngineOptions"/>. That gives total control over the input list, which
/// matters because the interesting behaviour is entirely about what happens to the list contents.
/// </para>
/// <para>
/// No host and no MVC bootstrapping is needed or wanted here. An end-to-end "does a view in
/// /Client/Views actually render" test would need a separate Razor-SDK app with compiled views, and
/// it would be asserting that <c>RazorViewEngine</c> honours <c>ViewLocationFormats</c> — testing
/// the framework, not this package.
/// </para>
/// </remarks>
public class ConfigureViewsInSubfolderTests
{
    /// <summary>Runs the extension's post-configure callbacks over a seeded options instance.</summary>
    private static RazorViewEngineOptions PostConfigure(IServiceCollection services, RazorViewEngineOptions options)
    {
        foreach (var postConfigure in services.BuildServiceProvider()
            .GetServices<IPostConfigureOptions<RazorViewEngineOptions>>())
        {
            postConfigure.PostConfigure(Options.DefaultName, options);
        }

        return options;
    }

    /// <summary>Applies the extension to a list of seeded <c>ViewLocationFormats</c>.</summary>
    private static IList<string> Rewrite(string folder, params string[] seededFormats)
    {
        var services = new ServiceCollection().ConfigureViewsInSubfolder(folder);
        var options = new RazorViewEngineOptions();

        foreach (var format in seededFormats)
        {
            options.ViewLocationFormats.Add(format);
        }

        return PostConfigure(services, options).ViewLocationFormats;
    }

    [Fact]
    public void Configure_PrefixesEachFormatWithFolder()
    {
        var formats = Rewrite("Client", "/Views/{1}/{0}.cshtml");

        Assert.Equal("/Client/Views/{1}/{0}.cshtml", Assert.Single(formats));
    }

    [Fact]
    public void Configure_PreservesOrderAndCount()
    {
        var formats = Rewrite(
            "Client",
            "/Views/{1}/{0}.cshtml",
            "/Views/Shared/{0}.cshtml",
            "/Views/{0}.cshtml");

        Assert.Equal(
            [
                "/Client/Views/{1}/{0}.cshtml",
                "/Client/Views/Shared/{0}.cshtml",
                "/Client/Views/{0}.cshtml",
            ],
            formats);
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("/Client")]
    [InlineData("Client/")]
    [InlineData("/Client/")]
    [InlineData("//Client//")]
    public void Configure_TrimsSurroundingSlashesFromFolder(string folder)
    {
        var formats = Rewrite(folder, "/Views/{0}.cshtml");

        Assert.Equal("/Client/Views/{0}.cshtml", Assert.Single(formats));
    }

    [Fact]
    public void Configure_NestedFolder_IsPreserved()
    {
        var formats = Rewrite("src/Client", "/Views/{0}.cshtml");

        Assert.Equal("/src/Client/Views/{0}.cshtml", Assert.Single(formats));
    }

    [Fact]
    public void Configure_EmptyFormatList_StaysEmpty()
    {
        Assert.Empty(Rewrite("Client"));
    }

    [Fact]
    public void ConfigureViewsInSubfolder_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.ConfigureViewsInSubfolder("Client"));
    }

    /// <summary>
    /// The rewrite is registered as a post-configure callback, not a configure callback.
    /// </summary>
    /// <remarks>
    /// D-M39, fixed. This is the mechanism that makes the extension order-independent — see
    /// <c>ConfigureViewsInSubfolderOrderingTests</c> for the behaviour it buys.
    /// </remarks>
    [Fact]
    public void ConfigureViewsInSubfolder_RegistersExactlyOnePostConfigureAndNoConfigure()
    {
        var provider = new ServiceCollection().ConfigureViewsInSubfolder("Client").BuildServiceProvider();

        Assert.Single(provider.GetServices<IPostConfigureOptions<RazorViewEngineOptions>>());
        Assert.Empty(provider.GetServices<IConfigureOptions<RazorViewEngineOptions>>());
    }

    /// <summary>
    /// An empty, whitespace or separator-only folder is rejected at the call site.
    /// </summary>
    /// <remarks>
    /// D-M38, fixed. <c>folder.Trim('/')</c> on <c>""</c> or <c>"/"</c> used to yield <c>""</c> and
    /// the interpolation then emitted a <c>//Views/…</c> double-slash prefix that never matched a
    /// view, with nothing reporting it. None of these values names a subfolder, so none of them is
    /// accepted.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("///")]
    [InlineData("\\")]
    public void Configure_EmptyOrSeparatorOnlyFolder_ThrowsArgumentException(string folder)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new ServiceCollection().ConfigureViewsInSubfolder(folder));

        Assert.Equal("folder", ex.ParamName);
    }

    /// <summary>
    /// A null folder throws <see cref="ArgumentNullException"/> from the call itself.
    /// </summary>
    /// <remarks>
    /// D-M38, fixed. It used to throw <see cref="NullReferenceException"/> out of
    /// <c>folder.Trim('/')</c>, and the throw was deferred until the options were first
    /// materialised, so the stack trace pointed at the options system rather than at the caller's
    /// registration line. Validating eagerly fixes both halves.
    /// </remarks>
    [Fact]
    public void Configure_NullFolder_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().ConfigureViewsInSubfolder(null!));

        Assert.Equal("folder", ex.ParamName);
    }

    /// <summary>
    /// Backslashes are normalised to forward slashes.
    /// </summary>
    /// <remarks>
    /// D-M38, fixed. A Windows developer naturally writes <c>"Client\\Web"</c>, and
    /// <c>Trim('/')</c> left it untouched. Razor view paths are always forward-slashed and are
    /// resolved through <c>IFileProvider</c>, so the resulting location silently matched nothing —
    /// and it failed the same way on both platforms, which made it harder to spot, not easier.
    /// </remarks>
    [Theory]
    [InlineData(@"Client\Web")]
    [InlineData(@"\Client\Web\")]
    [InlineData("Client/Web")]
    public void Configure_BackslashFolder_IsNormalisedToForwardSlashes(string folder)
    {
        var formats = Rewrite(folder, "/Views/{0}.cshtml");

        Assert.Equal("/Client/Web/Views/{0}.cshtml", Assert.Single(formats));
    }

    /// <summary>
    /// Applying the extension twice does not nest the folders — the last call wins.
    /// </summary>
    /// <remarks>
    /// D-M37, fixed. Each registration used to prefix the <i>current</i> list, so two calls composed
    /// into <c>/b/a/Views/…</c>. The folder now lives in a single holder that a repeated call
    /// overwrites, so only one callback ever runs no matter how many times the extension is called.
    /// </remarks>
    [Fact]
    public void Configure_AppliedTwiceWithDifferentFolders_LastCallWins()
    {
        var services = new ServiceCollection()
            .ConfigureViewsInSubfolder("a")
            .ConfigureViewsInSubfolder("b");

        var options = new RazorViewEngineOptions();
        options.ViewLocationFormats.Add("/Views/{0}.cshtml");

        PostConfigure(services, options);

        Assert.Equal("/b/Views/{0}.cshtml", Assert.Single(options.ViewLocationFormats));
    }

    /// <summary>D-M37: repeating the same folder is a no-op rather than a double prefix.</summary>
    [Fact]
    public void Configure_AppliedTwiceWithSameFolder_PrefixesOnce()
    {
        var services = new ServiceCollection()
            .ConfigureViewsInSubfolder("Client")
            .ConfigureViewsInSubfolder("Client");

        var options = new RazorViewEngineOptions();
        options.ViewLocationFormats.Add("/Views/{0}.cshtml");

        PostConfigure(services, options);

        Assert.Equal("/Client/Views/{0}.cshtml", Assert.Single(options.ViewLocationFormats));
    }

    /// <summary>
    /// All four location-format lists are rewritten, not only <c>ViewLocationFormats</c>.
    /// </summary>
    /// <remarks>
    /// D-M36, fixed. <c>AreaViewLocationFormats</c>, <c>PageViewLocationFormats</c> and
    /// <c>AreaPageViewLocationFormats</c> were untouched, so any application using Areas or Razor
    /// Pages got a half-applied configuration with no warning. All four hold paths relative to the
    /// application root and all four move together when the content moves, so all four get the same
    /// treatment.
    /// </remarks>
    [Fact]
    public void Configure_RewritesAllFourLocationFormatLists()
    {
        var services = new ServiceCollection().ConfigureViewsInSubfolder("Client");
        var options = new RazorViewEngineOptions();
        options.ViewLocationFormats.Add("/Views/{0}.cshtml");
        options.AreaViewLocationFormats.Add("/Areas/{2}/Views/{1}/{0}.cshtml");
        options.PageViewLocationFormats.Add("/Pages/{0}.cshtml");
        options.AreaPageViewLocationFormats.Add("/Areas/{2}/Pages/{0}.cshtml");

        PostConfigure(services, options);

        Assert.Equal("/Client/Views/{0}.cshtml", Assert.Single(options.ViewLocationFormats));
        Assert.Equal("/Client/Areas/{2}/Views/{1}/{0}.cshtml", Assert.Single(options.AreaViewLocationFormats));
        Assert.Equal("/Client/Pages/{0}.cshtml", Assert.Single(options.PageViewLocationFormats));
        Assert.Equal("/Client/Areas/{2}/Pages/{0}.cshtml", Assert.Single(options.AreaPageViewLocationFormats));
    }
}
