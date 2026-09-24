namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// A raw <see cref="IEndpoint"/> routed on <c>DELETE</c>. Also the natural shape for a delete that
/// answers <c>204 No Content</c> with no body: there is no response type to declare.
/// </summary>
public interface IDeleteEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Delete;
}

/// <summary>
/// A <c>DELETE</c> with a typed response and no request body.
/// </summary>
/// <remarks>
/// <b>The single type argument is the response</b>, as for <see cref="IGetEndpoint{TResponse}"/>. A
/// DELETE that returns a body — the deleted resource, or a confirmation — is common; one that
/// <i>takes</i> a body is rare. Making the single argument the response keeps GET and DELETE
/// agreeing, and means the common case never needs a placeholder request. For a delete that
/// returns nothing, implement <see cref="IDeleteEndpoint"/> and declare no type at all.
/// </remarks>
/// <typeparam name="TResponse">The success response body.</typeparam>
public interface IDeleteEndpoint<TResponse> : IResponseEndpoint<TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Delete;
}

/// <summary>
/// A <c>DELETE</c> that takes a request body anyway, and declares a typed response. The body is bound
/// like a POST's.
/// </summary>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
/// <typeparam name="TResponse">The success response body.</typeparam>
public interface IDeleteEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Delete;
}
