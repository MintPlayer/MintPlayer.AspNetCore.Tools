namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Base contract for all endpoints. Provides route metadata as static abstract members.
/// </summary>
public interface IEndpointBase
{
    /// <summary>The route pattern (e.g., "/api/users/{id}").</summary>
    static abstract string Path { get; }

    /// <summary>
    /// The HTTP methods this endpoint handles (e.g., ["GET"], ["POST"], ["GET", "HEAD"]).
    /// Convenience interfaces (IGetEndpoint, IPostEndpoint, etc.) provide this automatically.
    /// </summary>
    static abstract IEnumerable<string> Methods { get; }

    /// <summary>
    /// Optional hook to configure the route handler (auth, caching, OpenAPI metadata, etc.).
    /// Default implementation is a no-op.
    /// </summary>
    static virtual void Configure(RouteHandlerBuilder builder) { }
}
