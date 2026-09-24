using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Base class for every endpoint that declares a request type. The request is the body, on every
/// verb: a GET or DELETE that declares one is saying it takes a body anyway. This class supplies a working
/// <see cref="BindRequestAsync"/>, so a subclass need not write any binding at all — the body is
/// content-negotiated through MVC's input formatters where they are registered, and deserialized as
/// JSON where they are not.
/// </summary>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
public abstract class BodyEndpoint<TRequest> : EndpointBase<TRequest>
{
    /// <summary>
    /// Reads the request body using MVC input formatters for content negotiation.
    /// If MVC is not configured, falls back to JSON deserialization.
    /// Override for custom binding (e.g., multi-source, form data).
    /// </summary>
    /// <remarks>
    /// Every way this can fail ends as an <see cref="EndpointBindingException"/> carrying the status
    /// code the client deserves, so the caller never has to know that the body was read by an MVC
    /// formatter, by <c>System.Text.Json</c>, or by both in turn.
    /// <para>
    /// The body is made re-readable before the formatters run. A formatter is allowed to claim the
    /// body (<c>CanRead</c>) and then decline to produce a model, and without rewinding, the JSON
    /// fallback would read a drained stream and report a parse error at position 0 — blaming the
    /// client for a double-read inside the library.
    /// </para>
    /// </remarks>
    /// <param name="context">The request to read the body from.</param>
    protected override async ValueTask<TRequest?> BindRequestAsync(HttpContext context)
    {
        // Try MVC input formatters (available if AddControllers/AddMvc was called)
        var modelMetadataProvider = context.RequestServices.GetService<IModelMetadataProvider>();
        var formatters = modelMetadataProvider is not null
            ? context.RequestServices.GetService<IOptions<MvcOptions>>()?.Value.InputFormatters
            : null;

        if (modelMetadataProvider is not null && formatters is { Count: > 0 })
        {
            if (!context.Request.Body.CanSeek)
                context.Request.EnableBuffering();

            var modelState = new ModelStateDictionary();
            var modelMetadata = modelMetadataProvider.GetMetadataForType(typeof(TRequest));
            var formatterContext = new InputFormatterContext(
                context,
                modelName: string.Empty,
                modelState: modelState,
                metadata: modelMetadata,
                readerFactory: (stream, encoding) => new StreamReader(stream, encoding));

            foreach (var formatter in formatters)
            {
                if (!formatter.CanRead(formatterContext))
                    continue;

                var result = await formatter.ReadAsync(formatterContext);
                if (result.IsModelSet)
                    return (TRequest?)result.Model;

                // The formatter declined. Surface whatever it recorded rather than silently
                // re-reading the body with a different reader and reporting an unrelated error.
                if (!modelState.IsValid)
                    throw new EndpointBindingException(
                        StatusCodes.Status400BadRequest,
                        DescribeModelStateErrors(modelState));

                Rewind(context);
            }

            Rewind(context);
        }

        // Fallback: JSON (works without MVC)
        try
        {
            return await context.Request.ReadFromJsonAsync<TRequest>(context.RequestAborted);
        }
        catch (JsonException exception)
        {
            throw new EndpointBindingException(
                StatusCodes.Status400BadRequest,
                "The request body could not be read as JSON.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            // ReadFromJsonAsync throws this when the request's content type is not JSON.
            throw new EndpointBindingException(
                StatusCodes.Status415UnsupportedMediaType,
                $"The content type '{context.Request.ContentType}' is not supported by this endpoint.",
                exception);
        }
    }

    private static void Rewind(HttpContext context)
    {
        if (context.Request.Body.CanSeek)
            context.Request.Body.Position = 0;
    }

    private static string DescribeModelStateErrors(ModelStateDictionary modelState)
    {
        var messages = modelState
            .SelectMany(entry => (entry.Value?.Errors ?? []).Select(error =>
                string.IsNullOrEmpty(entry.Key)
                    ? Describe(error)
                    : $"{entry.Key}: {Describe(error)}"));

        return $"The request body is not valid. {string.Join("; ", messages)}";

        static string Describe(ModelError error) =>
            string.IsNullOrEmpty(error.ErrorMessage)
                ? error.Exception?.Message ?? "invalid value"
                : error.ErrorMessage;
    }
}
