namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// A <see cref="IEndpoint"/> routed on <c>GET</c>. Implementing this saves declaring
/// <c>Methods</c>; it adds nothing else.
/// </summary>
public interface IGetEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Get;
}

/// <summary>
/// A <see cref="IEndpoint{TRequest}"/> routed on <c>GET</c>. The generator bases the class on
/// <c>GetEndpoint&lt;TRequest&gt;</c>, which leaves <c>BindRequestAsync</c> abstract: a GET carries
/// no body, so there is nothing to bind by default and the endpoint must read
/// <typeparamref name="TRequest"/> out of the route, query string, or headers itself.
/// </summary>
/// <typeparam name="TRequest">The bound request.</typeparam>
public interface IGetEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Get;
}

/// <summary>
/// A <see cref="IEndpoint{TRequest, TResponse}"/> routed on <c>GET</c>: the binding is the
/// endpoint's own (see <see cref="IGetEndpoint{TRequest}"/>) and the response type reaches OpenAPI.
/// </summary>
/// <typeparam name="TRequest">The bound request.</typeparam>
/// <typeparam name="TResponse">The success response body.</typeparam>
public interface IGetEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Get;
}
