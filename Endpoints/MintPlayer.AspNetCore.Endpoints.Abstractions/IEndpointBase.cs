namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Base contract for all endpoints: static route metadata (<see cref="Path"/>, <see cref="Methods"/>)
/// and optional map-time hooks (<see cref="GetPath"/>, <see cref="IsEnabled"/>, <see cref="Configure"/>),
/// each evaluated once when the routes are mapped.
/// </summary>
public interface IEndpointBase
{
    /// <summary>
    /// The route pattern this endpoint answers on (e.g. <c>"/api/users/{id}"</c>), <b>relative to
    /// the prefix of the group it belongs to</b>. An endpoint declaring
    /// <c>[MemberOf&lt;UsersApi&gt;]</c> where the group's prefix is <c>"/api/users"</c> writes
    /// <c>"/{id}"</c> here, not the full route.
    /// </summary>
    /// <remarks>
    /// The composed route — what a request actually has to spell — is reported by
    /// <see cref="EndpointDescriptor.Path"/>, not by this member.
    /// </remarks>
    static abstract string Path { get; }

    /// <summary>
    /// Optional hook that chooses the route at map time, from the application's services — a path
    /// read from options or configuration. <see langword="null"/>, the default, means
    /// <see cref="Path"/>.
    /// </summary>
    /// <remarks>
    /// Evaluated once, when the routes are mapped, by the generated <c>Map…Endpoints()</c> and by
    /// <c>MapEndpoint&lt;T&gt;()</c>, both of which map <c>GetPath(services) ?? Path</c>. Like
    /// <see cref="Path"/>, the result is relative to the endpoint's group prefix. A configuration
    /// change at run time does not move the route; it takes a restart.
    /// <list type="bullet">
    /// <item>
    /// <b>Null always means "use <see cref="Path"/>"</b> — whether this is not overridden or an override
    /// returns null. It never means "not mapped": that is <see cref="IsEnabled"/>'s job.
    /// </item>
    /// <item>
    /// <b>Only the root provider is available.</b> No request exists yet, so a scoped service cannot
    /// be resolved from it.
    /// </item>
    /// <item>
    /// <b>The path must keep the route parameters of <see cref="Path"/></b>, by name (case-insensitive):
    /// bound properties and every build-time route check use <see cref="Path"/>, which stays the default.
    /// Literal segments, constraints and defaults may differ. Mapping checks this at startup and throws
    /// an <see cref="InvalidOperationException"/> naming both patterns when the names differ.
    /// </item>
    /// <item>
    /// <b>What is left out.</b> The generator cannot know the configured route, so an endpoint that
    /// overrides this gets no typed link and no client contract, its OpenAPI route parameters come
    /// from its bound properties only, and its descriptor reports <see cref="Path"/> with
    /// <c>EndpointDescriptor.IsPathConfigurable</c> set (MPEP034 says so at build time).
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="services">The application's root service provider.</param>
    /// <returns>The route to map, or <see langword="null"/> for <see cref="Path"/>.</returns>
    static virtual string? GetPath(IServiceProvider services) => null;

    /// <summary>
    /// Whether this endpoint is mapped at all. Evaluated once, when the routes are mapped, against the
    /// application's services — so one endpoint can be switched on or off by configuration without
    /// switching off its whole group. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// A group is enabled or disabled as a whole (<c>IEndpointGroup.IsEnabled</c>), so a condition that
    /// applies to one endpoint belongs here. It is checked after the group chain: an endpoint in a
    /// disabled group is never asked. When it returns <see langword="false"/>, nothing else happens for
    /// this endpoint — no route, and neither <see cref="GetPath"/> nor <see cref="Configure"/> is called.
    /// <para>
    /// Honoured by the generated <c>Map…Endpoints()</c> and by <c>MapEndpoint&lt;T&gt;()</c>, which then
    /// returns without mapping anything, as for a disabled group. Only the root provider is available,
    /// and endpoints are fixed at startup: a configuration change at run time maps or unmaps nothing,
    /// it takes a restart. The generated <c>Endpoints</c> descriptor list is static and still lists the
    /// endpoint; <c>IsEndpointMapped&lt;T&gt;</c> answers what is actually mapped.
    /// </para>
    /// </remarks>
    /// <param name="services">The application's root service provider.</param>
    static virtual bool IsEnabled(IServiceProvider services) => true;

    /// <summary>
    /// The HTTP methods this endpoint handles (e.g., ["GET"], ["POST"], ["GET", "HEAD"]).
    /// Convenience interfaces (IGetEndpoint, IPostEndpoint, etc.) provide this automatically.
    /// </summary>
    /// <remarks>
    /// A class that implements a convenience interface may still declare
    /// <c>public static IEnumerable&lt;string&gt; Methods</c> of its own; the class member is more
    /// specific than the interface's, so it wins. A class implementing two convenience interfaces
    /// <b>must</b> do so — otherwise neither verb is most specific and the compiler reports CS8705.
    /// </remarks>
    static abstract IEnumerable<string> Methods { get; }

    /// <summary>
    /// Optional hook to configure the route handler (auth, caching, OpenAPI metadata, etc.).
    /// Default implementation is a no-op.
    /// </summary>
    /// <remarks>
    /// Called once, when the routes are mapped, by the generated <c>Map…Endpoints()</c> and by
    /// <c>MapEndpoint&lt;T&gt;()</c>. <paramref name="services"/> is the application's <b>root</b>
    /// provider: read options and configuration from it, so a convention can depend on them (a CORS
    /// policy only when one is configured, say). No request exists yet, so a scoped service cannot be
    /// resolved from it. Whether the endpoint is mapped at all is decided earlier, by its group's and
    /// its own <see cref="IsEnabled"/>; <c>Configure</c> only decides what a mapped endpoint carries.
    /// <para>
    /// Since 11.4 this is the only <c>Configure</c> hook: a one-argument
    /// <c>Configure(RouteHandlerBuilder)</c> is no longer called, and MPEP035 reports it.
    /// </para>
    /// </remarks>
    /// <param name="builder">The endpoint's route handler builder.</param>
    /// <param name="services">The application's root service provider.</param>
    static virtual void Configure(RouteHandlerBuilder builder, IServiceProvider services) { }
}
