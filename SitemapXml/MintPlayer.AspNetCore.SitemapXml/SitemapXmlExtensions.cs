using MintPlayer.AspNetCore.SitemapXml.Options;
using Microsoft.Extensions.Options;

namespace MintPlayer.AspNetCore.SitemapXml;

public static class SitemapXmlExtensions
{
    public static IServiceCollection AddSitemapXml(this IServiceCollection services, Action<SitemapXmlOptions> options)
    {
        var opt = new SitemapXmlOptions();
        options(opt);

        services.AddScoped<Abstractions.ISitemapXml, DependencyInjection.SitemapXml>();
        services.AddControllersWithViews()
            .AddMvcOptions(mvc_options =>
            {
                mvc_options.RespectBrowserAcceptHeader = true;
                if (!string.IsNullOrEmpty(opt.StylesheetUrl))
                    mvc_options.OutputFormatters.Insert(0, new Formatters.XmlSerializerOutputFormatter(opt.StylesheetUrl));
            });

        services.Configure(options);

        return services;
    }

    /// <summary>Hosts a template XML stylesheet on the specified URL</summary>
    public static IEndpointConventionBuilder MapDefaultSitemapXmlStylesheet(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<SitemapXmlOptions>>();
        return endpoints.MapGet(options.Value.StylesheetUrl, async (context) =>
        {
            context.Response.ContentType = "text/xsl; charset=UTF-8";

            using (var stream = typeof(Abstractions.Data.Sitemap).Assembly.GetManifestResourceStream("MintPlayer.AspNetCore.SitemapXml.Assets.sitemap.xsl"))
            using (var streamreader = new System.IO.StreamReader(stream))
            {
                var content = await streamreader.ReadToEndAsync();
                await context.Response.WriteAsync(content);
            }
        });
    }
}
