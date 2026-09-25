using System.ComponentModel;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Written by the source generator, not by hand: records one open-generic endpoint of this assembly,
/// so an application that references it can close it with
/// <see cref="EndpointTypeArgumentAttribute{TConstraint, TArgument}"/>.
/// </summary>
/// <remarks>
/// An application's generator sees a referenced assembly as metadata only, where a <c>Path</c>
/// property's body and the declaring syntax are gone. This record carries the compile-time facts the
/// application needs and cannot recover from metadata.
/// </remarks>
/// <param name="endpoint">The unbound endpoint type, such as <c>typeof(Passkeys&lt;&gt;)</c>.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class OpenEndpointAttribute(Type endpoint) : Attribute
{
    /// <summary>The unbound endpoint type.</summary>
    public Type Endpoint { get; } = endpoint;

    /// <summary>The group-relative route literal, or null when it is not a compile-time constant.</summary>
    public string? Path { get; set; }

    /// <summary>The HTTP methods, upper-cased, comma-separated; null when not known at compile time.</summary>
    public string? Methods { get; set; }

    /// <summary>True when the generator emitted a parameter binder into the endpoint's partial declaration.</summary>
    public bool HasBinder { get; set; }

    /// <summary>The record format; 1 for the generator that introduced it.</summary>
    public int Version { get; set; }
}

/// <summary>
/// Written by the source generator, not by hand: records one route group on the chain of an
/// open-generic endpoint of this assembly — its compile-time <c>Prefix</c>, which metadata does not
/// carry.
/// </summary>
/// <param name="group">The group type, unbound when it is nested in a generic type.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class OpenEndpointGroupAttribute(Type group) : Attribute
{
    /// <summary>The group type.</summary>
    public Type Group { get; } = group;

    /// <summary>The prefix literal, or null when it is not a compile-time constant.</summary>
    public string? Prefix { get; set; }
}
