using Microsoft.AspNetCore.Mvc;
using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// DELETE /api/users/{id} — answers <c>204 No Content</c>, so there is no response type to declare.
/// </summary>
/// <remarks>
/// A raw endpoint with a bound property. A raw endpoint writes <c>HandleAsync(HttpContext)</c>
/// itself, so there is no library base class to bind <see cref="Id"/>; the generated partial
/// implements <c>IParameterBinder</c> and the mapper calls it first. That is the one reason this
/// class is <c>partial</c> — a raw endpoint with nothing to bind need not be.
/// <para>
/// A raw endpoint decides its own status, so the generator cannot document it; the attribute does.
/// Class-level attributes become endpoint metadata, so the OpenAPI document lists 204 — and, because
/// a success is now declared, not the default 200 it would otherwise assume.
/// </para>
/// </remarks>
[MemberOf<UsersApi>]
[ProducesResponseType(StatusCodes.Status204NoContent)]
public partial class DeleteUser(IUserStore users) : IDeleteEndpoint
{
    public static string Path => "/{id}";

    [RouteParam] public int Id { get; set; }

    // Idempotent: deleting a user that is not there is still 204.
    public Task<IResult> HandleAsync(HttpContext httpContext)
    {
        users.Remove(Id);
        return Task.FromResult(Results.NoContent());
    }
}
