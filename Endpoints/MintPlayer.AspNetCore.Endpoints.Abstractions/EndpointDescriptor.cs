namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// A description of one registered endpoint: its name, the fully resolved route it answers on, the
/// HTTP methods it handles and the class that handles them.
/// </summary>
/// <param name="Path">
/// The route as a request has to spell it — group prefixes already composed in. Not the
/// group-relative <c>TEndpoint.Path</c>.
/// </param>
/// <remarks>
/// Equality is structural over <paramref name="Methods"/>. The compiler-generated record equality
/// would compare the list by reference, and every endpoint's <c>Methods</c> is a distinct instance,
/// so two descriptions of the same endpoint would never compare equal.
/// </remarks>
public sealed record EndpointDescriptor(string Name, string Path, IReadOnlyList<string> Methods, Type HandlerType)
{
    public bool Equals(EndpointDescriptor? other) =>
        other is not null &&
        Name == other.Name &&
        Path == other.Path &&
        HandlerType == other.HandlerType &&
        Methods.SequenceEqual(other.Methods);

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
