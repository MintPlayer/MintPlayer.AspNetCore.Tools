using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Validation;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Runs <c>Microsoft.Extensions.Validation</c> over a bound request body, explicitly (PRD R5.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why explicitly.</b> The framework's validation generator discovers types from the parameter
/// lists of hand-written <c>Map*</c> calls. The calls this library generates take only an
/// <c>HttpContext</c>, and one source generator never sees another's output, so the framework's
/// own endpoint filter can never know about <c>TRequest</c> (PRD P9.1). The consumer marks the type
/// with <c>[ValidatableType]</c> in hand-written code, which the validation generator does see, and
/// this class asks the resulting resolver for it.
/// </para>
/// <para>
/// <b>The only guard is the lookup.</b> <see cref="IOptions{TOptions}"/> of
/// <see cref="ValidationOptions"/> resolves in every host that has options at all, whether or not
/// <c>AddValidation()</c> was called, because the options type is default-constructible. What
/// changes is that it then has no resolvers, so
/// <see cref="ValidationOptions.TryGetValidatableTypeInfo"/> returns <see langword="false"/> — and
/// that is what makes an app without <c>AddValidation()</c> behave exactly as before. A null check
/// on the options would never fire (spike S2). <c>GetService</c> rather than
/// <c>GetRequiredService</c>, and a null-tolerant <c>RequestServices</c>, only cover a host with no
/// options registration, or no service provider, whatsoever.
/// </para>
/// <para>
/// <b>Two TFMs, two APIs.</b> Between .NET 10 and .NET 11 the type-info interface was renamed, the
/// context's <c>ValidationContext</c> was replaced by <c>ServiceProvider</c>, and the error map
/// changed shape (PRD R5.7). Both seams are contained here, and the result is normalised to one
/// shape so <see cref="EndpointBase{TRequest}.OnValidationFailedAsync"/> has a single signature.
/// </para>
/// </remarks>
internal static class RequestValidation
{
    /// <summary>
    /// Validates <paramref name="request"/> and returns its errors keyed by member path, or
    /// <see langword="null"/> when the request is valid or its type is not validatable.
    /// </summary>
    public static async ValueTask<IReadOnlyDictionary<string, string[]>?> ValidateAsync<TRequest>(
        TRequest request, HttpContext context)
    {
        if (request is null)
            return null;

        // RequestServices is null on a bare DefaultHttpContext; such a request ran before validation
        // existed and must still run, not throw ArgumentNullException from GetService.
        var options = context.RequestServices?.GetService<IOptions<ValidationOptions>>()?.Value;
        if (options is null)
            return null;

#if NET11_0_OR_GREATER
        if (!options.TryGetValidatableTypeInfo(typeof(TRequest), out IValidatableTypeInfo? typeInfo) || typeInfo is null)
            return null;

        var validateContext = new ValidateContext
        {
            ValidationOptions = options,
            ServiceProvider = context.RequestServices,
        };

        await typeInfo.ValidateAsync(request, validateContext, context.RequestAborted);

        if (validateContext.ValidationErrors is not { Count: > 0 } errors)
            return null;

        return errors.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Select(error => error.ErrorMessage).ToArray(),
            StringComparer.Ordinal);
#else
        if (!options.TryGetValidatableTypeInfo(typeof(TRequest), out IValidatableInfo? typeInfo) || typeInfo is null)
            return null;

        var validateContext = new ValidateContext
        {
            ValidationOptions = options,
            // The display-name overload: the one without it discovers the name by reflection and is
            // [RequiresUnreferencedCode] (IL2026 under IsAotCompatible, PRD R6.4). With no member
            // name set, that reflection falls back to the object's type name, so passing it is the
            // same value, and the validation resolver overwrites it per member as it walks the type.
            ValidationContext = new System.ComponentModel.DataAnnotations.ValidationContext(
                request, request.GetType().Name, context.RequestServices, items: null),
        };

        await typeInfo.ValidateAsync(request, validateContext, context.RequestAborted);

        return validateContext.ValidationErrors is { Count: > 0 } errors ? errors : null;
#endif
    }
}
