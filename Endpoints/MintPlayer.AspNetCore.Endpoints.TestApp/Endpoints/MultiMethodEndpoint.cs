namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// Multi-method endpoint — handles both OPTIONS and HEAD.
/// No typed request, no base class needed.
/// </summary>
public class PreflightEndpoint : IEndpoint
{
    public static string Path => "/api/{**path}";
    public static IEnumerable<string> Methods => ["OPTIONS", "HEAD"];

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok());
}
