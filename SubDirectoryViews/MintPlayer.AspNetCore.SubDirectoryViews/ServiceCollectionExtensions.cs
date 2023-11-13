using Microsoft.AspNetCore.Mvc.Razor;

namespace MintPlayer.AspNetCore.SubDirectoryViews;

public static class ServiceCollectionExtensions
{
    /// <summary>Configures the <see cref="RazorViewEngineOptions"/> to look for Razor views in the specified subfolder.</summary>
    /// <param name="folder">Folder in the project containing the "Views" folder.</param>
    public static IServiceCollection ConfigureViewsInSubfolder(this IServiceCollection services, string folder)
    {
        return services
            .Configure<RazorViewEngineOptions>(options =>
            {
                var new_locations = options.ViewLocationFormats.Select(vlf => $"/{folder.Trim('/')}{vlf}").ToList();
                options.ViewLocationFormats.Clear();
                foreach (var format in new_locations)
                {
                    options.ViewLocationFormats.Add(format);
                }
            });
    }
}