namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// Route group for user endpoints — nested under ApiGroup, so resolves to /api/users.
/// </summary>
[MemberOf<ApiGroup>]
public class UsersApi : IEndpointGroup
{
    public static string Prefix => "/users";

    static void IEndpointGroup.Configure(RouteGroupBuilder group)
    {
        group.WithTags("Users");
    }
}
