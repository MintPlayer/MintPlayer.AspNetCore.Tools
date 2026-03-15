using Microsoft.AspNetCore.Http;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Base class for all typed endpoints. Provides the HandleAsync(HttpContext) bridge
/// and disposal logic. Subclassed by body-based and non-body-based variants.
/// </summary>
public abstract class EndpointBase<TRequest> : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Binds the request from the HttpContext. Override for custom binding.
    /// Body-based subclasses (PostEndpoint, PutEndpoint, PatchEndpoint) provide a
    /// default using MVC input formatters with JSON fallback.
    /// Non-body subclasses (GetEndpoint, DeleteEndpoint) leave this abstract.
    /// </summary>
    protected abstract ValueTask<TRequest?> BindRequestAsync(HttpContext context);

    /// <summary>Typed request handler — implemented by the user's endpoint class.</summary>
    public abstract Task<IResult> HandleAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>Bridge: IEndpoint.HandleAsync(HttpContext) -> BindRequestAsync -> HandleAsync(TRequest, CT).</summary>
    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var request = await BindRequestAsync(httpContext);
        return await HandleAsync(request!, httpContext.RequestAborted);
    }

    public virtual void Dispose() { }
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
