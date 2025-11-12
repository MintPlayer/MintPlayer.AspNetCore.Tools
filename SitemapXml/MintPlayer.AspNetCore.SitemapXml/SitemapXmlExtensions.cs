using MintPlayer.AspNetCore.SitemapXml.Options;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.SitemapXml.Extensions;

namespace MintPlayer.AspNetCore.SitemapXml;

public static class SitemapXmlExtensions
{
    public static IServiceCollection AddSitemapXml(this IServiceCollection services, Action<SitemapXmlOptions> options)
    {
        services.Configure(options);
        return services.AddSitemapXml();
    }
    public static IServiceCollection AddSitemapXml(this IServiceCollection services)
    {
        services.AddSitemapXmlServices();
        services.AddControllersWithViews()
            .AddMvcOptions(mvc_options =>
            {
                mvc_options.RespectBrowserAcceptHeader = true;
                mvc_options.OutputFormatters.Insert(0, new Formatters.XmlSerializerOutputFormatter());
            });


        return services;
    }

    /// <summary>Hosts a template XML stylesheet on the specified URL (defaults to /sitemap.xsl)</summary>
    public static IEndpointConventionBuilder MapDefaultSitemapXmlStylesheet(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<SitemapXmlOptions>>();
        return endpoints.MapGet(options.Value.StylesheetUrl.NullIfEmpty() ?? "/sitemap.xsl", async (context) =>
        {
            context.Response.ContentType = "text/xsl; charset=UTF-8";

            using (var stream = typeof(SitemapXmlExtensions).Assembly.GetManifestResourceStream("MintPlayer.AspNetCore.SitemapXml.Assets.sitemap.xsl"))
            using (var streamreader = new System.IO.StreamReader(stream))
            {
                var content = await streamreader.ReadToEndAsync();
                await context.Response.WriteAsync(content);
            }
        });
    }
}
