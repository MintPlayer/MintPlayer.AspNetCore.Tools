namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// Raw GET inside a group — GET /api/users/
/// </summary>
public class ListUsers : IGetEndpoint, IMemberOf<UsersApi>
{
    public static string Path => "/";

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(new[]
        {
            new { Id = 1, Name = "Alice", Email = "alice@example.com" },
            new { Id = 2, Name = "Bob", Email = "bob@example.com" },
        }));
}
