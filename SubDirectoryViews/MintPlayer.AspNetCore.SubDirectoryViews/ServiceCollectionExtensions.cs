using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Options;

namespace MintPlayer.AspNetCore.SubDirectoryViews;

/// <summary>
/// Relocates Razor view discovery into a subfolder of the project.
/// </summary>
/// <remarks>
/// Useful when the views live alongside a client application rather than at the project root — for
/// example a <c>ClientApp</c> or <c>Web</c> folder shared with a SPA build.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configures the <see cref="RazorViewEngineOptions"/> to look for Razor views, Razor pages and
    /// their Area equivalents in the specified subfolder.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="folder">Folder in the project containing the "Views" folder. Forward and
    /// backward slashes are both accepted, leading/trailing and repeated separators are ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="folder"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is empty, whitespace, or
    /// contains only separators — none of which name a subfolder.</exception>
    /// <remarks>
    /// <para>
    /// The rewrite runs as a <see cref="IPostConfigureOptions{TOptions}"/> callback, so this method
    /// may be called before or after <c>AddControllersWithViews()</c>: post-configure callbacks run
    /// after every <see cref="IConfigureOptions{TOptions}"/> callback, including the framework's own
    /// one that populates the default locations.
    /// </para>
    /// <para>
    /// Calling it more than once is safe. The last call wins — the folders do not nest.
    /// </para>
    /// </remarks>
    public static IServiceCollection ConfigureViewsInSubfolder(this IServiceCollection services, string folder)
    {
        ArgumentNullException.ThrowIfNull(services);

        var prefix = NormalizePrefix(folder);

        // The prefix lives in a holder rather than in the callback's closure, so that a repeated
        // call replaces it instead of registering a second callback that would prefix the already
        // prefixed locations.
        var holder = services
            .FirstOrDefault(descriptor => descriptor.ServiceType == typeof(SubfolderPrefix))
            ?.ImplementationInstance as SubfolderPrefix;

        if (holder is null)
        {
            holder = new SubfolderPrefix();
            services.AddSingleton(holder);
            services.PostConfigure<RazorViewEngineOptions>(options =>
            {
                Prefix(options.ViewLocationFormats, holder.Value);
                Prefix(options.AreaViewLocationFormats, holder.Value);
                Prefix(options.PageViewLocationFormats, holder.Value);
                Prefix(options.AreaPageViewLocationFormats, holder.Value);
            });
        }

        holder.Value = prefix;

        return services;
    }

    /// <summary>Turns a caller-supplied folder into a rooted, forward-slashed path prefix.</summary>
    private static string NormalizePrefix(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var segments = folder
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            throw new ArgumentException(
                $"'{folder}' does not name a subfolder.", nameof(folder));
        }

        return $"/{string.Join('/', segments)}";
    }

    /// <summary>Prefixes every location format in place.</summary>
    private static void Prefix(IList<string> formats, string prefix)
    {
        for (var i = 0; i < formats.Count; i++)
        {
            formats[i] = prefix + formats[i];
        }
    }

    private sealed class SubfolderPrefix
    {
        public string Value { get; set; } = string.Empty;
    }
}
