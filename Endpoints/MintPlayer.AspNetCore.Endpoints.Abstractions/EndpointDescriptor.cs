namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// A description of one registered endpoint: its name, the fully resolved route it answers on, the
/// HTTP methods it handles and the class that handles them.
/// </summary>
/// <param name="Name">
/// The endpoint's identity in this list, and what a lookup keys on. Defaults to the handler's class
/// name; <see cref="EndpointDescriptorNameAttribute"/> overrides it. Nothing enforces uniqueness, so
/// two endpoints sharing a name are two entries, not one.
/// </param>
/// <param name="Path">
/// The route as a request has to spell it — group prefixes already composed in. Not the
/// group-relative <c>TEndpoint.Path</c>.
/// </param>
/// <param name="Methods">
/// The HTTP methods routed to <paramref name="HandlerType"/>, upper-case, as
/// <c>IEndpointBase.Methods</c> reported them. One entry describes all of them: a class handling
/// both GET and POST appears once, with two methods — not twice.
/// </param>
/// <param name="HandlerType">
/// The endpoint class itself, not an interface it implements. It is the type resolved from the
/// container per request, so it is also the handle to reach the endpoint's attributes or to
/// re-register it by hand through <c>MapEndpoint&lt;T&gt;</c>.
/// </param>
/// <remarks>
/// Equality is structural over <paramref name="Methods"/>. The compiler-generated record equality
/// would compare the list by reference, and every endpoint's <c>Methods</c> is a distinct instance,
/// so two descriptions of the same endpoint would never compare equal.
/// </remarks>
public sealed record EndpointDescriptor(string Name, string Path, IReadOnlyList<string> Methods, Type HandlerType)
{
    /// <summary>
    /// Compares two descriptors member by member, with <see cref="Methods"/> compared element by
    /// element rather than by reference. Replaces the record's generated equality — see the type's
    /// remarks for why.
    /// </summary>
    public bool Equals(EndpointDescriptor? other) =>
        other is not null &&
        Name == other.Name &&
        Path == other.Path &&
        HandlerType == other.HandlerType &&
        Methods.SequenceEqual(other.Methods);

    /// <summary>
    /// Hashes the same members <see cref="Equals(EndpointDescriptor)"/> compares, folding
    /// <see cref="Methods"/> in element by element so equal descriptors hash equally.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Path);
        hash.Add(HandlerType);
        foreach (var method in Methods)
            hash.Add(method);
        return hash.ToHashCode();
    }
}
