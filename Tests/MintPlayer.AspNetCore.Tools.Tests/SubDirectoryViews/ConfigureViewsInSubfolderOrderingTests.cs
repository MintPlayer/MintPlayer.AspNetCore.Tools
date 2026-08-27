using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.SubDirectoryViews;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SubDirectoryViews;

/// <summary>
/// Covers the extension against the framework's real default view locations.
/// </summary>
/// <remarks>
/// These run through a real <see cref="ServiceCollection"/> with MVC registered — but still no
/// host and no server. The defaults are populated by the framework's own
/// <c>IConfigureOptions&lt;RazorViewEngineOptions&gt;</c>, and configure callbacks run in
/// registration order — which is precisely why this extension has to post-configure.
/// </remarks>
public class ConfigureViewsInSubfolderOrderingTests
{
    /// <summary>
    /// A minimal <see cref="IWebHostEnvironment"/>, because <c>AddControllersWithViews()</c>
    /// requires one to resolve. Still cheaper than a host by a wide margin.
    /// </summary>
    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        // Must name a REAL, loadable assembly: AddControllersWithViews() resolves MVC
        // application parts by loading ApplicationName, and a made-up value fails with
        // FileNotFoundException rather than anything that mentions application parts.
        public string ApplicationName { get; set; } =
            typeof(ConfigureViewsInSubfolderOrderingTests).Assembly.GetName().Name!;
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IList<string> ResolveViewLocationFormats(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
        configure(services);

        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<RazorViewEngineOptions>>()
            .Value
            .ViewLocationFormats;
    }

    [Fact]
    public void CalledAfterAddControllersWithViews_PrefixesEveryDefaultFormat()
    {
        var formats = ResolveViewLocationFormats(services =>
        {
            services.AddControllersWithViews();
            services.ConfigureViewsInSubfolder("Client");
        });

        Assert.NotEmpty(formats);
        Assert.All(formats, format => Assert.StartsWith("/Client/", format));
    }

    /// <summary>
    /// Called <i>before</i> <c>AddControllersWithViews()</c>, the extension works just the same.
    /// </summary>
    /// <remarks>
    /// <para>
    /// D-M39, fixed, and the highest-value test in this library. The extension used to be an
    /// <c>IConfigureOptions</c> callback, and those run in registration order — so registering it
    /// first meant it transformed an <b>empty</b> list, and the framework's own setup then populated
    /// the defaults afterwards, unprefixed.
    /// </para>
    /// <para>
    /// There was no error, no warning, and no way to tell from the registration site. The symptom
    /// was "my views in the subfolder are not found", which reads as a path problem rather than an
    /// ordering problem. It is now a <c>PostConfigure</c> callback, which runs after every
    /// <c>Configure</c> callback and so cannot observe a half-populated list.
    /// </para>
    /// </remarks>
    [Fact]
    public void CalledBeforeAddControllersWithViews_PrefixesEveryDefaultFormat()
    {
        var formats = ResolveViewLocationFormats(services =>
        {
            services.ConfigureViewsInSubfolder("Client");
            services.AddControllersWithViews();
        });

        Assert.NotEmpty(formats);
        Assert.All(formats, format => Assert.StartsWith("/Client/", format));
    }

    /// <summary>
    /// The two registration orders produce identical results, which is the whole promise of the fix.
    /// </summary>
    [Fact]
    public void RegistrationOrder_DoesNotMatter()
    {
        var after = ResolveViewLocationFormats(services =>
        {
            services.AddControllersWithViews();
            services.ConfigureViewsInSubfolder("Client");
        });

        var before = ResolveViewLocationFormats(services =>
        {
            services.ConfigureViewsInSubfolder("Client");
            services.AddControllersWithViews();
        });

        Assert.Equal(after, before);
    }

    /// <summary>Control: the defaults are untouched when the extension is not called.</summary>
    [Fact]
    public void WithoutTheExtension_DefaultFormatsAreUnchanged()
    {
        var formats = ResolveViewLocationFormats(services => services.AddControllersWithViews());

        Assert.NotEmpty(formats);
        Assert.All(formats, format => Assert.StartsWith("/Views/", format));
    }

    /// <summary>
    /// The controller and shared lookups both survive the rewrite, since those are the two paths
    /// every MVC application actually depends on.
    /// </summary>
    [Fact]
    public void PrefixedFormats_StillIncludeControllerAndSharedLookups()
    {
        var formats = ResolveViewLocationFormats(services =>
        {
            services.AddControllersWithViews();
            services.ConfigureViewsInSubfolder("Client");
        });

        Assert.Contains("/Client/Views/{1}/{0}.cshtml", formats);
        Assert.Contains("/Client/Views/Shared/{0}.cshtml", formats);
    }

    /// <summary>
    /// Areas and Razor Pages are prefixed against the framework's real defaults too.
    /// </summary>
    /// <remarks>
    /// D-M36. The unit suite seeds one format per list; this one uses all of MVC's. Razor Pages has
    /// to be registered as well — <c>AddControllersWithViews()</c> alone leaves
    /// <c>AreaPageViewLocationFormats</c> empty, so without it the assertion would pass vacuously.
    /// </remarks>
    [Fact]
    public void AreaAndPageDefaults_AreAlsoPrefixed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
        services.AddControllersWithViews();
        services.AddRazorPages();
        services.ConfigureViewsInSubfolder("Client");

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<RazorViewEngineOptions>>()
            .Value;

        Assert.NotEmpty(options.AreaViewLocationFormats);
        Assert.All(options.AreaViewLocationFormats, format => Assert.StartsWith("/Client/", format));
        Assert.NotEmpty(options.PageViewLocationFormats);
        Assert.All(options.PageViewLocationFormats, format => Assert.StartsWith("/Client/", format));
        Assert.NotEmpty(options.AreaPageViewLocationFormats);
        Assert.All(options.AreaPageViewLocationFormats, format => Assert.StartsWith("/Client/", format));
    }
}
