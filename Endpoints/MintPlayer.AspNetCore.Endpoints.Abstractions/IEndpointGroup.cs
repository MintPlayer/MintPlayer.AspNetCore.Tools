namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Defines a route group: a shared route prefix plus one place to configure everything mapped under
/// it. Endpoints join with <see cref="MemberOfAttribute{TGroup}"/>; a group joins another group
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
    /// <remarks>
    /// Called once, when the routes are mapped. <paramref name="services"/> is the application's
    /// <b>root</b> provider: read options and configuration from it, so a convention can depend on
    /// them. No request exists yet, so a scoped service cannot be resolved from it.
    /// <para>
    /// <see cref="IsEnabled"/> decides whether the group <b>exists</b>; this hook decides what it
    /// carries. To drop a convention when an option is off, test the option here — returning
    /// <see langword="false"/> from <see cref="IsEnabled"/> removes every route of the group.
    /// </para>
    /// <para>
    /// Since 11.4 this is the only <c>Configure</c> hook: a one-argument
    /// <c>Configure(RouteGroupBuilder)</c> is no longer called, and MPEP035 reports it.
    /// </para>
    /// </remarks>
    /// <param name="group">The group's route group builder.</param>
    /// <param name="services">The application's root service provider.</param>
    static virtual void Configure(RouteGroupBuilder group, IServiceProvider services) { }

    /// <summary>
    /// Whether this group, its endpoints and every group nested in it are mapped at all. Evaluated
    /// once, when the routes are mapped, against the application's services — so a library can map
    /// an optional cluster of endpoints only when its options enable it. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Honoured by the generated <c>Map…Endpoints()</c> and by <c>MapEndpoint&lt;T&gt;()</c>. The
    /// generated <c>Endpoints</c> descriptor list is static and still lists what is declared.
    /// </remarks>
    /// <param name="services">The application's root service provider.</param>
    static virtual bool IsEnabled(IServiceProvider services) => true;
}
