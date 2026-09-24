using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// GET /api/users/{id} — a typed response and no body, with the id bound from the route.
/// </summary>
/// <remarks>
/// This used to need a request record and a hand-written <c>BindRequestAsync</c> that parsed
/// <c>RouteValues["id"]</c> and threw on a bad value. Both are gone: <c>/api/users/abc</c> is a 400
/// naming the parameter, the value and the expected type, produced before the handler runs.
/// </remarks>
[MemberOf<UsersApi>]
public partial class GetUser : IGetEndpoint<UserResponse>
{
    public static string Path => "/{id}";

    [RouteParam] public int Id { get; set; }

    public override Task<IResult> HandleAsync(CancellationToken ct)
        => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice", "alice@example.com")));
}
