using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// DELETE with typed request — must provide explicit BindRequestAsync.
/// DELETE /api/users/{id}
/// </summary>
[MemberOf<UsersApi>]
public partial class DeleteUser : IDeleteEndpoint<GetUserRequest>
{
    public static string Path => "/{id}";

    protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
    {
        // A hand-written binder says what a malformed request is; the library cannot guess. Left as a
        // bare int.Parse this is a 500, because a FormatException out of user code could equally be a
        // bug and the library will not swallow it.
        if (!int.TryParse(context.Request.RouteValues["id"]?.ToString(), out var id))
            throw new EndpointBindingException(StatusCodes.Status400BadRequest, "The id must be an integer.");

        return ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(id));
    }

    public override Task<IResult> HandleAsync(GetUserRequest request, CancellationToken ct)
    {
        return Task.FromResult(Results.NoContent());
    }
}
