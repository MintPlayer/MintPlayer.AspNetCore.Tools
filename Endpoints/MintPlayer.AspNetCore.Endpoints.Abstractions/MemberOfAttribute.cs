namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Places an endpoint — or another group — inside <typeparamref name="TGroup"/>. The declaring
/// type's <c>Path</c> (or <c>Prefix</c>) is then relative to <typeparamref name="TGroup"/>'s
/// <see cref="IEndpointGroup.Prefix"/>, and <typeparamref name="TGroup"/>'s
/// <see cref="IEndpointGroup.Configure"/> applies to it.
/// </summary>
/// <remarks>
/// <b>Membership inherits, and the nearest declaration wins.</b> An endpoint deriving from a base
/// class that carries <c>[MemberOf&lt;UsersApi&gt;]</c> is in <c>UsersApi</c>; one that declares
/// its own <c>[MemberOf&lt;OtherApi&gt;]</c> moves to <c>OtherApi</c>. The source generator and
/// the reflection-based <c>MapEndpoint&lt;T&gt;()</c> both implement that rule, so the two
/// registration paths always agree on the route.
/// <para>
/// One membership per type. Declaring two is <c>CS0579</c> — the compiler enforces it even when
/// the two are on different partial declarations — because two groups would mean two routes and
/// no way to choose between them.
/// </para>
/// <para>
/// Why an attribute and not a marker interface: membership says where an endpoint <i>lives</i>, not
/// what it <i>is</i>, and it no longer sits in the type's interface list beside
/// <c>IPostEndpoint&lt;TRequest, TResponse&gt;</c>. The generic constraint keeps the one guarantee
/// the interface gave: naming a type that is not a group is <c>CS0311</c>, at the declaration.
/// </para>
/// </remarks>
/// <typeparam name="TGroup">The group to join. Nest groups by applying this to a group.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class MemberOfAttribute<TGroup> : Attribute
    where TGroup : IEndpointGroup;
