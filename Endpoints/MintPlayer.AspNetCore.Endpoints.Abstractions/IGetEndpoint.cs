namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// A raw <see cref="IEndpoint"/> routed on <c>GET</c>: the handler receives <c>HttpContext</c> and
/// writes whatever it likes. Implementing this saves declaring <c>Methods</c>; it adds nothing else.
/// </summary>
public interface IGetEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Get;
}

/// <summary>
/// A <c>GET</c> with a typed response and no request body — the common case.
/// </summary>
/// <remarks>
/// <b>The single type argument is the response, not a request.</b> A GET has no body to bind, so
/// there is nothing to name there; route and query values arrive through <c>[RouteParam]</c> and
/// <c>[QueryParam]</c> properties. This is the one place the endpoint ladder is asymmetric —
/// <c>IPostEndpoint&lt;T&gt;</c>'s single argument is the request body — and it mirrors HTTP rather
/// than inventing a placeholder request that would mean nothing. If your API does send a body on a
/// GET, use <see cref="IGetEndpoint{TRequest, TResponse}"/>.
/// </remarks>
/// <typeparam name="TResponse">The success response body.</typeparam>
public interface IGetEndpoint<TResponse> : IResponseEndpoint<TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Get;
}

/// <summary>
/// A <c>GET</c> that takes a request body anyway, and declares a typed response.
/// </summary>
/// <remarks>
/// Declaring a request on a body-less verb is the signal that this endpoint deviates from the
/// usual — some APIs (search endpoints, notably) accept a body on GET. It is bound exactly like a
/// POST body: content-negotiated through MVC's input formatters, JSON otherwise.
/// </remarks>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
/// <typeparam name="TResponse">The success response body.</typeparam>
public interface IGetEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Get;
}
