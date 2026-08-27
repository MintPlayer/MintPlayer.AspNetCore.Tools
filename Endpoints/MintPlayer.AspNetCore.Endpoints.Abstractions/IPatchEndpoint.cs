namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// A <see cref="IEndpoint"/> routed on <c>PATCH</c>. Implementing this saves declaring
/// <c>Methods</c>; it adds nothing else — a raw endpoint reads the body off
/// <c>HttpContext</c> itself.
/// </summary>
public interface IPatchEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Patch;
}

/// <summary>
/// A <see cref="IEndpoint{TRequest}"/> routed on <c>PATCH</c>. The generator bases the class on
/// <c>PatchEndpoint&lt;TRequest&gt;</c>, so the request body is bound for you — content-negotiated
/// through MVC's input formatters where they are registered, JSON otherwise.
/// </summary>
/// <remarks>
/// The default binding deserializes the whole body into <typeparamref name="TRequest"/>, which
/// cannot tell "field omitted" from "field set to its default" — a real JSON Merge Patch or JSON
/// Patch endpoint has to override <c>BindRequestAsync</c>, or model the absent case explicitly.
/// </remarks>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
public interface IPatchEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Patch;
}

/// <summary>
/// A <see cref="IEndpoint{TRequest, TResponse}"/> routed on <c>PATCH</c>: the body is bound for you
/// (see <see cref="IPatchEndpoint{TRequest}"/>, including its caveat about omitted fields) and the
/// response type reaches OpenAPI.
/// </summary>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
/// <typeparam name="TResponse">The success response body.</typeparam>
public interface IPatchEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Patch;
}
