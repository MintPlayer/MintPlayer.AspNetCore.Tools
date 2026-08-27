namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Base class for endpoints whose request does not come from the body (GET, DELETE). The
/// counterpart of <see cref="BodyEndpoint{TRequest}"/>: it deliberately supplies <b>no</b>
/// <c>BindRequestAsync</c>, leaving the member abstract so the compiler requires each endpoint to
/// write its own binding.
/// </summary>
/// <remarks>
/// There is no sensible default to inherit. A body-less request is assembled from route values, the
/// query string, or headers, and only the endpoint knows which — a default that guessed would bind
/// silently wrong rather than fail. Read those off <c>HttpContext</c> in the override and throw
/// <see cref="EndpointBindingException"/> for input the endpoint cannot accept, so the failure
/// becomes the same 400 the body endpoints produce instead of an unhandled exception.
/// </remarks>
/// <typeparam name="TRequest">The bound request.</typeparam>
public abstract class NonBodyEndpoint<TRequest> : EndpointBase<TRequest>
{
    // BindRequestAsync remains abstract — the compiler forces an explicit implementation.
}
