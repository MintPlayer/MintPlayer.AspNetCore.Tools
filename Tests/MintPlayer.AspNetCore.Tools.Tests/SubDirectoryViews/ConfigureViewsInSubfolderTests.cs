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
/// <c>ConfigureViewsInSubfolder</c> registers an <see cref="IConfigureOptions{T}"/> callback, so the
/// transformation can be driven directly against a hand-seeded
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
    /// <summary>Applies the extension's configure callback to a list of seeded formats.</summary>
    private static IList<string> Rewrite(string folder, params string[] seededFormats)
    {
        var services = new ServiceCollection().ConfigureViewsInSubfolder(folder);
        var options = new RazorViewEngineOptions();

        foreach (var format in seededFormats)
        {
            options.ViewLocationFormats.Add(format);
        }

        foreach (var configure in services.BuildServiceProvider()
            .GetServices<IConfigureOptions<RazorViewEngineOptions>>())
        {
            configure.Configure(options);
        }

        return options.ViewLocationFormats;
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

    [Fact]
    public void ConfigureViewsInSubfolder_RegistersExactlyOneConfigureOptions()
    {
        var services = new ServiceCollection().ConfigureViewsInSubfolder("Client");

        Assert.Single(services.BuildServiceProvider()
            .GetServices<IConfigureOptions<RazorViewEngineOptions>>());
    }

    /// <summary>
    /// An empty or slash-only folder produces a double-slash prefix.
    /// </summary>
    /// <remarks>
    /// D-M25/D-M38. <c>folder.Trim('/')</c> on <c>""</c> or <c>"/"</c> yields <c>""</c>, and the
    /// interpolation then emits <c>"/" + "" + "/Views/..."</c>. The resulting path never matches a
    /// view, and nothing reports it.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("///")]
    public void Configure_EmptyOrSlashOnlyFolder_ProducesDoubleSlashPrefix_KnownBug(string folder)
    {
        var formats = Rewrite(folder, "/Views/{0}.cshtml");

        Assert.Equal("//Views/{0}.cshtml", Assert.Single(formats));
    }

    /// <summary>
    /// A null folder throws <see cref="NullReferenceException"/>, not
    /// <see cref="ArgumentNullException"/>.
    /// </summary>
    /// <remarks>
    /// D-M26/D-M38. <c>folder.Trim('/')</c> dereferences without a guard. The throw is also
    /// deferred until the options are first materialised, so the stack trace points at the options
    /// system rather than at the caller's registration line.
    /// </remarks>
    [Fact]
    public void Configure_NullFolder_ThrowsNullReference_KnownBug()
    {
        Assert.Throws<NullReferenceException>(() => Rewrite(null!, "/Views/{0}.cshtml"));
    }

    /// <summary>
    /// Backslashes are not normalised to forward slashes.
    /// </summary>
    /// <remarks>
    /// D-M27/D-M38. A Windows developer naturally writes <c>"Client\\Web"</c>, and
    /// <c>Trim('/')</c> leaves it untouched. Razor view paths are always forward-slashed and are
    /// resolved through <c>IFileProvider</c>, so the resulting location silently matches nothing —
    /// and it fails the same way on both platforms, which makes it harder to spot, not easier.
    /// </remarks>
    [Fact]
    public void Configure_BackslashFolder_IsNotNormalised_KnownBug()
    {
        var formats = Rewrite(@"Client\Web", "/Views/{0}.cshtml");

        Assert.Equal(@"/Client\Web/Views/{0}.cshtml", Assert.Single(formats));
    }

    /// <summary>
    /// Applying the extension twice double-prefixes.
    /// </summary>
    /// <remarks>
    /// D-M37. Each callback prefixes the <i>current</i> list, so two registrations compose into
    /// <c>/b/a/Views/...</c> instead of the caller's likely expectation that the last one wins. The
    /// same applies if the options are reconfigured for any other reason.
    /// </remarks>
    [Fact]
    public void Configure_AppliedTwice_DoublePrefixes_KnownBug()
    {
        var services = new ServiceCollection()
            .ConfigureViewsInSubfolder("a")
            .ConfigureViewsInSubfolder("b");

        var options = new RazorViewEngineOptions();
        options.ViewLocationFormats.Add("/Views/{0}.cshtml");

        foreach (var configure in services.BuildServiceProvider()
            .GetServices<IConfigureOptions<RazorViewEngineOptions>>())
        {
            configure.Configure(options);
        }

        Assert.Equal("/b/a/Views/{0}.cshtml", Assert.Single(options.ViewLocationFormats));
    }

    /// <summary>
    /// Only <c>ViewLocationFormats</c> is rewritten; Areas and Razor Pages keep looking in the root.
    /// </summary>
    /// <remarks>
    /// D-M36. The package promises "views live in a subfolder", but
    /// <c>AreaViewLocationFormats</c>, <c>PageViewLocationFormats</c> and
    /// <c>AreaPageViewLocationFormats</c> are untouched, so any application using Areas or Pages
    /// gets a half-applied configuration with no warning.
    /// </remarks>
    [Fact]
    public void Configure_LeavesAreaAndPageLocationFormatsUntouched_KnownGap()
    {
        var services = new ServiceCollection().ConfigureViewsInSubfolder("Client");
        var options = new RazorViewEngineOptions();
        options.ViewLocationFormats.Add("/Views/{0}.cshtml");
        options.AreaViewLocationFormats.Add("/Areas/{2}/Views/{1}/{0}.cshtml");
        options.PageViewLocationFormats.Add("/Pages/{0}.cshtml");

        foreach (var configure in services.BuildServiceProvider()
            .GetServices<IConfigureOptions<RazorViewEngineOptions>>())
        {
            configure.Configure(options);
        }

        Assert.Equal("/Client/Views/{0}.cshtml", Assert.Single(options.ViewLocationFormats));
        Assert.Equal("/Areas/{2}/Views/{1}/{0}.cshtml", Assert.Single(options.AreaViewLocationFormats));
        Assert.Equal("/Pages/{0}.cshtml", Assert.Single(options.PageViewLocationFormats));
    }
}
