using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// POST with typed request + response — content-negotiated body parsing via base class.
/// POST /api/users/
/// Generator emits: partial class CreateUser : PostEndpoint&lt;CreateUserRequest&gt;
/// Generator emits: .Produces&lt;CreateUserResponse&gt;(201)
/// </summary>
[MemberOf<UsersApi>]
public partial class CreateUser(IUserStore users) : IPostEndpoint<CreateUserRequest, CreateUserResponse>
{
    public static string Path => "/";

    static int IEndpoint<CreateUserRequest, CreateUserResponse>.SuccessStatusCode => 201;

    public override Task<IResult> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        // Stored through the scoped IUserStore the constructor received.
        var user = users.Add(request.Name, request.Email);
        var response = new CreateUserResponse(user.Id, user.Name, user.Email);
        // A typed link, not "/api/users/42": renaming the group prefix or the route token now
        // breaks this line at compile time instead of silently pointing the Location header at
        // nothing.
        return Task.FromResult(Results.Created(Routes.Api.Users.GetUser(id: response.Id), response));
    }
}
