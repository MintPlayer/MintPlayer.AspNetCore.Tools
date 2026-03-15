using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// PUT with typed request — body parsing from base class, no response type.
/// PUT /api/users/{id}
/// </summary>
public partial class UpdateUser : IPutEndpoint<UpdateUserRequest>, IMemberOf<UsersApi>
{
    public static string Path => "/{id}";

    public override Task<IResult> HandleAsync(UpdateUserRequest request, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { id = request.Id, name = request.Name, email = request.Email }));
    }
}
