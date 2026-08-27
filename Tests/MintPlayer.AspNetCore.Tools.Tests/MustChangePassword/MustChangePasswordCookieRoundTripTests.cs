using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MintPlayer.AspNetCore.MustChangePassword.Abstractions;
using MintPlayer.AspNetCore.MustChangePassword.Constants;
using MintPlayer.AspNetCore.MustChangePassword.Exceptions;
using MintPlayer.AspNetCore.MustChangePassword.Extensions;
using MintPlayer.AspNetCore.Tools.Tests.MustChangePassword.Fakes;
using System.Net;
using System.Security.Claims;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword;

/// <summary>
/// The one group in this folder that runs a real server.
/// </summary>
/// <remarks>
/// <para>
/// Everything else replaces <c>IAuthenticationService</c>, which is fast and precise but proves
/// nothing about the cookie. Here the real <c>CookieAuthenticationHandler</c>, the real data
/// protector and a real <c>Set-Cookie</c>/<c>Cookie</c> exchange are in play, which is what makes
/// two claims checkable that a fake cannot support: that the plaintext password really does travel
/// to the client and back (D-M23), and that a successful change really does leave the ticket usable
/// (D-M26).
/// </para>
/// <para>
/// An inline <c>UseTestServer()</c> pipeline is used rather than <c>WebApplicationFactory</c>
/// (PRD R3.2): no entry-point assembly and no content-root probing, the latter being a genuine
/// Windows-vs-Linux hazard.
/// </para>
/// </remarks>
public class MustChangePasswordCookieRoundTripTests
{
    private static string Scheme => MustChangePasswordConstants.MustChangePasswordScheme;

    private const string InitialPassword = MustChangePasswordTestHarness.InitialPassword;
    private const string NewPassword = MustChangePasswordTestHarness.NewPassword;

    /// <summary>
    /// Builds the server. A generic <see cref="HostBuilder"/> hosting a web host is used rather than
    /// the bare <c>WebHostBuilder</c> the PRD sketches — <c>WebHostBuilder</c>/<c>IWebHost</c> are
    /// deprecated on .NET 10 (ASPDEPR004/008) and would put a warning on every test in this file.
    /// The property that matters is unchanged: an inline pipeline with no entry-point assembly and
    /// no content-root probing.
    /// </summary>
    private static IHost BuildHost() => new HostBuilder()
        .ConfigureWebHost(webHost => webHost
        .UseTestServer()
        .ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddRouting();
            services.AddSingleton<TestUserData>();
            services.AddIdentityCore<TestUser>().AddUserStore<TestUserStore>();
            services.AddHttpContextAccessor();
            services.AddMustChangePassword<TestUser, string>();
            services.AddAuthentication(Scheme).AddMustChangePasswordUserIdCookie();

            // Keys in memory only: the default key ring writes to the user profile, which is not
            // something a test should touch on either platform.
            services.AddSingleton<IDataProtectionProvider, EphemeralDataProtectionProvider>();
        })
        .Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/seed", async context =>
                {
                    var users = context.RequestServices.GetRequiredService<UserManager<TestUser>>();
                    var user = new TestUser { UserName = "alice", Email = "alice@example.com" };
                    var result = await users.CreateAsync(user, InitialPassword);
                    context.Response.StatusCode = result.Succeeded ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync(user.Id);
                });

                endpoints.MapGet("/signin", async context =>
                {
                    var users = context.RequestServices.GetRequiredService<UserManager<TestUser>>();
                    var service = context.RequestServices.GetRequiredService<IMustChangePasswordService<TestUser, string>>();
                    var user = await users.FindByNameAsync("ALICE");

                    try
                    {
                        await service.ChangePasswordSignInAsync(user!, InitialPassword);
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    }
                    catch (MustChangePasswordException)
                    {
                        // The documented signal: the ticket is stored and the caller must redirect.
                        context.Response.StatusCode = StatusCodes.Status200OK;
                    }
                });

                endpoints.MapGet("/peek", async context =>
                {
                    var result = await context.AuthenticateAsync(Scheme);
                    if (!result.Succeeded)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }

                    await context.Response.WriteAsync(result.Principal!.FindFirstValue("OldPassword") ?? string.Empty);
                });

                endpoints.MapGet("/perform", async context =>
                {
                    var service = context.RequestServices.GetRequiredService<IMustChangePasswordService<TestUser, string>>();
                    try
                    {
                        await service.PerformChangePasswordAsync(
                            context.Request.Query["new"].ToString(),
                            context.Request.Query["confirm"].ToString());
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    }
                    catch (Exception)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    }
                });

                endpoints.MapGet("/check", async context =>
                {
                    var users = context.RequestServices.GetRequiredService<UserManager<TestUser>>();
                    var user = await users.FindByNameAsync("ALICE");
                    var ok = await users.CheckPasswordAsync(user!, context.Request.Query["password"].ToString());
                    await context.Response.WriteAsync(ok ? "yes" : "no");
                });
            });
        }))
        .Build();

    /// <summary>
    /// Pulls the change-password <c>Set-Cookie</c> out of a response, or returns null.
    /// </summary>
    /// <remarks>
    /// Done by hand rather than with a <c>CookieContainer</c>: the tests need to see the raw
    /// attributes (and the deletion cookie) as well as replay the value.
    /// </remarks>
    private static string? FindSetCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        return values.FirstOrDefault(v => v.StartsWith(Scheme + "=", StringComparison.Ordinal));
    }

    private static string CookieHeader(string setCookie) => setCookie.Split(';')[0];

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task SignIn_IssuesACookieNamedAfterTheScheme()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        using var seed = await GetAsync(client, "/seed");
        Assert.Equal(HttpStatusCode.OK, seed.StatusCode);

        using var signIn = await GetAsync(client, "/signin");

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        var setCookie = FindSetCookie(signIn);
        Assert.NotNull(setCookie);
        Assert.StartsWith("Identity.ChangePassword=", setCookie!, StringComparison.Ordinal);

        await host.StopAsync();
    }

    /// <summary>
    /// D-M33: over plain HTTP the credential-bearing cookie is issued without <c>secure</c>, because
    /// the library leaves <c>SecurePolicy</c> at <c>SameAsRequest</c>. It is <c>httponly</c> and
    /// <c>samesite=lax</c> purely from the framework defaults.
    /// </summary>
    [Fact]
    public async Task SignIn_CookieHasNoSecureAttributeOverPlainHttp_KnownGap()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await (await GetAsync(client, "/seed")).Content.ReadAsStringAsync();
        using var signIn = await GetAsync(client, "/signin");
        var setCookie = FindSetCookie(signIn)!;

        Assert.DoesNotContain("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);

        await host.StopAsync();
    }

    /// <summary>
    /// The five-minute <c>ExpireTimeSpan</c> lives inside the encrypted ticket, not in a
    /// <c>Set-Cookie</c> <c>expires</c> attribute — the ticket is not persistent, so the browser
    /// treats it as a session cookie. Worth pinning: a reader of
    /// <c>AddMustChangePasswordUserIdCookie</c> would reasonably expect the browser to be told.
    /// </summary>
    [Fact]
    public async Task SignIn_CookieIsASessionCookieWithNoExpiresAttribute()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var setCookie = FindSetCookie(signIn)!;

        Assert.DoesNotContain("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", setCookie, StringComparison.OrdinalIgnoreCase);

        await host.StopAsync();
    }

    /// <summary>
    /// D-M23, end to end: the cookie value itself is data-protected, but the plaintext current
    /// password round-trips through the client and comes back readable on the server. This is the
    /// assertion the unit-level fake cannot make, and it is the one that shows the credential really
    /// does leave the process.
    /// </summary>
    [Fact]
    public async Task Cookie_CarriesThePlaintextPasswordBackFromTheClient_KnownBug()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var setCookie = FindSetCookie(signIn)!;
        var cookie = CookieHeader(setCookie);

        // The wire value is encrypted, so the password is not literally visible in transit...
        Assert.DoesNotContain(InitialPassword, cookie, StringComparison.Ordinal);

        // ...but it is in the ticket, and the client is the one holding it.
        using var peek = await GetAsync(client, "/peek", cookie);
        Assert.Equal(HttpStatusCode.OK, peek.StatusCode);
        Assert.Equal(InitialPassword, await peek.Content.ReadAsStringAsync());

        await host.StopAsync();
    }

    [Fact]
    public async Task Perform_ChangesThePasswordUsingOnlyTheCookie()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var cookie = CookieHeader(FindSetCookie(signIn)!);

        using var perform = await GetAsync(client, $"/perform?new={NewPassword}&confirm={NewPassword}", cookie);

        Assert.Equal(HttpStatusCode.NoContent, perform.StatusCode);
        using var checkNew = await GetAsync(client, $"/check?password={NewPassword}");
        Assert.Equal("yes", await checkNew.Content.ReadAsStringAsync());

        await host.StopAsync();
    }

    /// <summary>
    /// D-M26, end to end: after a successful change the server issues no deletion cookie, and the
    /// very same cookie still authenticates. The ticket — carrying the now-former password — stays
    /// replayable for the rest of its five minutes.
    /// </summary>
    [Fact]
    public async Task Perform_LeavesTheCookieValidAfterSuccess_KnownBug()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var cookie = CookieHeader(FindSetCookie(signIn)!);

        using var perform = await GetAsync(client, $"/perform?new={NewPassword}&confirm={NewPassword}", cookie);
        Assert.Equal(HttpStatusCode.NoContent, perform.StatusCode);
        Assert.Null(FindSetCookie(perform));

        using var peek = await GetAsync(client, "/peek", cookie);
        Assert.Equal(HttpStatusCode.OK, peek.StatusCode);
        Assert.Equal(InitialPassword, await peek.Content.ReadAsStringAsync());

        await host.StopAsync();
    }

    /// <summary>
    /// The asymmetry that makes D-M26 a bug rather than a design choice: a <i>failed</i> attempt does
    /// issue the deletion cookie, so the flow already knows how to end. Only the success path forgets.
    /// </summary>
    [Fact]
    public async Task Perform_DeletesTheCookieOnFailure()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var cookie = CookieHeader(FindSetCookie(signIn)!);

        using var perform = await GetAsync(client, $"/perform?new={NewPassword}&confirm=Different1!pass", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, perform.StatusCode);
        var deletion = FindSetCookie(perform);
        Assert.NotNull(deletion);
        Assert.Contains("expires=Thu, 01 Jan 1970", deletion!, StringComparison.OrdinalIgnoreCase);

        await host.StopAsync();
    }

    /// <summary>
    /// Without the cookie the flow cannot proceed, and the failure is the bare <c>Exception</c> of
    /// D-M27 — which the endpoint here can only map to a generic 400, having no way to tell it from
    /// any other internal failure.
    /// </summary>
    [Fact]
    public async Task Perform_WithoutTheCookie_Fails()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");

        using var perform = await GetAsync(client, $"/perform?new={NewPassword}&confirm={NewPassword}");

        Assert.Equal(HttpStatusCode.BadRequest, perform.StatusCode);
        using var check = await GetAsync(client, $"/check?password={InitialPassword}");
        Assert.Equal("yes", await check.Content.ReadAsStringAsync());

        await host.StopAsync();
    }

    /// <summary>
    /// A second use of the cookie after the password has already been rotated fails — the stored
    /// <c>OldPassword</c> no longer verifies. So D-M26's replay window cannot be used to change the
    /// password twice, but it does keep a valid, decryptable copy of the old credential alive on the
    /// client (see <c>Perform_LeavesTheCookieValidAfterSuccess_KnownBug</c>).
    /// </summary>
    [Fact]
    public async Task Perform_ReplayedAfterSuccess_FailsOnTheStaleCurrentPassword()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var cookie = CookieHeader(FindSetCookie(signIn)!);

        using var first = await GetAsync(client, $"/perform?new={NewPassword}&confirm={NewPassword}", cookie);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        using var replay = await GetAsync(client, $"/perform?new=Third1!pass&confirm=Third1!pass", cookie);

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var check = await GetAsync(client, $"/check?password={NewPassword}");
        Assert.Equal("yes", await check.Content.ReadAsStringAsync());

        await host.StopAsync();
    }
}
