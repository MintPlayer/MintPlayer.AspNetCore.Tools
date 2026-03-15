namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// Root API group — all sub-groups nest under /api.
/// </summary>
public class ApiGroup : IEndpointGroup
{
    public static string Prefix => "/api";
}
