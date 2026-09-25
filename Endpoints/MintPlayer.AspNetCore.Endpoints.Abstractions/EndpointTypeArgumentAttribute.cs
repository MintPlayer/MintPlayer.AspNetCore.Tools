namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Closes open-generic endpoints for this application's generated mapping: wherever an endpoint's
/// type parameter has <typeparamref name="TConstraint"/> among its constraint types, the source
/// generator uses <typeparamref name="TArgument"/> for it.
/// </summary>
/// <remarks>
/// <para>
/// A library cannot map an endpoint such as <c>Passkeys&lt;TUser&gt; where TUser : LibUser, new()</c>
/// itself, because it does not know the application's user type. The application does, so it writes
/// one line, and its <c>Map…Endpoints()</c> maps <c>Passkeys&lt;AppUser&gt;</c> — with its route,
/// group chain, typed link, contract and OpenAPI metadata — like any endpoint of its own:
/// <code>
/// [assembly: EndpointTypeArgument&lt;LibUser, AppUser&gt;]
/// </code>
/// The compiler checks <c>TArgument : TConstraint</c>, so a wrong argument is a build error. The
/// generator checks every other constraint (<c>new()</c>, <c>class</c>, further constraint types, …)
/// and reports an error on this attribute instead of emitting code that would not compile.
/// </para>
/// <para>
/// An endpoint is closed only when <b>every</b> type parameter, including those of the types it is
/// nested in, is bound. A closed endpoint is named <c>{Name}_{TypeArguments}</c>
/// (<c>Passkeys_AppUser</c>), so two closings of one endpoint never collide.
/// </para>
/// </remarks>
/// <typeparam name="TConstraint">The constraint type that identifies the type parameters to bind.</typeparam>
/// <typeparam name="TArgument">The type argument to use for them.</typeparam>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class EndpointTypeArgumentAttribute<TConstraint, TArgument> : Attribute
    where TArgument : TConstraint;

/// <summary>
/// Closes one open-generic endpoint explicitly, for a type parameter that has no constraint type to
/// key <see cref="EndpointTypeArgumentAttribute{TConstraint, TArgument}"/> on:
/// <c>[assembly: EndpointTypeArgument(typeof(Echo&lt;&gt;), typeof(string))]</c>.
/// </summary>
/// <remarks>
/// The type arguments are listed in declaration order, outermost containing type first. For that
/// endpoint this form wins over every constraint-keyed attribute. Each occurrence is one closing, so
/// the same endpoint can be closed more than once with different arguments.
/// </remarks>
/// <param name="openEndpoint">The unbound endpoint type, such as <c>typeof(Echo&lt;&gt;)</c>.</param>
/// <param name="typeArguments">One type argument per type parameter.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class EndpointTypeArgumentAttribute(Type openEndpoint, params Type[] typeArguments) : Attribute
{
    /// <summary>The unbound endpoint type.</summary>
    public Type OpenEndpoint { get; } = openEndpoint;

    /// <summary>The type arguments, outermost containing type's first.</summary>
    public Type[] TypeArguments { get; } = typeArguments;
}
