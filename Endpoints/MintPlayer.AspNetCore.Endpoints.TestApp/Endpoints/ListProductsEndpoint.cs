namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// GET /api/products/ — list all products.
/// </summary>
public class ListProducts : IGetEndpoint, IMemberOf<ProductsApi>
{
    public static string Path => "/";

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(new[]
        {
            new { Id = 1, Name = "Widget", Price = 9.99 },
            new { Id = 2, Name = "Gadget", Price = 19.99 },
        }));
}
