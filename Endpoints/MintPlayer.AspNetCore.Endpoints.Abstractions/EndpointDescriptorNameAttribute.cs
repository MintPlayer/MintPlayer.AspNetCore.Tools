namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Overrides the <see cref="EndpointDescriptor.Name"/> the source generator records for this
/// endpoint. Without it the class name is used.
/// </summary>
/// <remarks>
/// Deliberately <b>not</b> called <c>EndpointNameAttribute</c>: that simple name is taken by
/// <c>Microsoft.AspNetCore.Routing.EndpointNameAttribute</c>, whose namespace is an implicit global
/// using in every <c>Microsoft.NET.Sdk.Web</c> project — so a consumer who also imports
/// <c>MintPlayer.AspNetCore.Endpoints</c> could not write the short form without a CS0104
/// ambiguity error.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EndpointDescriptorNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
