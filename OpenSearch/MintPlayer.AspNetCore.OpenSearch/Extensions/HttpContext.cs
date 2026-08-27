using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace MintPlayer.AspNetCore.OpenSearch.Extensions;

internal static class HttpContextExtensions
{
    private static Task ExecuteResultAsync<TResult>(this HttpContext context, TResult result) where TResult : IActionResult
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (result == null) throw new ArgumentNullException(nameof(result));

        var executor = context.RequestServices.GetRequiredService<IActionResultExecutor<TResult>>();
        var actionDescriptor = new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor();
        var actionContext = new ActionContext(context, context.GetRouteData(), actionDescriptor);

        return executor.ExecuteAsync(actionContext, result);
    }

    /// <summary>
    /// Writes <paramref name="model"/> through MVC content negotiation.
    /// </summary>
    /// <remarks>
    /// <paramref name="contentType"/>, when given, is the only media type the response may be
    /// written as. The formatter is still chosen by negotiation, so a client whose <c>Accept</c>
    /// excludes it gets the usual fallback — or a 406 where
    /// <c>MvcOptions.ReturnHttpNotAcceptable</c> is on.
    /// </remarks>
    internal static Task WriteModelAsync<TModel>(this HttpContext context, TModel model, string? contentType = null)
    {
        var result = new ObjectResult(model) { DeclaredType = typeof(TModel) };
        if (contentType is not null)
            result.ContentTypes.Add(contentType);

        return context.ExecuteResultAsync(result);
    }

    /// <summary>
    /// Writes <paramref name="model"/> with one specific formatter as one specific media type,
    /// bypassing content negotiation entirely.
    /// </summary>
    /// <remarks>
    /// The OSDX endpoint serves exactly one representation, to a browser registering a search
    /// engine rather than to a negotiating API client. Routing it through <c>ObjectResult</c> would
    /// make it depend on <c>MvcOptions.ReturnHttpNotAcceptable</c> staying at its default: turning
    /// that documented setting on gives any client without a wildcard <c>Accept</c> an empty 406,
    /// and the consumer has no per-endpoint way to opt out. Setting
    /// <c>ObjectResult.ContentTypes</c> does not help — the executor intersects it *with* the
    /// <c>Accept</c> header, so a mismatch is exactly what produces the 406.
    /// </remarks>
    internal static Task WriteWithFormatterAsync<TModel>(this HttpContext context, TModel model, TextOutputFormatter formatter, string contentType)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (formatter == null) throw new ArgumentNullException(nameof(formatter));

        var writerFactory = context.RequestServices.GetRequiredService<IHttpResponseStreamWriterFactory>();
        var formatterContext = new OutputFormatterWriteContext(context, writerFactory.CreateWriter, typeof(TModel), model)
        {
            ContentType = new StringSegment(contentType),
            ContentTypeIsServerDefined = true,
        };

        return formatter.WriteAsync(formatterContext);
    }
}
