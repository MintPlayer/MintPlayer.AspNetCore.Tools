using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// GET /api/users/by-name/{name} — a <c>string</c> route value, so a name with <c>/</c>, <c>@</c> or
/// spaces in it shows how a link and a typed client escape a route value.
/// </summary>
[MemberOf<UsersApi>]
public partial class FindUserByName(IUserStore users) : IGetEndpoint<UserResponse>
{
    public static string Path => "/by-name/{name}";

    [RouteParam] public string Name { get; set; } = "";

    public override Task<IResult> HandleAsync(CancellationToken ct)
        => Task.FromResult(users.FindByName(Name) is { } user ? Results.Ok(user) : Results.NotFound());
}
