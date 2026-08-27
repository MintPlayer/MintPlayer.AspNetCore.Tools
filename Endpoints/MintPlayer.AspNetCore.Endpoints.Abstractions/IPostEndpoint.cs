namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// A <see cref="IEndpoint"/> routed on <c>POST</c>. Implementing this saves declaring
/// <c>Methods</c>; it adds nothing else — a raw endpoint reads the body off
/// <c>HttpContext</c> itself.
/// </summary>
public interface IPostEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Post;
}

/// <summary>
/// A <see cref="IEndpoint{TRequest}"/> routed on <c>POST</c>. The generator bases the class on
/// <c>PostEndpoint&lt;TRequest&gt;</c>, so the request body is bound for you — content-negotiated
/// through MVC's input formatters where they are registered, JSON otherwise — and a body the library
/// cannot bind becomes a 400 or 415 without reaching the handler.
/// </summary>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
public interface IPostEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Post;
}

/// <summary>
/// A <see cref="IEndpoint{TRequest, TResponse}"/> routed on <c>POST</c>: the body is bound for you
/// (see <see cref="IPostEndpoint{TRequest}"/>) and the response type reaches OpenAPI. A create that
/// answers <c>201 Created</c> should say so by overriding <c>SuccessStatusCode</c>, which otherwise
/// documents a 200.
/// </summary>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
/// <typeparam name="TResponse">The success response body.</typeparam>
public interface IPostEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Post;
}
