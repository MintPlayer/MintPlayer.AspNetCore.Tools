namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Overrides the name of the generated MapEndpoints extension method for this assembly.
/// Without this attribute, the method name is derived from the assembly name
/// (e.g., assembly "MyApp.Api" -> "MapMyAppApiEndpoints").
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public class EndpointsMethodNameAttribute(string methodName) : Attribute
{
    /// <summary>
    /// The whole method name, not a stem: the <c>Map</c> prefix and <c>Endpoints</c> suffix of the
    /// default naming are not added for you.
    /// </summary>
    /// <remarks>
    /// It need not already be a legal identifier. Characters that cannot appear in one are dropped,
    /// a leading digit gets an underscore, and a name that survives as nothing usable falls back to
    /// the assembly-derived default — with the generator reporting MPEP006 so the adjustment is not
    /// silent.
    /// </remarks>
    public string MethodName { get; } = methodName;
}
