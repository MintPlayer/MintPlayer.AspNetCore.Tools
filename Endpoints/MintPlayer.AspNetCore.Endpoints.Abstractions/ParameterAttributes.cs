namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Binds this endpoint property from a route value before the handler runs.
/// </summary>
/// <remarks>
/// <code>
/// [MemberOf&lt;UsersApi&gt;]
/// public partial class GetUser : IGetEndpoint&lt;UserResponse&gt;
/// {
///     public static string Path =&gt; "/{id}";
///     [RouteParam] public int Id { get; set; }
///
///     public override Task&lt;IResult&gt; HandleAsync(CancellationToken ct) =&gt; …;
/// }
/// </code>
/// The route value is matched to the property by name, case-insensitively, unless
/// <see cref="Name"/> says otherwise. The property's type decides the conversion: <c>string</c>,
/// any enum, or any type implementing <c>IParsable&lt;TSelf&gt;</c> — <c>int</c>, <c>Guid</c>,
/// <c>DateOnly</c> and your own value types alike. A nullable property is optional; a property
/// with an initializer keeps that value when the route supplies none; anything else is required.
/// A value that is missing or does not parse is a <c>400</c> naming the parameter, the value and
/// the expected type, and the handler is never called.
/// <para>
/// <b>Why the property is on the endpoint and not on the request.</b> The request type is the body.
/// Putting route values on it as well would make <c>.Accepts&lt;TRequest&gt;()</c> advertise them
/// as body fields, and would leave a name that appears in both the route and the body resolved by
/// a precedence rule the declaration never mentions. Here the collision cannot be expressed.
/// </para>
/// <para>
/// <b>Named <c>RouteParam</c>, not <c>FromRoute</c>.</b> <c>Microsoft.AspNetCore.Mvc</c> already has
/// a <c>FromRouteAttribute</c>, and any file importing that namespace would get <c>CS0104</c>, an
/// ambiguous reference, with no import order that resolves it.
/// </para>
/// <para>
/// The endpoint instance is created per request and disposed afterwards, which is what makes a
/// settable property a safe place for a request's values. That is a load-bearing invariant of this
/// library, not an implementation detail.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class RouteParamAttribute : Attribute
{
    /// <summary>Binds from the route value with the same name as the property.</summary>
    public RouteParamAttribute() { }

    /// <summary>Binds from the route value <paramref name="name"/>.</summary>
    /// <param name="name">The <c>{token}</c> in the route template, without braces.</param>
    public RouteParamAttribute(string name) => Name = name;

    /// <summary>
    /// The route token to read, or <see langword="null"/> to use the property's name.
    /// </summary>
    public string? Name { get; }
}

/// <summary>
/// Binds this endpoint property from the query string before the handler runs.
/// </summary>
/// <remarks>
/// The same rules as <see cref="RouteParamAttribute"/>: matched by name case-insensitively unless
/// <see cref="Name"/> says otherwise; converted by the property's type; nullable is optional, an
/// initializer is a default, anything else is required. When the key repeats, the first value is
/// used. An empty value (<c>?page=</c>) counts as absent for every type except <c>string</c>, for
/// which <c>""</c> is a real value.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class QueryParamAttribute : Attribute
{
    /// <summary>Binds from the query key with the same name as the property.</summary>
    public QueryParamAttribute() { }

    /// <summary>Binds from the query key <paramref name="name"/>.</summary>
    /// <param name="name">The query-string key.</param>
    public QueryParamAttribute(string name) => Name = name;

    /// <summary>
    /// The query key to read, or <see langword="null"/> to use the property's name.
    /// </summary>
    public string? Name { get; }
}
