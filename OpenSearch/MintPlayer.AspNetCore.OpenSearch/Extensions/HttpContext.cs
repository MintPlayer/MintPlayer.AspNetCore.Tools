﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace MintPlayer.AspNetCore.OpenSearch.Extensions;

internal static class HttpContextExtensions
{
    private static Task ExecuteResultAsync<TResult>(this HttpContext context, TResult result) where TResult : IActionResult
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (result == null) throw new ArgumentNullException(nameof(result));

        var executor = context.RequestServices.GetRequiredService<IActionResultExecutor<TResult>>();
        var routeData = context.GetRouteData() ?? new RouteData();
        var actionDescriptor = new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor();
        var actionContext = new ActionContext(context, routeData, actionDescriptor);

        return executor.ExecuteAsync(actionContext, result);
    }

    internal static Task WriteModelAsync<TModel>(this HttpContext context, TModel model)
    {
        return context.ExecuteResultAsync(new ObjectResult(model) { DeclaredType = typeof(TModel) });
    }
}
