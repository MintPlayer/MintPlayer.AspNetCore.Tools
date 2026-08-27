using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Globalization;

namespace MintPlayer.AspNetCore.Hsts;

internal class ImprovedHstsMiddleware
{

    private const string IncludeSubDomains = "; includeSubDomains";
    private const string Preload = "; preload";

    private readonly RequestDelegate next;
    private readonly ILogger logger;
    private readonly StringValues _strictTransportSecurityValue;
    private readonly IList<string> _excludedHosts;

    /// <summary>
    /// Initialize the HSTS middleware.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="options">The HSTS options. <see cref="HstsOptions.ExcludedHosts"/> is matched
    /// with <see cref="string.Equals(string?, string?, StringComparison)"/> — see
    /// <see cref="Invoke(HttpContext)"/>.</param>
    /// <param name="loggerFactory">Used to report the two cases in which no header is written.</param>
    public ImprovedHstsMiddleware(RequestDelegate next, IOptions<HstsOptions> options, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        this.next = next;
        this.logger = loggerFactory.CreateLogger<ImprovedHstsMiddleware>();

        var hstsOptions = options.Value;
        var maxAge = Convert.ToInt64(Math.Floor(hstsOptions.MaxAge.TotalSeconds))
                        .ToString(CultureInfo.InvariantCulture);
        var includeSubdomains = hstsOptions.IncludeSubDomains ? IncludeSubDomains : StringSegment.Empty;
        var preload = hstsOptions.Preload ? Preload : StringSegment.Empty;
        _strictTransportSecurityValue = new StringValues($"max-age={maxAge}{includeSubdomains}{preload}");
        _excludedHosts = hstsOptions.ExcludedHosts;
    }

    /// <summary>
    /// Invoke the middleware.
    /// </summary>
    /// <param name="context">The <see cref="HttpContext"/>.</param>
    /// <remarks>
    /// A host is excluded only when it is <i>equal</i> (ordinal, case-insensitive) to an entry in
    /// <see cref="HstsOptions.ExcludedHosts"/>. There is deliberately no wildcard or suffix
    /// matching, and no normalisation of IPv6 literals — <c>[::1]</c> and <c>::1</c> are different
    /// strings, so an IPv6 exclusion has to be written in the bracketed form that
    /// <see cref="HostString.Host"/> yields. This matches the framework's own
    /// <c>HstsMiddleware</c> exactly; the difference between the two is only <i>when</i> the header
    /// is written.
    /// </remarks>
    public async Task Invoke(HttpContext context)
    {
        if (!context.Request.IsHttps)
        {
            logger.LogDebug("The request is insecure. Skipping HSTS header.");
            await next(context);
            return;
        }

        if (IsHostExcluded(context.Request.Host.Host))
        {
            logger.LogDebug("The host is excluded. Skipping HSTS header.");
            await next(context);
            return;
        }

        // The difference with the AspNetCore HSTS-middleware.
        context.Response.OnStarting((state) =>
        {
            var httpContext = (HttpContext)state;
            httpContext.Response.Headers.StrictTransportSecurity = _strictTransportSecurityValue;
            return Task.CompletedTask;
        }, context);

        await next(context);
    }

    private bool IsHostExcluded(string host)
    {
        for (var i = 0; i < _excludedHosts.Count; i++)
        {
            if (string.Equals(host, _excludedHosts[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

// Extension method used to add the middleware to the HTTP request pipeline.
public static class ImprovedHstsMiddlewareExtensions
{
    public static IApplicationBuilder UseImprovedHsts(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ImprovedHstsMiddleware>();
    }
}
