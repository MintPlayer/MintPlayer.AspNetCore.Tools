namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// DELETE /api/users/{id} — answers <c>204 No Content</c>, so there is no response type to declare.
/// </summary>
/// <remarks>
/// A raw endpoint with a bound property. A raw endpoint writes <c>HandleAsync(HttpContext)</c>
/// itself, so there is no library base class to bind <see cref="Id"/>; the generated partial
/// implements <c>IParameterBinder</c> and the mapper calls it first. That is the one reason this
/// class is <c>partial</c> — a raw endpoint with nothing to bind need not be.
/// </remarks>
[MemberOf<UsersApi>]
public partial class DeleteUser : IDeleteEndpoint
{
    public static string Path => "/{id}";

    [RouteParam] public int Id { get; set; }

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.NoContent());
}
