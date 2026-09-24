using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// POST with typed request + response — content-negotiated body parsing via base class.
/// POST /api/users/
/// Generator emits: partial class CreateUser : PostEndpoint&lt;CreateUserRequest&gt;
/// Generator emits: .Produces&lt;CreateUserResponse&gt;(201)
/// </summary>
[MemberOf<UsersApi>]
public partial class CreateUser : IPostEndpoint<CreateUserRequest, CreateUserResponse>
{
    public static string Path => "/";

    static int IEndpoint<CreateUserRequest, CreateUserResponse>.SuccessStatusCode => 201;

    public override Task<IResult> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        // Simulate creation
        var response = new CreateUserResponse(42, request.Name, request.Email);
        // A typed link, not "/api/users/42": renaming the group prefix or the route token now
        // breaks this line at compile time instead of silently pointing the Location header at
        // nothing.
        return Task.FromResult(Results.Created(Routes.Api.Users.GetUser(id: response.Id), response));
    }
}
