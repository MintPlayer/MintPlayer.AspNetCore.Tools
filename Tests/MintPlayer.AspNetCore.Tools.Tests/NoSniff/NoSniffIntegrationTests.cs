using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using MintPlayer.AspNetCore.NoSniff;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.NoSniff;

/// <summary>
/// The cases that need a real server loop rather than a recording response feature.
/// </summary>
public class NoSniffIntegrationTests
{
    private static async Task<HttpResponseMessage> Send(RequestDelegate? terminal = null)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .Configure(app =>
                {
                    app.UseNoSniff();
                    app.Run(terminal ?? (context =>
                    {
                        context.Response.StatusCode = 200;
                        return Task.CompletedTask;
                    }));
                }))
            .StartAsync();

        return await host.GetTestClient().GetAsync("/");
    }

    [Fact]
    public async Task Response_ContainsNosniffHeader()
    {
        var response = await Send();

        Assert.True(response.Content.Headers.TryGetValues("X-Content-Type-Options", out var contentValues)
            || response.Headers.TryGetValues("X-Content-Type-Options", out contentValues));
        Assert.Equal("nosniff", Assert.Single(contentValues!));
    }

    /// <summary>
    /// A downstream handler's own value is overwritten, because the middleware's callback runs at
    /// header-flush time rather than on the way in.
    /// </summary>
    /// <remarks>
    /// This is the assertion that must live here rather than in the unit suite. The recording
    /// response feature fires callbacks in registration order, whereas a real server fires them in
    /// reverse; asserting "who wins" against the fake would be pinning the fake's behaviour, not
    /// the framework's.
    /// </remarks>
    [Fact]
    public async Task Response_DownstreamHeaderValue_IsOverwritten()
    {
        var response = await Send(context =>
        {
            context.Response.Headers.XContentTypeOptions = "sniff-me-please";
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var values = response.Content.Headers.TryGetValues("X-Content-Type-Options", out var v)
            ? v
            : response.Headers.GetValues("X-Content-Type-Options");

        Assert.Equal("nosniff", Assert.Single(values));
    }

    [Fact]
    public async Task UseNoSniff_ReturnsSameBuilder()
    {
        var builder = new ApplicationBuilder(serviceProvider: null!);

        Assert.Same(builder, builder.UseNoSniff());
    }
}
