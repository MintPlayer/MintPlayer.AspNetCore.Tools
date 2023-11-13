using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.OpenSearch.Abstractions;
using MintPlayer.AspNetCore.OpenSearch.Extensions;
using System.Diagnostics;

namespace MintPlayer.AspNetCore.OpenSearch
{
    public static class OpenSearchExtensions
    {
        public static IServiceCollection AddOpenSearch<TService>(this IServiceCollection services) where TService : class, IOpenSearchService
        {
            services.AddControllersWithViews()
                .AddMvcOptions(mvc_options =>
                {
                    mvc_options.RespectBrowserAcceptHeader = true;
                    mvc_options.OutputFormatters.Insert(0, new Formatters.XmlSerializerOutputFormatter());
                });
            return services.AddScoped<IOpenSearchService, TService>();
        }

        public static IApplicationBuilder UseOpenSearch(this IApplicationBuilder app) 
        {
            #region Compile opensearch options
            var opts = app.ApplicationServices.GetService<IOptions<OpenSearchOptions>>();
            var options = opts.Value;

            Debug.WriteLine(options.Description);
            #endregion

            if (options == null) throw new Exception("tarara");

            #region Check OSDX endpoint format
            if (!options.OsdxEndpoint.StartsWith('/'))
                throw new Exception(@"OpenSearch endpoint must start with ""/""");
            #endregion

            //#region Get Service instance
            //var service = app.ApplicationServices.GetService<IOpenSearchService>();
            //#endregion

            #region Handle routes specific to this package
            var builder = new RouteBuilder(app);
            builder
                .MapVerb("GET", options.OsdxEndpoint, async (context) =>
                {
                    #region Return OSDX
                    //context.Response.ContentType = "application/opensearchdescription+xml; charset=utf-8";
                    context.Response.ContentType = "application/opensearchdescription+xml";
                    context.Response.Headers["Content-Disposition"] = $"attachment; filename={options.ShortName}.osdx";
                    await context.WriteModelAsync(new Data.OpenSearchDescription
                    {
                        ShortName = options.ShortName,
                        Description = options.Description,
                        InputEncoding = "UTF-8",
                        Image = new Data.Image
                        {
                            Width = 16,
                            Height = 16,
                            Url = $"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase}{options.ImageUrl}",
                            Type = "image/png"
                        },
                        Urls = new[] {
                            new Data.Url { Type = "text/html", Method = "GET", Template = $"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase}{options.SearchUrl}" },
                            new Data.Url { Type = "application/x-suggestions+json", Method = "GET", Template = $"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase}{options.SuggestUrl}" },
                            new Data.Url { Type = "application/opensearchdescription+xml", Relation = "self", Template = $"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase}{options.OsdxEndpoint}" }
                        }.ToList(),
                        Contact = options.Contact,
                        SearchForm = $"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase}/"
                    });
                    #endregion
                })
                .MapVerb("GET", options.SuggestUrl, async (context) =>
                {
                    var service = context.RequestServices.GetRequiredService<IOpenSearchService>();
                    var searchTerms = (string?)context.GetRouteValue("searchTerms");
                    var suggestions = await service.ProvideSuggestions(searchTerms);
                    context.Response.Headers["Content-Type"] = "application/json";
                    await context.WriteModelAsync(new object[]
                    {
                        searchTerms,
                        suggestions.ToArray()
                    });
                })
                .MapVerb("GET", options.SearchUrl, async (context) =>
                {
                    var service = context.RequestServices.GetRequiredService<IOpenSearchService>();
                    var searchTerms = (string?)context.GetRouteValue("searchTerms");
                    var result = await service.PerformSearch(searchTerms);
                    context.Response.Redirect(result.Url);
                });
            var router = builder.Build();
            #endregion

            return app.UseRouter(router);
        }
    }
}
