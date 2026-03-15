namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Level 1: Raw endpoint — full control over HttpContext.
/// </summary>
public interface IEndpoint : IEndpointBase
{
    Task<IResult> HandleAsync(HttpContext httpContext);
}

/// <summary>
/// Level 2: Typed request — automatic request binding via the abstract base class.
/// </summary>
public interface IEndpoint<TRequest> : IEndpoint
{
    Task<IResult> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Level 3: Typed request and response — full type information for OpenAPI/Swagger.
/// TResponse is used by the source generator to emit .Produces&lt;TResponse&gt;(statusCode).
/// </summary>
public interface IEndpoint<TRequest, TResponse> : IEndpoint<TRequest>
{
    /// <summary>
    /// The HTTP status code for successful responses.
    /// Used by the source generator for .Produces&lt;TResponse&gt;(statusCode).
    /// Default: 200 (OK).
    /// </summary>
    static virtual int SuccessStatusCode => 200;
}
