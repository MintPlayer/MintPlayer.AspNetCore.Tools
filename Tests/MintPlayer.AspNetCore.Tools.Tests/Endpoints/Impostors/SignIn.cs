using Microsoft.AspNetCore.Http;
using MintPlayer.AspNetCore.Endpoints;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints.Impostors;

/// <summary>
/// Shares its simple name with <see cref="IsEndpointMappedTests.SignIn"/>, in another namespace: the
/// prototype's impostor probe for issue #38.
/// </summary>
public sealed class SignIn : IPostEndpoint
{
    public static string Path => "/impostor/sign-in";

    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
}
