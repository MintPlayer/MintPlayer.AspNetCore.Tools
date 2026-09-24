namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// An endpoint with a typed response and no request body — what <c>IGetEndpoint&lt;TResponse&gt;</c>
/// and <c>IDeleteEndpoint&lt;TResponse&gt;</c> are.
/// </summary>
/// <remarks>
/// The handler takes only a cancellation token. Values from the URL arrive through
/// <c>[RouteParam]</c>/<c>[QueryParam]</c> properties on the endpoint, bound before this is called.
/// <para>
/// This rung exists so a GET does not have to name a request it does not have. The alternative — a
/// placeholder request type — appeared, meaning nothing, in five of eight signatures in a prototype,
/// including the generic arguments OpenAPI sees.
/// </para>
/// <para>
/// <typeparamref name="TResponse"/> is not enforced at run time; like
/// <see cref="IEndpoint{TRequest, TResponse}"/>'s, it is documentation, emitted as
/// <c>.Produces&lt;TResponse&gt;(SuccessStatusCode)</c>.
/// </para>
/// </remarks>
/// <typeparam name="TResponse">The success response body, as documented to OpenAPI.</typeparam>
public interface IResponseEndpoint<TResponse> : IEndpoint
{
    /// <summary>
    /// Handles the request once every bound property has been assigned.
    /// </summary>
    /// <param name="cancellationToken">
    /// The request's <c>RequestAborted</c> token; cancelled when the client disconnects.
    /// </param>
    Task<IResult> HandleAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IEndpoint{TRequest, TResponse}.SuccessStatusCode"/>
    static virtual int SuccessStatusCode => 200;
}
