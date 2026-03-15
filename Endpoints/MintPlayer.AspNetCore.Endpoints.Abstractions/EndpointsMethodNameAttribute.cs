namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Overrides the name of the generated MapEndpoints extension method for this assembly.
/// Without this attribute, the method name is derived from the assembly name
/// (e.g., assembly "MyApp.Api" -> "MapMyAppApiEndpoints").
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public class EndpointsMethodNameAttribute(string methodName) : Attribute
{
    public string MethodName { get; } = methodName;
}
