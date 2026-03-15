using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// DELETE with typed request — must provide explicit BindRequestAsync.
/// DELETE /api/users/{id}
/// </summary>
public partial class DeleteUser : IDeleteEndpoint<GetUserRequest>, IMemberOf<UsersApi>
{
    public static string Path => "/{id}";

    protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
    {
        var id = int.Parse(context.Request.RouteValues["id"]!.ToString()!);
        return ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(id));
    }

    public override Task<IResult> HandleAsync(GetUserRequest request, CancellationToken ct)
    {
        return Task.FromResult(Results.NoContent());
    }
}
