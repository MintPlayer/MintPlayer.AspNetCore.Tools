using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Base class for body-based endpoints (POST, PUT, PATCH).
/// Provides content-negotiated request body parsing using MVC input formatters
/// (if available) with JSON fallback.
/// </summary>
public abstract class BodyEndpoint<TRequest> : EndpointBase<TRequest>
{
    /// <summary>
    /// Reads the request body using MVC input formatters for content negotiation.
    /// If MVC is not configured, falls back to JSON deserialization.
    /// Override for custom binding (e.g., multi-source, form data).
    /// </summary>
    protected override async ValueTask<TRequest?> BindRequestAsync(HttpContext context)
    {
        // Try MVC input formatters (available if AddControllers/AddMvc was called)
        var modelMetadataProvider = context.RequestServices.GetService<IModelMetadataProvider>();
        var formatters = modelMetadataProvider is not null
            ? context.RequestServices.GetService<IOptions<MvcOptions>>()?.Value.InputFormatters
            : null;

        if (modelMetadataProvider is not null && formatters is { Count: > 0 })
        {
            var modelMetadata = modelMetadataProvider.GetMetadataForType(typeof(TRequest));
            var formatterContext = new InputFormatterContext(
                context,
                modelName: string.Empty,
                modelState: new ModelStateDictionary(),
                metadata: modelMetadata,
                readerFactory: (stream, encoding) => new StreamReader(stream, encoding));

            foreach (var formatter in formatters)
            {
                if (formatter.CanRead(formatterContext))
                {
                    var result = await formatter.ReadAsync(formatterContext);
                    if (result.IsModelSet)
                        return (TRequest?)result.Model;
                }
            }
        }

        // Fallback: JSON (works without MVC)
        return await context.Request.ReadFromJsonAsync<TRequest>(context.RequestAborted);
    }
}
