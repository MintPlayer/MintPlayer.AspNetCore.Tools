namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// Route group for user endpoints — all members get the /api/users prefix.
/// </summary>
public class UsersApi : IEndpointGroup
{
    public static string Prefix => "/api/users";

    static void IEndpointGroup.Configure(RouteGroupBuilder group)
    {
        group.WithTags("Users");
    }
}
