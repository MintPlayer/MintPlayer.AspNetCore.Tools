namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Base contract for all endpoints. Provides route metadata as static abstract members.
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
    static virtual void Configure(RouteHandlerBuilder builder) { }
}
