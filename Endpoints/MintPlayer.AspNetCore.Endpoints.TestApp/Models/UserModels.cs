using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Models;

/// <summary>
/// The body of <c>POST /api/users</c>, validated before the handler runs.
/// </summary>
/// <remarks>
/// <c>[ValidatableType]</c> has to be written here by hand: the validation generator only sees
/// hand-written code, never the mapping this library generates. Together with the single
/// <c>AddValidation()</c> call inside <c>AddTestAppValidation()</c> (see <c>TestAppValidation.cs</c>)
/// it makes an invalid body a 400 problem response.
/// The attributes sit on the positional parameters; the validation generator honours them there
/// just as it does with a <c>property:</c> target.
/// </remarks>
[ValidatableType]
public record CreateUserRequest([Required] string Name, [Required, EmailAddress] string Email);

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
