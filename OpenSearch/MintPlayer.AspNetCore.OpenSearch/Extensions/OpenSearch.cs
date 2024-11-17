using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.OpenSearch.Abstractions;
using MintPlayer.AspNetCore.OpenSearch.Extensions;
using System.Diagnostics;
using System.Reflection;

namespace MintPlayer.AspNetCore.OpenSearch
{
    public static class OpenSearchExtensions
    {
        private static Lazy<Type> formatterType = new(() => typeof(Formatters.XmlSerializerOutputFormatter));

        public static IServiceCollection AddOpenSearch<TService>(this IServiceCollection services) where TService : class, IOpenSearchService
        {
            services.AddControllersWithViews()
                .AddMvcOptions(mvc_options =>
                {
                    mvc_options.RespectBrowserAcceptHeader = true;
                    if (!mvc_options.OutputFormatters.Any(f => formatterType.Value.IsInstanceOfType(f)))
                        mvc_options.OutputFormatters.Insert(0, new Formatters.XmlSerializerOutputFormatter());
                });
            return services.AddScoped<IOpenSearchService, TService>();
        }

        public static IServiceCollection AddOpenSearch<TService>(this IServiceCollection services, Action<OpenSearchOptions> options) where TService : class, IOpenSearchService
        {
            services.AddOpenSearch<TService>();
            services.Configure<OpenSearchOptions>(options);
            return services;
        }

        public static IEndpointRouteBuilder MapOpenSearch(this IEndpointRouteBuilder routes)
        {
            #region Compile opensearch options
            var opts = routes.ServiceProvider.GetService<IOptions<OpenSearchOptions>>();
            var options = opts?.Value;
            if (options == null) throw new InvalidOperationException("OpenSearchOptions not initialized. Did you forget to call AddOpenSearch?");
            #endregion

            var osdxEndpoint = options.OsdxEndpoint.NullIfEmpty() ?? "/opensearch.xml";
            var suggestUrl = options.SuggestUrl.NullIfEmpty() ?? "/suggest";
            var searchUrl = options.SearchUrl.NullIfEmpty() ?? "/search";
            var shortName = options.ShortName.NullIfEmpty() ?? Assembly.GetExecutingAssembly()?.FullName ?? "Website";
            var description = options.Description.NullIfEmpty() ?? $"Search {shortName}";

            #region Check OSDX endpoint format
            if (!osdxEndpoint.StartsWith('/'))
                throw new Exception(@"OpenSearch endpoint must start with ""/""");
            #endregion

            #region Handle routes specific to this package
            routes.MapGet(osdxEndpoint, async (context) =>
            {
                #region Return OSDX
                context.Response.ContentType = "application/opensearchdescription+xml";
                context.Response.Headers["Content-Disposition"] = $"attachment; filename={shortName}.osdx";
                await context.WriteModelAsync(new Data.OpenSearchDescription
                {
                    ShortName = shortName,
                    Description = description,
                    InputEncoding = "UTF-8",
                    Image = new Data.Image
                    {
                        Width = 16,
                        Height = 16,
                        Url = $"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase}{options.ImageUrl}",
                        Type = "image/png"
                    },
                    Urls = new[] {
                        new Data.Url { Type = "text/html", Method = "GET", Template = $"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase}{searchUrl}" },
                        new Data.Url { Type = "application/x-suggestions+json", Method = "GET", Template = $"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase}{suggestUrl}" },
                        new Data.Url { Type = "application/opensearchdescription+xml", Relation = "self", Template = $"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase}{osdxEndpoint}" }
                    }.ToList(),
                    Contact = options.Contact,
                    SearchForm = $"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase}/"
                });
                #endregion
            });

            routes.MapGet(suggestUrl, async (context) =>
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
            });

            routes.MapGet(searchUrl, async (context) =>
            {
                var service = context.RequestServices.GetRequiredService<IOpenSearchService>();
                var searchTerms = (string?)context.GetRouteValue("searchTerms");
                var result = await service.PerformSearch(searchTerms);
                context.Response.Redirect(result.Url);
            });
            #endregion

            return routes;
        }
    }
}
