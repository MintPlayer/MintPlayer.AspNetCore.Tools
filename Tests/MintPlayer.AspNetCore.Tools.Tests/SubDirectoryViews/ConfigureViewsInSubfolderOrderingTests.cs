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
/// registration order, which is precisely what makes the ordering trap below possible.
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
    /// Called <i>before</i> <c>AddControllersWithViews()</c>, the extension silently does nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// D-M39, and the highest-value test in this library. The extension is an
    /// <c>IConfigureOptions</c> callback, and those run in registration order — so registering it
    /// first means it transforms an <b>empty</b> list, and the framework's own setup then populates
    /// the defaults afterwards, unprefixed.
    /// </para>
    /// <para>
    /// There is no error, no warning, and no way to tell from the registration site. The symptom is
    /// "my views in the subfolder are not found", which reads as a path problem rather than an
    /// ordering problem. Switching the library to <c>PostConfigure</c> makes it order-independent
    /// and is the fix; when that lands, this test flips to asserting every format is prefixed.
    /// </para>
    /// </remarks>
    [Fact]
    public void CalledBeforeAddControllersWithViews_SilentlyDoesNothing_KnownBug()
    {
        var formats = ResolveViewLocationFormats(services =>
        {
            services.ConfigureViewsInSubfolder("Client");
            services.AddControllersWithViews();
        });

        Assert.NotEmpty(formats);
        Assert.All(formats, format => Assert.DoesNotContain("/Client/", format));
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
}
