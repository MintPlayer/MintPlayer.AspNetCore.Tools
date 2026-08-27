namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Defines a route group: a shared route prefix plus one place to configure everything mapped under
/// it. Endpoints join by implementing <see cref="IMemberOf{TGroup}"/>; a group joins another group
/// the same way, so groups nest.
/// </summary>
/// <remarks>
/// The group type is never instantiated — <see cref="Prefix"/> and <see cref="Configure"/> are
/// static, and a group needs no members beyond them. Nesting composes outermost first, so a group
/// whose prefix is <c>"/v1"</c> containing one whose prefix is <c>"/users"</c> puts an endpoint with
/// <c>Path = "/{id}"</c> at <c>/v1/users/{id}</c>, and the outer group's <see cref="Configure"/>
/// runs before the inner one's.
/// </remarks>
public interface IEndpointGroup
{
    /// <summary>
    /// The route prefix contributed by this group (e.g. <c>"/api/users"</c>). It is prepended to the
    /// <c>Path</c> of each member endpoint — and, when this group is itself a member of another,
    /// nested under that group's prefix rather than replacing it.
    /// </summary>
    static abstract string Prefix { get; }

    /// <summary>
    /// Optional hook to configure the route group (auth, rate limiting, CORS, tags, etc.).
    /// Default implementation is a no-op.
    /// </summary>
    static virtual void Configure(RouteGroupBuilder group) { }
}

/// <summary>
/// Places an endpoint — or another group — inside <typeparamref name="TGroup"/>. The implementer's
/// <c>Path</c> (or <c>Prefix</c>) is then relative to <typeparamref name="TGroup"/>'s
/// <see cref="IEndpointGroup.Prefix"/>, and <typeparamref name="TGroup"/>'s
/// <see cref="IEndpointGroup.Configure"/> applies to it.
/// </summary>
/// <remarks>
/// A pure marker: no members, nothing to implement. Exactly one may be declared per type — two
/// memberships have no single resolvable route, so registration throws rather than pick one, as does
/// a membership cycle.
/// </remarks>
/// <typeparam name="TGroup">The group to join. Nest by having a group declare this too.</typeparam>
public interface IMemberOf<TGroup> where TGroup : IEndpointGroup;
