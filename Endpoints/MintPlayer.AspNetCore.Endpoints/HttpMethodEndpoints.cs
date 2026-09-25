namespace MintPlayer.AspNetCore.Endpoints;

// Concrete base classes per HTTP method. The source generator picks one of these as the base of the
// partial endpoint class it emits, from the verb interface the endpoint implements. They exist only
// so that choice has a name per verb — the behaviour lives in BodyEndpoint.

/// <summary>
/// Base for a typed <c>POST</c> endpoint. A <see cref="BodyEndpoint{TRequest}"/>: the request body
/// is bound for you, so <c>BindRequestAsync</c> only needs overriding for binding the default cannot
/// express (form data, several sources at once).
/// </summary>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
public abstract class PostEndpoint<TRequest> : BodyEndpoint<TRequest>;

/// <summary>
/// Base for a typed <c>PUT</c> endpoint. A <see cref="BodyEndpoint{TRequest}"/>: the request body is
/// bound for you, so <c>BindRequestAsync</c> only needs overriding for binding the default cannot
/// express.
/// </summary>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
public abstract class PutEndpoint<TRequest> : BodyEndpoint<TRequest>;

/// <summary>
/// Base for a typed <c>PATCH</c> endpoint. A <see cref="BodyEndpoint{TRequest}"/>: the request body
/// is bound for you — as a whole <typeparamref name="TRequest"/>, which cannot distinguish an
/// omitted field from a defaulted one, so a merge-patch endpoint overrides
/// <c>BindRequestAsync</c>.
/// </summary>
/// <typeparam name="TRequest">The type the request body is deserialized into.</typeparam>
public abstract class PatchEndpoint<TRequest> : BodyEndpoint<TRequest>;

/// <summary>
/// Base for a <c>GET</c> endpoint that declares a request type — <c>IGetEndpoint&lt;TRequest, TResponse&gt;</c>.
/// Declaring a request on a verb that normally has no body is the signal that <i>this</i> endpoint
/// takes one anyway (some APIs send a body on GET), so it gets the same content-negotiated body
/// binding as a POST. Route and query values arrive through <c>[RouteParam]</c>/<c>[QueryParam]</c>
/// properties, not through the request.
/// </summary>
/// <typeparam name="TRequest">The bound request.</typeparam>
public abstract class GetEndpoint<TRequest> : BodyEndpoint<TRequest>;

/// <summary>
/// Base for a <c>DELETE</c> endpoint that declares a request type — <c>IDeleteEndpoint&lt;TRequest, TResponse&gt;</c>.
/// As for <see cref="GetEndpoint{TRequest}"/>: declaring a request means the endpoint takes a body, and
/// it is bound like any POST.
/// </summary>
/// <typeparam name="TRequest">The bound request.</typeparam>
public abstract class DeleteEndpoint<TRequest> : BodyEndpoint<TRequest>;
