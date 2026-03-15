namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// Route group for user endpoints — nested under ApiGroup, so resolves to /api/users.
/// </summary>
public class UsersApi : IEndpointGroup, IMemberOf<ApiGroup>
{
    public static string Prefix => "/users";

    static void IEndpointGroup.Configure(RouteGroupBuilder group)
    {
        group.WithTags("Users");
    }
}
