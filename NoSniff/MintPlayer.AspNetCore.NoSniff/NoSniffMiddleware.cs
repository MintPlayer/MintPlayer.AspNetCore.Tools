namespace MintPlayer.AspNetCore.NoSniff;

/// <summary>
/// Writes <c>X-Content-Type-Options: nosniff</c> on every response, telling browsers to trust the
/// declared <c>Content-Type</c> instead of guessing from the bytes — which is what stops an uploaded
/// file served as <c>text/plain</c> from being executed as script.
/// </summary>
/// <remarks>
/// The header is set from a <see cref="HttpResponse.OnStarting(Func{object, Task}, object)"/>
/// callback rather than being assigned before calling the next middleware. That is the whole point:
/// callbacks run immediately before the headers are flushed, after every downstream handler has had
/// its turn, so the header survives a handler further down the pipeline that rewrites or clears the
/// response headers. A header written on the way in would simply be lost in that case.
/// </remarks>
public class NoSniffMiddleware
{
    private readonly RequestDelegate next;

    /// <summary>Initialize the NoSniff middleware.</summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public NoSniffMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);

        this.next = next;
    }

    /// <summary>
    /// Registers the header-writing callback for this response and passes the request on.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the current request.</param>
    /// <remarks>
    /// Unconditional: there is no request for which sniffing protection is unwanted, so there is
    /// nothing to configure and no exclusion list.
    /// </remarks>
    public async Task Invoke(HttpContext httpContext)
    {
        httpContext.Response.OnStarting((state) =>
        {
            var context = (HttpContext)state;
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Task.CompletedTask;

        }, httpContext);

        await next(httpContext);
    }
}

/// <summary>Pipeline registration for <see cref="NoSniffMiddleware"/>.</summary>
public static class NoSniffMiddlewareExtensions
{
    /// <summary>
    /// Adds <see cref="NoSniffMiddleware"/> to the request pipeline.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <remarks>
    /// Position in the pipeline barely matters: the middleware only registers a callback that runs
    /// at flush time, so it covers responses produced by anything downstream of it — including
    /// static files and terminal endpoints.
    /// </remarks>
    public static IApplicationBuilder UseNoSniff(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<NoSniffMiddleware>();
    }
}
