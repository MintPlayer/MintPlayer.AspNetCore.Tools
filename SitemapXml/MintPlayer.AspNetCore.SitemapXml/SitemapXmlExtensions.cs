using MintPlayer.AspNetCore.SitemapXml.Abstractions;
using MintPlayer.AspNetCore.SitemapXml.Options;
using Microsoft.Extensions.Options;

namespace MintPlayer.AspNetCore.SitemapXml;

public static class SitemapXmlExtensions
{
    private static readonly Type formatterType = typeof(Formatters.XmlSerializerOutputFormatter);

    public static IServiceCollection AddSitemapXml(this IServiceCollection services, Action<SitemapXmlOptions> options)
    {
        services.Configure(options);
        return services.AddSitemapXml();
    }

    /// <remarks>
    /// Idempotent: two libraries each calling this must not end up with two output formatters or
    /// two <see cref="ISitemapXml"/> registrations.
    /// </remarks>
    public static IServiceCollection AddSitemapXml(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(ISitemapXml)))
            services.AddSitemapXmlServices();

        services.AddControllersWithViews()
            .AddMvcOptions(mvc_options =>
            {
                mvc_options.RespectBrowserAcceptHeader = true;
                if (!mvc_options.OutputFormatters.Any(f => formatterType.IsInstanceOfType(f)))
                    mvc_options.OutputFormatters.Insert(0, new Formatters.XmlSerializerOutputFormatter());
            });

        return services;
    }

    /// <summary>Hosts a template XML stylesheet on the specified URL (defaults to /sitemap.xsl)</summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="SitemapXmlOptions.StylesheetUrl"/> is configured but is not an absolute path, or
    /// contains a double quote. Validated here, at startup, because the same value also becomes an
    /// href in every sitemap response.
    /// </exception>
    public static IEndpointConventionBuilder MapDefaultSitemapXmlStylesheet(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<SitemapXmlOptions>>();
        var pattern = StylesheetUrl.Resolve(options.Value.StylesheetUrl) ?? "/sitemap.xsl";

        return endpoints.MapGet(pattern, async (context) =>
        {
            context.Response.ContentType = "text/xsl; charset=UTF-8";

            using (var stream = typeof(SitemapXmlExtensions).Assembly.GetManifestResourceStream("MintPlayer.AspNetCore.SitemapXml.Assets.sitemap.xsl"))
            using (var streamreader = new System.IO.StreamReader(stream!))
            {
                var content = await streamreader.ReadToEndAsync();
                await context.Response.WriteAsync(content);
            }
        });
    }
}
