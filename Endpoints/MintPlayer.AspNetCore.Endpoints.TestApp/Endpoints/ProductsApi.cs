namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// Route group for product endpoints — nested under ApiGroup, so resolves to /api/products.
/// </summary>
public class ProductsApi : IEndpointGroup, IMemberOf<ApiGroup>
{
    public static string Prefix => "/products";

    static void IEndpointGroup.Configure(RouteGroupBuilder group)
    {
        group.WithTags("Products");
    }
}
