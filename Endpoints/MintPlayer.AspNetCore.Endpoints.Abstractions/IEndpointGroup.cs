namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Defines a route group with a shared prefix and optional group-wide configuration.
/// Endpoints join a group by implementing IMemberOf&lt;TGroup&gt;.
/// </summary>
public interface IEndpointGroup
{
    /// <summary>The route prefix for all endpoints in this group (e.g., "/api/users").</summary>
    static abstract string Prefix { get; }

    /// <summary>
    /// Optional hook to configure the route group (auth, rate limiting, CORS, tags, etc.).
    /// Default implementation is a no-op.
    /// </summary>
    static virtual void Configure(RouteGroupBuilder group) { }
}

/// <summary>
/// Marker interface that associates an endpoint with an endpoint group.
/// The endpoint's Path becomes relative to the group's Prefix.
/// </summary>
public interface IMemberOf<TGroup> where TGroup : IEndpointGroup;
