using Microsoft.AspNetCore.Http;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// The generated base class for an endpoint with a typed response and no request body —
/// <c>IGetEndpoint&lt;TResponse&gt;</c> and <c>IDeleteEndpoint&lt;TResponse&gt;</c>.
/// </summary>
/// <remarks>
/// A GET has no body to bind, so there is no request object to hand the handler. Whatever the
/// endpoint needs from the URL arrives through <c>[RouteParam]</c>/<c>[QueryParam]</c> properties,
/// assigned before <see cref="HandleAsync(CancellationToken)"/> is called — which is why this rung
/// exists rather than a placeholder request type that would appear, meaning nothing, in every
/// signature.
/// </remarks>
public abstract class ResponseEndpoint : IDisposable, IAsyncDisposable
{
    /// <summary>Handles the request once every bound property has been assigned.</summary>
    /// <param name="cancellationToken">
    /// The request's <c>RequestAborted</c> token; cancelled when the client disconnects.
    /// </param>
    public abstract Task<IResult> HandleAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Binds the properties, then invokes <see cref="HandleAsync(CancellationToken)"/>. A value that
    /// cannot be bound never reaches the handler; <see cref="OnBindFailedAsync"/> answers instead.
    /// </summary>
    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        try
        {
            BindParameters(httpContext);
        }
        catch (EndpointBindingException failure)
        {
            return await OnBindFailedAsync(httpContext, failure);
        }

        return await HandleAsync(httpContext.RequestAborted);
    }

    /// <summary>
    /// Assigns the <c>[RouteParam]</c>/<c>[QueryParam]</c> properties. Overridden by generated code;
    /// the default binds nothing.
    /// </summary>
    protected virtual void BindParameters(HttpContext context) { }

    /// <inheritdoc cref="EndpointBase{TRequest}.OnBindFailedAsync"/>
    protected virtual ValueTask<IResult> OnBindFailedAsync(HttpContext context, EndpointBindingException failure) =>
        new(ParameterBinding.Failure(failure));

    /// <summary>Called once the response has been produced. The default does nothing.</summary>
    public virtual void Dispose() { }

    /// <summary>Called once the response has been produced. The default calls <see cref="Dispose"/>.</summary>
    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
