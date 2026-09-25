namespace MintPlayer.AspNetCore.Endpoints.TestApp.Client;

/// <summary>
/// A small consumer of the generated <c>TestAppClient</c>, so this project's own code depends on it.
/// </summary>
/// <remarks>
/// That dependency is the point: remove or rename <c>GetUser</c> in the TestApp and this file stops
/// compiling (CS1061) — the server's change breaks the client's build rather than its first request.
/// </remarks>
internal sealed class UserDirectory(TestAppClient client)
{
    /// <summary>The user's display name, or null when there is no such user.</summary>
    public async Task<string?> DisplayNameAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await client.FindUserByNameAsync(name, cancellationToken);
            return user is null ? null : $"{user.Name} <{user.Email}>";
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>The user with the given id.</summary>
    public Task<MintPlayer.AspNetCore.Endpoints.TestApp.Models.UserResponse?> GetAsync(int id, CancellationToken cancellationToken = default)
        => client.GetUserAsync(id, cancellationToken);
}
