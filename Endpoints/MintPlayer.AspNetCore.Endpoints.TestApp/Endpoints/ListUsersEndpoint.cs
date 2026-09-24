namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// GET /api/users?page=2 — a raw endpoint with a query-string property that has a default.
/// </summary>
/// <remarks>
/// The initializer is the default: <see cref="Page"/> stays 1 unless the query supplies a value,
/// and <c>?page=abc</c> is a 400. Because the property has an initializer, the generated binder
/// assigns it only when a value is present.
/// </remarks>
[MemberOf<UsersApi>]
public partial class ListUsers : IGetEndpoint
{
    public static string Path => "/";

    [QueryParam] public int Page { get; set; } = 1;

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(new
        {
            page = Page,
            users = new[]
            {
                new { Id = 1, Name = "Alice", Email = "alice@example.com" },
                new { Id = 2, Name = "Bob", Email = "bob@example.com" },
            },
        }));
}
