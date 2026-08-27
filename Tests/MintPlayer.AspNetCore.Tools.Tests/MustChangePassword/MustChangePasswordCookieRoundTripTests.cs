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
/// protector and a real <c>Set-Cookie</c>/<c>Cookie</c> exchange are in play — which is what makes
/// the D-M23 regression guard meaningful: the claim being defended is about what physically leaves
/// the process, and a fake cannot show that.
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
    /// <remarks>
    /// The library's own cookie defaults are used as shipped, including
    /// <c>SecurePolicy = Always</c>: the test client is not a browser, so it replays the
    /// <c>Set-Cookie</c> value regardless, and the attribute itself is what
    /// <c>SignIn_CookieIsSecureHttpOnlyAndStrict</c> asserts.
    /// </remarks>
    private static IHost BuildHost() => new HostBuilder()
        .ConfigureWebHost(webHost => webHost
        .UseTestServer()
        .ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddRouting();
            services.AddSingleton<TestUserData>();
            services.AddIdentityCore<TestUser>().AddUserStore<TestUserStore>();
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
                        await service.ChangePasswordSignInAsync(user!, context.Request.Query["password"].ToString() is { Length: > 0 } p ? p : InitialPassword);
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    }
                    catch (MustChangePasswordException)
                    {
                        // The documented signal: the ticket is stored and the caller must redirect.
                        context.Response.StatusCode = StatusCodes.Status200OK;
                    }
                    catch (IncorrectCurrentPasswordException)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    }
                });

                // Writes out everything the ticket carries, so a test can assert what does — and does
                // not — come back from the client.
                endpoints.MapGet("/peek", async context =>
                {
                    var result = await context.AuthenticateAsync(Scheme);
                    if (!result.Succeeded)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }

                    await context.Response.WriteAsync(string.Join(
                        "\n",
                        result.Principal!.Claims.Select(c => $"{c.Type}={c.Value}")));
                });

                endpoints.MapGet("/perform", async context =>
                {
                    var service = context.RequestServices.GetRequiredService<IMustChangePasswordService<TestUser, string>>();
                    try
                    {
                        await service.PerformChangePasswordAsync(
                            context.Request.Query["current"].ToString(),
                            context.Request.Query["new"].ToString(),
                            context.Request.Query["confirm"].ToString());
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                    }
                    catch (PasswordRejectedException ex)
                    {
                        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                        await context.Response.WriteAsync(string.Join(",", ex.Errors.Select(e => e.Code)));
                    }
                    catch (IncorrectCurrentPasswordException)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    }
                    catch (ChangePasswordFailedException ex)
                    {
                        // Session-level failures: expired, malformed, or the account is gone.
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync(ex.GetType().Name);
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

    private static string PerformUrl(string current, string @new, string confirm)
        => $"/perform?current={Uri.EscapeDataString(current)}&new={Uri.EscapeDataString(@new)}&confirm={Uri.EscapeDataString(confirm)}";

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
    /// D-M33 fixed: the cookie is issued <c>secure</c>, <c>httponly</c> and <c>samesite=strict</c>
    /// rather than inheriting <c>SameAsRequest</c> and <c>Lax</c> from the framework — over plain HTTP
    /// it used to be issued with no <c>secure</c> attribute at all.
    /// </summary>
    [Fact]
    public async Task SignIn_CookieIsSecureHttpOnlyAndStrict()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var setCookie = FindSetCookie(signIn)!;

        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);

        await host.StopAsync();
    }

    /// <summary>
    /// D-M41 fixed: <c>ExpireTimeSpan</c> bounds the server-side ticket only, so the browser was told
    /// nothing and kept the cookie for the whole browsing session, long after the ticket was dead. The
    /// browser is now told the same five minutes through <c>max-age</c>.
    /// </summary>
    [Fact]
    public async Task SignIn_CookieTellsTheBrowserWhenToDropIt()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var setCookie = FindSetCookie(signIn)!;

        Assert.Contains("max-age=300", setCookie, StringComparison.OrdinalIgnoreCase);

        await host.StopAsync();
    }

    /// <summary>
    /// D-M23/D-M42 fixed, end to end, and the permanent regression guard: the ticket that reaches the
    /// client and comes back carries the user id and nothing else. It used to carry the user's live
    /// plaintext password, readable on the way back in from the client's own cookie — encrypted on the
    /// wire, but genuinely out of the process and in the browser's jar.
    /// </summary>
    [Fact]
    public async Task Cookie_CarriesNothingButTheUserIdBackFromTheClient()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        using var seed = await GetAsync(client, "/seed");
        var userId = await seed.Content.ReadAsStringAsync();
        using var signIn = await GetAsync(client, "/signin");
        var setCookie = FindSetCookie(signIn)!;
        var cookie = CookieHeader(setCookie);

        Assert.DoesNotContain(InitialPassword, cookie, StringComparison.Ordinal);

        using var peek = await GetAsync(client, "/peek", cookie);
        Assert.Equal(HttpStatusCode.OK, peek.StatusCode);
        var claims = await peek.Content.ReadAsStringAsync();

        Assert.Equal($"{ClaimTypes.Name}={userId}", claims);
        Assert.DoesNotContain(InitialPassword, claims, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@example.com", claims, StringComparison.Ordinal);

        await host.StopAsync();
    }

    /// <summary>
    /// D-M43 fixed, end to end: an incorrect current password never gets a ticket, so no cookie is
    /// issued at all and the failure lands in the request that made the mistake.
    /// </summary>
    [Fact]
    public async Task SignIn_WithTheWrongPassword_IssuesNoCookie()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin?password=not-the-password");

        Assert.Equal(HttpStatusCode.Forbidden, signIn.StatusCode);
        Assert.Null(FindSetCookie(signIn));

        await host.StopAsync();
    }

    [Fact]
    public async Task Perform_ChangesThePasswordWithTheCookieAndTheReenteredPassword()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var cookie = CookieHeader(FindSetCookie(signIn)!);

        using var perform = await GetAsync(client, PerformUrl(InitialPassword, NewPassword, NewPassword), cookie);

        Assert.Equal(HttpStatusCode.NoContent, perform.StatusCode);
        using var checkNew = await GetAsync(client, $"/check?password={NewPassword}");
        Assert.Equal("yes", await checkNew.Content.ReadAsStringAsync());

        await host.StopAsync();
    }

    /// <summary>
    /// D-M26 fixed, end to end: the success path issues the deletion cookie, so the browser drops the
    /// ticket instead of holding a valid one for the rest of its five minutes. A failed attempt already
    /// did this — that asymmetry is what made the omission a bug rather than a design choice.
    /// </summary>
    [Fact]
    public async Task Perform_DeletesTheCookieOnSuccess()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var cookie = CookieHeader(FindSetCookie(signIn)!);

        using var perform = await GetAsync(client, PerformUrl(InitialPassword, NewPassword, NewPassword), cookie);

        Assert.Equal(HttpStatusCode.NoContent, perform.StatusCode);
        var deletion = FindSetCookie(perform);
        Assert.NotNull(deletion);
        Assert.Contains("expires=Thu, 01 Jan 1970", deletion!, StringComparison.OrdinalIgnoreCase);

        await host.StopAsync();
    }

    /// <summary>
    /// A recoverable input error keeps the ticket: no <c>Set-Cookie</c> at all, so the user can correct
    /// the form and try again without re-authenticating. The errors come back naming the reason.
    /// </summary>
    [Fact]
    public async Task Perform_MismatchedConfirmation_KeepsTheCookieAndNamesTheReason()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var cookie = CookieHeader(FindSetCookie(signIn)!);

        using var perform = await GetAsync(client, PerformUrl(InitialPassword, NewPassword, MustChangePasswordTestHarness.MismatchedPassword), cookie);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, perform.StatusCode);
        Assert.Equal(PasswordRejectedException.PasswordConfirmationMismatchCode, await perform.Content.ReadAsStringAsync());
        Assert.Null(FindSetCookie(perform));

        using var retry = await GetAsync(client, PerformUrl(InitialPassword, NewPassword, NewPassword), cookie);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);

        await host.StopAsync();
    }

    /// <summary>
    /// D-M27 fixed, end to end: without a cookie the endpoint can tell "your session expired" from any
    /// other internal failure, where the bare <c>Exception</c> left it nothing to branch on.
    /// </summary>
    [Fact]
    public async Task Perform_WithoutTheCookie_ReportsAnExpiredSession()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");

        using var perform = await GetAsync(client, PerformUrl(InitialPassword, NewPassword, NewPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, perform.StatusCode);
        Assert.Equal(nameof(ChangePasswordSessionExpiredException), await perform.Content.ReadAsStringAsync());
        using var check = await GetAsync(client, $"/check?password={InitialPassword}");
        Assert.Equal("yes", await check.Content.ReadAsStringAsync());

        await host.StopAsync();
    }

    /// <summary>
    /// A retained copy of the cookie value stays cryptographically valid until it expires — cookie
    /// authentication is stateless, so signing out can only ask the browser to drop it. What the D-M23
    /// redesign buys is that replaying it is worth nothing: the ticket holds no credential, and the
    /// current password it would need has just been rotated.
    /// </summary>
    [Fact]
    public async Task Perform_ReplayedAfterSuccess_CannotChangeThePasswordAgain()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();

        await GetAsync(client, "/seed");
        using var signIn = await GetAsync(client, "/signin");
        var cookie = CookieHeader(FindSetCookie(signIn)!);

        using var first = await GetAsync(client, PerformUrl(InitialPassword, NewPassword, NewPassword), cookie);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        using var replay = await GetAsync(client, PerformUrl(InitialPassword, MustChangePasswordTestHarness.SecondNewPassword, MustChangePasswordTestHarness.SecondNewPassword), cookie);

        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        using var check = await GetAsync(client, $"/check?password={NewPassword}");
        Assert.Equal("yes", await check.Content.ReadAsStringAsync());

        await host.StopAsync();
    }
}
