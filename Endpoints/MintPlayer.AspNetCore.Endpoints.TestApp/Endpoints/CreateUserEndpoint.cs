using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// POST with typed request + response — content-negotiated body parsing via base class.
/// POST /api/users/
/// Generator emits: partial class CreateUser : PostEndpoint&lt;CreateUserRequest&gt;
/// Generator emits: .Produces&lt;CreateUserResponse&gt;(201)
/// </summary>
public partial class CreateUser : IPostEndpoint<CreateUserRequest, CreateUserResponse>, IMemberOf<UsersApi>
{
    public static string Path => "/";

    static int IEndpoint<CreateUserRequest, CreateUserResponse>.SuccessStatusCode => 201;

    public override Task<IResult> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        // Simulate creation
        var response = new CreateUserResponse(42, request.Name, request.Email);
        return Task.FromResult(Results.Created($"/api/users/42", response));
    }
}
