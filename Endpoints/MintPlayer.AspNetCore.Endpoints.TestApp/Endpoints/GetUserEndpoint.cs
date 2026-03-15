using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// GET with typed request — must provide explicit BindRequestAsync.
/// GET /api/users/{id}
/// </summary>
public partial class GetUser : IGetEndpoint<GetUserRequest, UserResponse>, IMemberOf<UsersApi>
{
    public static string Path => "/{id}";

    protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
    {
        var id = int.Parse(context.Request.RouteValues["id"]!.ToString()!);
        return ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(id));
    }

    public override Task<IResult> HandleAsync(GetUserRequest request, CancellationToken ct)
    {
        // Simulate a lookup
        var user = new UserResponse(request.Id, "Alice", "alice@example.com");
        return Task.FromResult(Results.Ok(user));
    }
}
