namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// Simple raw GET endpoint — no typed request, no base class needed.
/// </summary>
public class HealthCheck : IGetEndpoint
{
    public static string Path => "/health";

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
}
