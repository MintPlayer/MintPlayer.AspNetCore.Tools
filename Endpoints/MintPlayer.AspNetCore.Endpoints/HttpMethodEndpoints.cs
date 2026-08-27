namespace MintPlayer.AspNetCore.Endpoints;

// Concrete base classes per HTTP method. The source generator picks one of these as the base of the
// partial endpoint class it emits, from the verb interface the endpoint implements. They exist only
// so that choice has a name per verb — the behaviour lives in BodyEndpoint / NonBodyEndpoint.

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
/// Base for a typed <c>GET</c> endpoint. A <see cref="NonBodyEndpoint{TRequest}"/>: there is no body
/// to bind, so <c>BindRequestAsync</c> stays abstract and the endpoint must build
/// <typeparamref name="TRequest"/> from the route, query string, or headers itself.
/// </summary>
/// <typeparam name="TRequest">The bound request.</typeparam>
public abstract class GetEndpoint<TRequest> : NonBodyEndpoint<TRequest>;

/// <summary>
/// Base for a typed <c>DELETE</c> endpoint. A <see cref="NonBodyEndpoint{TRequest}"/>: treated as
/// body-less, so <c>BindRequestAsync</c> stays abstract and the endpoint must build
/// <typeparamref name="TRequest"/> from the route, query string, or headers itself.
/// </summary>
/// <typeparam name="TRequest">The bound request.</typeparam>
public abstract class DeleteEndpoint<TRequest> : NonBodyEndpoint<TRequest>;
