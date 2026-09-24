using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// GET /api/users/store-scope — shows that the <see cref="IUserStore"/> in an endpoint's constructor
/// is the request's scoped instance.
/// </summary>
/// <remarks>
/// It reports the instance its constructor received next to the one the request's own services
/// resolve. The two are the same object within a request and differ between requests, which is what
/// "scoped" means — and it only holds because the generated mapping creates the endpoint from
/// <c>HttpContext.RequestServices</c> on every request.
/// </remarks>
[MemberOf<UsersApi>]
public class UserStoreScope(IUserStore users) : IGetEndpoint
{
    public static string Path => "/store-scope";

    public Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var requestScoped = httpContext.RequestServices.GetRequiredService<IUserStore>();
        return Task.FromResult(Results.Ok(new
        {
            constructorInstance = users.InstanceId,
            requestInstance = requestScoped.InstanceId,
        }));
    }
}
