namespace MintPlayer.AspNetCore.Endpoints.TestApp.Models;

public record CreateUserRequest(string Name, string Email);

public record CreateUserResponse(int Id, string Name, string Email);

public record UserResponse(int Id, string Name, string Email);

/// <summary>
/// The body of <c>PUT /api/users/{id}</c> — only what the body carries.
/// </summary>
/// <remarks>
/// It used to be <c>UpdateUserRequest(int Id, string Name, string Email)</c>, and because nothing
/// bound the route, <c>Id</c> was read from the <i>body</i>: <c>PUT /api/users/1</c> with
/// <c>{"id":999}</c> acted on 999. The route value now lives on the endpoint, and the body type has
/// no <c>Id</c> left to disagree with it.
/// </remarks>
public record UpdateUserBody(string Name, string Email);
