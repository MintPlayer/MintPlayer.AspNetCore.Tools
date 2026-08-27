namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// A <see cref="IEndpoint"/> routed on <c>DELETE</c>. Implementing this saves declaring
/// <c>Methods</c>; it adds nothing else.
/// </summary>
public interface IDeleteEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Delete;
}

/// <summary>
/// A <see cref="IEndpoint{TRequest}"/> routed on <c>DELETE</c>. The generator bases the class on
/// <c>DeleteEndpoint&lt;TRequest&gt;</c>, which leaves <c>BindRequestAsync</c> abstract: a DELETE is
/// treated as body-less, so the endpoint must read <typeparamref name="TRequest"/> out of the route,
/// query string, or headers itself.
/// </summary>
/// <typeparam name="TRequest">The bound request.</typeparam>
public interface IDeleteEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Delete;
}

/// <summary>
/// A <see cref="IEndpoint{TRequest, TResponse}"/> routed on <c>DELETE</c>: the binding is the
/// endpoint's own (see <see cref="IDeleteEndpoint{TRequest}"/>) and the response type reaches
/// OpenAPI. A delete that answers <c>204 No Content</c> should say so by overriding
/// <c>SuccessStatusCode</c>, otherwise OpenAPI advertises a 200 with a body.
/// </summary>
/// <typeparam name="TRequest">The bound request.</typeparam>
/// <typeparam name="TResponse">The success response body.</typeparam>
public interface IDeleteEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Delete;
}
