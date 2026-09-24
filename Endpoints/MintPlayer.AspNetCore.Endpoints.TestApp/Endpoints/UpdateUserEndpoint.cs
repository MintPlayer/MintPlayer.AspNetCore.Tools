using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// PUT /api/users/{id} — a route value and a JSON body together.
/// </summary>
/// <remarks>
/// The route value binds to <see cref="Id"/> on the endpoint and the body to
/// <see cref="UpdateUserBody"/>, so the two cannot collide: the body has no <c>Id</c>. The body is
/// still content-negotiated through MVC's input formatters, which a design that merged route values
/// into the body type could not have kept. The route value is bound first, so
/// <c>PUT /api/users/abc</c> is rejected without the body ever being read.
/// </remarks>
[MemberOf<UsersApi>]
public partial class UpdateUser : IPutEndpoint<UpdateUserBody>
{
    public static string Path => "/{id}";

    [RouteParam] public int Id { get; set; }

    public override Task<IResult> HandleAsync(UpdateUserBody request, CancellationToken ct)
        => Task.FromResult(Results.Ok(new { id = Id, name = request.Name, email = request.Email }));
}
