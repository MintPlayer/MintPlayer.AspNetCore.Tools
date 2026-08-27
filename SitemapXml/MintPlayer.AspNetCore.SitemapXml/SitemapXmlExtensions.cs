using MintPlayer.AspNetCore.SitemapXml.Abstractions;
using MintPlayer.AspNetCore.SitemapXml.Options;
using Microsoft.Extensions.Options;

namespace MintPlayer.AspNetCore.SitemapXml;

/// <summary>
/// Registers the sitemap services and the output formatter that renders
/// <see cref="Abstractions.Data.UrlSet"/> and <see cref="Abstractions.Data.SitemapIndex"/> as
/// sitemap XML, and optionally hosts a stylesheet for them.
/// </summary>
public static class SitemapXmlExtensions
{
    private static readonly Type formatterType = typeof(Formatters.XmlSerializerOutputFormatter);

    /// <summary>Configures <see cref="SitemapXmlOptions"/> and then registers the sitemap services.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="options">
    /// Configures the options — in practice, the stylesheet URL. Runs when the options are first
    /// resolved, not here, so a bad value surfaces at the first sitemap request or at
    /// <see cref="MapDefaultSitemapXmlStylesheet"/>.
    /// </param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddSitemapXml(this IServiceCollection services, Action<SitemapXmlOptions> options)
    {
        services.Configure(options);
        return services.AddSitemapXml();
    }

    /// <summary>
    /// Registers <see cref="ISitemapXml"/> and inserts the sitemap output formatter ahead of the
    /// MVC defaults, so a controller action returning a <see cref="Abstractions.Data.UrlSet"/> or
    /// <see cref="Abstractions.Data.SitemapIndex"/> is rendered as sitemap XML. Also enables
    /// <c>RespectBrowserAcceptHeader</c>, which the content negotiation depends on.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
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

    /// <summary>
    /// Hosts the built-in XSL stylesheet at <see cref="SitemapXmlOptions.StylesheetUrl"/>, or at
    /// <c>/sitemap.xsl</c> when none is configured, so a browser renders the sitemap as a readable
    /// table. Call it only if you want that default stylesheet — point
    /// <see cref="SitemapXmlOptions.StylesheetUrl"/> at your own route instead and leave this out.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The mapped endpoint's convention builder.</returns>
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
