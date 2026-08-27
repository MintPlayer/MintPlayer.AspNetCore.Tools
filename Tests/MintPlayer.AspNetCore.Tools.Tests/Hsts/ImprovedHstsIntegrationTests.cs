using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MintPlayer.AspNetCore.Hsts;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Hsts;

/// <summary>
/// The three cases that genuinely need a real server loop.
/// </summary>
/// <remarks>
/// Everything else about this middleware is asserted as a fast unit test against a recording
/// response feature. What a fake cannot establish is <i>when</i> the header is written relative to
/// header flush — and that timing is the entire difference between this middleware and the
/// framework's built-in one. See <see cref="Response_HeaderSurvivesDownstreamHeaderClear"/>.
/// </remarks>
public class ImprovedHstsIntegrationTests
{
    private static async Task<HttpResponseMessage> Send(
        string url,
        Action<HstsOptions>? configureOptions = null,
        RequestDelegate? terminal = null)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.Configure<HstsOptions>(options =>
                    {
                        // The default exclusions contain "localhost", which is exactly the host
                        // TestServer uses — so without clearing them every test would silently
                        // exercise the excluded-host path instead of the header path.
                        options.ExcludedHosts.Clear();
                        configureOptions?.Invoke(options);
                    });
                })
                .Configure(app =>
                {
                    app.UseImprovedHsts();
                    app.Run(terminal ?? (context =>
                    {
                        context.Response.StatusCode = 200;
                        return Task.CompletedTask;
                    }));
                }))
            .StartAsync();

        return await host.GetTestClient().GetAsync(url);
    }

    [Fact]
    public async Task Response_OverHttps_ContainsStrictTransportSecurityHeader()
    {
        var response = await Send("https://localhost/");

        Assert.True(response.Headers.TryGetValues("Strict-Transport-Security", out var values));
        Assert.Equal("max-age=2592000", Assert.Single(values!));
    }

    [Fact]
    public async Task Response_OverPlainHttp_HasNoStrictTransportSecurityHeader()
    {
        var response = await Send("http://localhost/");

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    /// <summary>
    /// The header survives a downstream handler that clears every response header.
    /// </summary>
    /// <remarks>
    /// This is the library's reason to exist. The framework's <c>HstsMiddleware</c> writes the
    /// header immediately on the way in, so a later <c>Response.Headers.Clear()</c> wipes it; this
    /// one registers an <c>OnStarting</c> callback, which runs after the pipeline has finished and
    /// therefore wins. It cannot be demonstrated against a fake response feature, because the
    /// ordering being tested belongs to the server loop.
    /// </remarks>
    [Fact]
    public async Task Response_HeaderSurvivesDownstreamHeaderClear()
    {
        var response = await Send("https://localhost/", terminal: context =>
        {
            context.Response.Headers.Clear();
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        Assert.True(response.Headers.TryGetValues("Strict-Transport-Security", out var values));
        Assert.Equal("max-age=2592000", Assert.Single(values!));
    }

    [Fact]
    public async Task Response_ExcludedHost_HasNoHeader()
    {
        var response = await Send("https://localhost/", options => options.ExcludedHosts.Add("localhost"));

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task UseImprovedHsts_ResolvesMiddlewareFromTheContainer()
    {
        // ActivatorUtilities picks the greediest satisfiable constructor. If the two overloads ever
        // become ambiguous, or the options registration is missing, this is where it surfaces —
        // as a startup failure rather than a missing header.
        var response = await Send("https://localhost/");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
