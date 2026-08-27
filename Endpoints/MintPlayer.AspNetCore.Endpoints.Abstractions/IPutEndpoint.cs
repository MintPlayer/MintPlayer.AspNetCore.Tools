namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// A <see cref="IEndpoint"/> routed on <c>PUT</c>. Implementing this saves declaring
/// <c>Methods</c>; it adds nothing else — a raw endpoint reads the body off
/// <c>HttpContext</c> itself.
/// </summary>
public interface IPutEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Put;
}

/// <summary>
/// A <see cref="IEndpoint{TRequest}"/> routed on <c>PUT</c>. The generator bases the class on
/// <c>PutEndpoint&lt;TRequest&gt;</c>, so the request body is bound for you — content-negotiated
/// through MVC's input formatters where they are registered, JSON otherwise — and a body the library
/// cannot bind becomes a 400 or 415 without reaching the handler.
/// </summary>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
public interface IPutEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Put;
}

/// <summary>
/// A <see cref="IEndpoint{TRequest, TResponse}"/> routed on <c>PUT</c>: the body is bound for you
/// (see <see cref="IPutEndpoint{TRequest}"/>) and the response type reaches OpenAPI.
/// </summary>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
/// <typeparam name="TResponse">The success response body.</typeparam>
public interface IPutEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Put;
}
