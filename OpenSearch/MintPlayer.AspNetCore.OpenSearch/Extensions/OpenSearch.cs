using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.OpenSearch.Abstractions;
using MintPlayer.AspNetCore.OpenSearch.Extensions;
using System.Reflection;
using System.Text;

namespace MintPlayer.AspNetCore.OpenSearch
{
    /// <summary>
    /// Wires an OpenSearch engine into the host app: <see cref="AddOpenSearch{TService}(IServiceCollection)"/>
    /// registers the search implementation, <see cref="MapOpenSearch(IEndpointRouteBuilder)"/> exposes
    /// the three endpoints a browser needs to discover and use it.
    /// </summary>
    public static class OpenSearchExtensions
    {
        internal const string OsdxMediaType = "application/opensearchdescription+xml";
        internal const string SuggestionsMediaType = "application/x-suggestions+json";

        /// <summary>
        /// Single shared instance used to write the description without content negotiation. Safe to
        /// share: the base formatter clones <c>WriterSettings</c> per write and caches its
        /// serializers in a concurrent dictionary.
        /// </summary>
        private static readonly Formatters.XmlSerializerOutputFormatter osdxFormatter = new();

        /// <summary>
        /// Registers <typeparamref name="TService"/> as the app's search implementation and installs the
        /// XML output formatter that writes the description document.
        /// </summary>
        /// <typeparam name="TService">
        /// The host app's <see cref="IOpenSearchService"/>, resolved per request (scoped), so it may take
        /// a scoped dependency such as a database context.
        /// </typeparam>
        /// <param name="services">The service collection.</param>
        /// <remarks>
        /// Leaves every <see cref="OpenSearchOptions"/> at its default; use the
        /// <see cref="AddOpenSearch{TService}(IServiceCollection, Action{OpenSearchOptions})"/> overload to
        /// configure them. Calling <see cref="MapOpenSearch(IEndpointRouteBuilder)"/> without having called
        /// one of these first is an error the map call detects and reports.
        /// </remarks>
        public static IServiceCollection AddOpenSearch<TService>(this IServiceCollection services) where TService : class, IOpenSearchService
        {
            services.AddControllersWithViews()
                .AddMvcOptions(mvc_options =>
                {
                    mvc_options.RespectBrowserAcceptHeader = true;
                    if (!mvc_options.OutputFormatters.Any(f => f is Formatters.XmlSerializerOutputFormatter))
                        mvc_options.OutputFormatters.Insert(0, new Formatters.XmlSerializerOutputFormatter());
                });

            services.TryAddSingleton<OpenSearchMarker>();
            return services.AddScoped<IOpenSearchService, TService>();
        }

        /// <summary>
        /// Registers <typeparamref name="TService"/> as the app's search implementation and configures how
        /// the engine advertises itself.
        /// </summary>
        /// <typeparam name="TService">The host app's <see cref="IOpenSearchService"/>, registered scoped.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="options">
        /// Applied to <see cref="OpenSearchOptions"/>. The values are read once, when
        /// <see cref="MapOpenSearch(IEndpointRouteBuilder)"/> builds the endpoints — a later reconfiguration
        /// has no effect, and an invalid path is reported from there rather than from here.
        /// </param>
        public static IServiceCollection AddOpenSearch<TService>(this IServiceCollection services, Action<OpenSearchOptions> options) where TService : class, IOpenSearchService
        {
            services.AddOpenSearch<TService>();
            services.Configure<OpenSearchOptions>(options);
            return services;
        }

        /// <summary>
        /// Maps the three endpoints an OpenSearch engine consists of.
        /// </summary>
        /// <param name="routes">The endpoint route builder.</param>
        /// <remarks>
        /// <list type="bullet">
        /// <item><description>
        /// <see cref="OpenSearchOptions.OsdxEndpoint"/> serves the description document, as
        /// <c>application/opensearchdescription+xml</c> with a <c>Content-Disposition</c> filename derived
        /// from <see cref="OpenSearchOptions.ShortName"/>. Link it from the page head with
        /// <c>&lt;link rel="search" type="application/opensearchdescription+xml"&gt;</c> for browsers to
        /// discover it. Every URL in the document is made absolute against the requesting host, so the
        /// same app answers correctly behind a different origin or path base.
        /// </description></item>
        /// <item><description>
        /// <see cref="OpenSearchOptions.SuggestUrl"/> answers autocomplete polls with the OpenSearch
        /// suggestions JSON array from <see cref="IOpenSearchService.ProvideSuggestions"/>.
        /// </description></item>
        /// <item><description>
        /// <see cref="OpenSearchOptions.SearchUrl"/> asks <see cref="IOpenSearchService.PerformSearch"/>
        /// where to send the user and answers with that redirect.
        /// </description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// <c>AddOpenSearch</c> was never called, or one of the three path options is not a usable route
        /// pattern — it must start with <c>/</c> and must not contain a query string.
        /// </exception>
        public static IEndpointRouteBuilder MapOpenSearch(this IEndpointRouteBuilder routes)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));

            #region Compile opensearch options
            // Presence of the marker — not of IOptions<OpenSearchOptions>, which the options
            // infrastructure fabricates for any type — is what proves AddOpenSearch ran.
            if (routes.ServiceProvider.GetService<OpenSearchMarker>() is null)
                throw new InvalidOperationException("OpenSearchOptions not initialized. Did you forget to call AddOpenSearch?");

            var options = routes.ServiceProvider.GetService<IOptions<OpenSearchOptions>>()?.Value ?? new OpenSearchOptions();

            var osdxEndpoint = RequireRoutablePath(nameof(OpenSearchOptions.OsdxEndpoint), options.OsdxEndpoint.NullIfEmpty() ?? "/opensearch.xml");
            var searchUrl = RequireRoutablePath(nameof(OpenSearchOptions.SearchUrl), options.SearchUrl.NullIfEmpty() ?? "/search");
            var suggestUrl = RequireRoutablePath(nameof(OpenSearchOptions.SuggestUrl), options.SuggestUrl.NullIfEmpty() ?? "/suggest");
            var imageUrl = options.ImageUrl.NullIfEmpty() is { } configuredImageUrl
                ? RequireLeadingSlash(nameof(OpenSearchOptions.ImageUrl), configuredImageUrl)
                : null;

            var searchTermsParameter = options.SearchTermsParameter.NullIfEmpty() ?? "q";
            // GetEntryAssembly, not GetExecutingAssembly: the latter is this library, so every app
            // that did not set ShortName advertised itself as "MintPlayer.AspNetCore.OpenSearch".
            // It returns null when the process was started from unmanaged code, which is what makes
            // the "Website" fallback reachable.
            var shortName = options.ShortName.NullIfEmpty() ?? Assembly.GetEntryAssembly()?.GetName().Name.NullIfEmpty() ?? "Website";
            var description = options.Description.NullIfEmpty() ?? $"Search {shortName}";
            var imageType = options.ImageType.NullIfEmpty() ?? "image/png";
            var contact = options.Contact.NullIfEmpty();
            var contentDisposition = $"attachment; filename=\"{ToHeaderFileName(shortName)}.osdx\"";
            #endregion

            #region Handle routes specific to this package
            routes.MapGet(osdxEndpoint, async (context) =>
            {
                #region Return OSDX
                var origin = $"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase}";
                var descriptionDocument = new Data.OpenSearchDescription
                {
                    ShortName = shortName,
                    Description = description,
                    InputEncoding = "UTF-8",
                    // Omitted entirely when unconfigured: an <Image> whose URL is the bare host
                    // advertises the site root as a 16x16 png.
                    Image = imageUrl is null ? null : new Data.Image
                    {
                        Width = options.ImageWidth,
                        Height = options.ImageHeight,
                        Url = $"{origin}{imageUrl}",
                        Type = imageType,
                    },
                    Urls = [
                        new Data.Url { Type = "text/html", Method = "GET", Template = WithSearchTerms($"{origin}{searchUrl}", searchTermsParameter) },
                        new Data.Url { Type = SuggestionsMediaType, Method = "GET", Template = WithSearchTerms($"{origin}{suggestUrl}", searchTermsParameter) },
                        new Data.Url { Type = OsdxMediaType, Relation = "self", Template = $"{origin}{osdxEndpoint}" },
                    ],
                    Contact = contact,
                    SearchForm = $"{origin}/",
                };

                context.Response.Headers["Content-Disposition"] = contentDisposition;
                await context.WriteWithFormatterAsync(descriptionDocument, osdxFormatter, OsdxMediaType);
                #endregion
            });

            routes.MapGet(suggestUrl, async (context) =>
            {
                var service = context.RequestServices.GetRequiredService<IOpenSearchService>();
                var searchTerms = context.Request.Query[searchTermsParameter].FirstOrDefault();
                var suggestions = await service.ProvideSuggestions(searchTerms);
                await context.WriteModelAsync(
                    new object?[]
                    {
                        searchTerms,
                        suggestions?.ToArray() ?? [],
                    },
                    SuggestionsMediaType);
            });

            routes.MapGet(searchUrl, async (context) =>
            {
                var service = context.RequestServices.GetRequiredService<IOpenSearchService>();
                var searchTerms = context.Request.Query[searchTermsParameter].FirstOrDefault();
                var redirect = await service.PerformSearch(searchTerms);

                if (redirect is null)
                    throw new InvalidOperationException($"{nameof(IOpenSearchService)}.{nameof(IOpenSearchService.PerformSearch)} returned null.");
                if (string.IsNullOrWhiteSpace(redirect.Url))
                    throw new InvalidOperationException($"{nameof(IOpenSearchService)}.{nameof(IOpenSearchService.PerformSearch)} returned a redirect without a URL.");

                // Response.Redirect would drop Permanent/PreserveMethod, and it answers 302 with no
                // Location header at all for a null URL rather than failing.
                context.Response.StatusCode = (redirect.Permanent, redirect.PreserveMethod) switch
                {
                    (true, true) => StatusCodes.Status308PermanentRedirect,
                    (true, false) => StatusCodes.Status301MovedPermanently,
                    (false, true) => StatusCodes.Status307TemporaryRedirect,
                    (false, false) => StatusCodes.Status302Found,
                };
                context.Response.Headers.Location = redirect.Url;
            });
            #endregion

            return routes;
        }

        /// <remarks>
        /// A plain <c>string</c> check on purpose. <c>Uri.TryCreate(value, UriKind.Absolute, …)</c>
        /// is not usable here: on Linux a leading-slash path parses as the absolute
        /// <c>file:///…</c> URI and succeeds, while on Windows it fails — the same input, a different
        /// answer per OS.
        /// </remarks>
        private static string RequireLeadingSlash(string optionName, string value)
        {
            if (!value.StartsWith('/'))
                throw new InvalidOperationException($@"OpenSearchOptions.{optionName} must start with ""/"" (was ""{value}"").");

            return value;
        }

        /// <summary>
        /// Additionally rejects a query string: these three options are mapped as route patterns, and
        /// routing answers a <c>?</c> in a literal segment with a <c>RoutePatternException</c> that
        /// names neither the option nor the package.
        /// </summary>
        private static string RequireRoutablePath(string optionName, string value)
        {
            RequireLeadingSlash(optionName, value);

            if (value.Contains('?'))
                throw new InvalidOperationException($@"OpenSearchOptions.{optionName} is a route path and cannot contain a query string (was ""{value}""). Use SearchTermsParameter to name the query-string parameter.");

            return value;
        }

        /// <summary>Appends the OpenSearch <c>{searchTerms}</c> macro as a query-string parameter.</summary>
        private static string WithSearchTerms(string url, string parameter)
            => $"{url}?{parameter}={{searchTerms}}";

        /// <summary>
        /// Reduces a display name to characters that are legal in a <c>Content-Disposition</c>
        /// filename. The caller quotes the result as well; both are needed, because an unquoted
        /// header token cannot contain a space, a comma or an <c>=</c>.
        /// </summary>
        private static string ToHeaderFileName(string shortName)
        {
            var builder = new StringBuilder(shortName.Length);
            foreach (var c in shortName)
            {
                if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')
                    builder.Append(c);
            }

            return builder.Length == 0 ? "opensearch" : builder.ToString();
        }
    }
}
