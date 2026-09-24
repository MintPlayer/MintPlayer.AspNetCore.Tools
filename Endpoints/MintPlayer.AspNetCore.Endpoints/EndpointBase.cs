using Microsoft.AspNetCore.Http;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Base class for all typed endpoints. Provides the HandleAsync(HttpContext) bridge
/// and disposal logic. Subclassed by body-based and non-body-based variants.
/// </summary>
/// <typeparam name="TRequest">The bound request the typed handler receives.</typeparam>
public abstract class EndpointBase<TRequest> : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Binds the request from the HttpContext. Override for custom binding.
    /// Body-based subclasses (PostEndpoint, PutEndpoint, PatchEndpoint) provide a
    /// default using MVC input formatters with JSON fallback.
    /// Non-body subclasses (GetEndpoint, DeleteEndpoint) leave this abstract.
    /// </summary>
    /// <remarks>
    /// An override reports bad input by throwing <see cref="EndpointBindingException"/> with the
    /// status code it deserves; that — and returning <see langword="null"/> — is what routes the
    /// request to <see cref="OnBindFailedAsync"/> instead of the handler. Any <i>other</i> exception
    /// is left alone and propagates as a 500, because the library cannot know what it means.
    /// </remarks>
    /// <param name="context">The request to bind from.</param>
    protected abstract ValueTask<TRequest?> BindRequestAsync(HttpContext context);

    /// <summary>Typed request handler — implemented by the user's endpoint class.</summary>
    public abstract Task<IResult> HandleAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Bridge: IEndpoint.HandleAsync(HttpContext) -> BindRequestAsync -> HandleAsync(TRequest, CT).
    /// </summary>
    /// <remarks>
    /// A request that cannot be bound never reaches the typed handler: the handler's signature
    /// promises a non-null <c>TRequest</c>, so handing it a null (which a literal JSON <c>null</c>
    /// body produces) turns a malformed request into a 500 from inside the endpoint author's code.
    /// Binding failures become a response here instead — see <see cref="OnBindFailedAsync"/>.
    /// </remarks>
    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        TRequest? request;
        try
        {
            // Parameters before the body, and inside the same try: a malformed route value is
            // rejected without the body ever being read, and both failures reach the one
            // OnBindFailedAsync customisation point.
            BindParameters(httpContext);
            request = await BindRequestAsync(httpContext);
        }
        catch (EndpointBindingException failure)
        {
            return await OnBindFailedAsync(httpContext, failure);
        }

        if (request is null)
            return await OnBindFailedAsync(httpContext, null);

        return await HandleAsync(request, httpContext.RequestAborted);
    }

    /// <summary>
    /// Assigns the endpoint's <c>[RouteParam]</c>/<c>[QueryParam]</c> properties. Overridden by generated
    /// code; the default binds nothing. Runs before <see cref="BindRequestAsync"/>.
    /// </summary>
    protected virtual void BindParameters(HttpContext context) { }

    /// <summary>
    /// Produces the response for a request the endpoint could not bind. Override to customise the
    /// problem details, log, or map a failure onto a different status code.
    /// </summary>
    /// <param name="context">The request being bound.</param>
    /// <param name="failure">
    /// The binding failure, or <see langword="null"/> when binding completed but yielded no request
    /// at all (an explicit JSON <c>null</c>, or a hand-written binder returning <c>default</c>).
    /// </param>
    protected virtual ValueTask<IResult> OnBindFailedAsync(HttpContext context, EndpointBindingException? failure)
        => new(failure is null
            ? Results.BadRequest()
            : Results.Problem(statusCode: failure.StatusCode, detail: failure.Message));

    /// <summary>Releases the endpoint synchronously. The default is a no-op.</summary>
    public virtual void Dispose() { }

    /// <summary>
    /// Releases the endpoint. The default forwards to <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// Both disposal call sites prefer <see cref="IAsyncDisposable"/>, and this class implements
    /// both — so without this forwarding an overridden <c>Dispose()</c> would be dead code for every
    /// typed endpoint. An override of this method that does not call <c>base.DisposeAsync()</c>
    /// takes over completely, which is the usual contract.
    /// </remarks>
    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
