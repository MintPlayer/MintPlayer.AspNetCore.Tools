namespace MintPlayer.AspNetCore.Endpoints;

[AttributeUsage(AttributeTargets.Class)]
public class EndpointNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
