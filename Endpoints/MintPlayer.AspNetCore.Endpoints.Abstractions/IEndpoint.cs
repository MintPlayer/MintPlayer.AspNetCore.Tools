namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Level 1 of the endpoint ladder: raw <c>HttpContext</c>, no binding, no generated base class.
/// </summary>
/// <remarks>
/// Pick this when the request is not one object — streaming, server-sent events, manual content
/// negotiation, anything that needs the response written rather than returned. Everything above this
/// level ultimately implements it: the generated base class provides
/// <see cref="HandleAsync(HttpContext)"/> so that the typed handler is what an endpoint author
/// writes.
/// </remarks>
public interface IEndpoint : IEndpointBase
{
    /// <summary>
    /// Handles the request and returns the result to execute. Called once per request on an instance
    /// resolved from the container, which is disposed afterwards.
    /// </summary>
    /// <remarks>
    /// At levels 2 and 3 this member is already implemented by the endpoint's generated base class,
    /// which binds the request and forwards to the typed overload — implement
    /// <see cref="IEndpoint{TRequest}.HandleAsync(TRequest, CancellationToken)"/> instead of this
    /// one there, or the binding and the binding-failure response are bypassed.
    /// </remarks>
    Task<IResult> HandleAsync(HttpContext httpContext);
}

/// <summary>
/// Level 2 of the endpoint ladder: a typed request, so the handler receives
/// <typeparamref name="TRequest"/> and a cancellation token instead of <c>HttpContext</c>.
/// </summary>
/// <remarks>
/// Whether the binding is written for you depends on the verb, not on this level. A body verb
/// (POST/PUT/PATCH) gets a working default from <c>BodyEndpoint&lt;TRequest&gt;</c>; a body-less one
/// (GET/DELETE) inherits <c>BindRequestAsync</c> as abstract from
/// <c>NonBodyEndpoint&lt;TRequest&gt;</c> and must implement it. Either way a request that fails to
/// bind never reaches the handler.
/// <para>
/// Pick level 3 instead if the endpoint returns a body worth documenting; this level is right when
/// the response shape varies, or when nothing but a status code comes back.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The bound request.</typeparam>
public interface IEndpoint<TRequest> : IEndpoint
{
    /// <summary>
    /// Handles a successfully bound request. <paramref name="request"/> is never
    /// <see langword="null"/> — a request that binds to nothing is answered as a binding failure
    /// before this is called.
    /// </summary>
    /// <param name="request">The bound request.</param>
    /// <param name="cancellationToken">
    /// The request's <c>RequestAborted</c> token; it is cancelled when the client disconnects.
    /// </param>
    Task<IResult> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Level 3 of the endpoint ladder: level 2 plus a declared response type, which is what makes the
/// endpoint self-describing to OpenAPI/Swagger.
/// </summary>
/// <remarks>
/// <typeparamref name="TResponse"/> is not enforced at runtime — the handler still returns
/// <c>IResult</c>, and nothing checks that the body matches. Its job is metadata: the source
/// generator emits <c>.Produces&lt;TResponse&gt;(SuccessStatusCode)</c> for the mapped route.
/// </remarks>
/// <typeparam name="TRequest">The bound request.</typeparam>
/// <typeparam name="TResponse">The success response body, as documented to OpenAPI.</typeparam>
public interface IEndpoint<TRequest, TResponse> : IEndpoint<TRequest>
{
    /// <summary>
    /// The status code paired with <typeparamref name="TResponse"/> in the generated
    /// <c>.Produces&lt;TResponse&gt;(statusCode)</c> call. Defaults to 200.
    /// </summary>
    /// <remarks>
    /// Documentation only: it does not set the response status, so an endpoint returning
    /// <c>Results.Created(...)</c> has to override this with 201 for the two to agree. Override it on
    /// the endpoint class, not on an interface — a static virtual member is resolved through the
    /// concrete type.
    /// </remarks>
    static virtual int SuccessStatusCode => 200;
}
