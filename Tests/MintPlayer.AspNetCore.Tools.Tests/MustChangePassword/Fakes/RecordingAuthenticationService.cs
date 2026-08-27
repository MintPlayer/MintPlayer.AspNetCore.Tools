using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword.Fakes;

/// <summary>
/// An <see cref="IAuthenticationService"/> that records every <c>SignInAsync</c>,
/// <c>SignOutAsync</c> and <c>AuthenticateAsync</c> call and replays a configurable
/// <see cref="AuthenticateResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// The service under test never touches a cookie directly — it calls the
/// <c>HttpContext.SignInAsync</c>/<c>SignOutAsync</c>/<c>AuthenticateAsync</c> extension methods,
/// which all resolve <see cref="IAuthenticationService"/> from
/// <c>HttpContext.RequestServices</c>. Replacing that one service is therefore enough to observe
/// the whole auth-facing surface without a server, which keeps these tests unit-fast; the real
/// cookie is exercised once, in <c>MustChangePasswordCookieRoundTripTests</c>.
/// </para>
/// <para>
/// Recording <i>counts</i> as well as arguments is what makes the sign-out assertions meaningful:
/// D-M26 is precisely the <i>absence</i> of a call, which cannot be asserted from arguments alone.
/// </para>
/// </remarks>
internal sealed class RecordingAuthenticationService : IAuthenticationService
{
    public List<(string? Scheme, ClaimsPrincipal Principal, AuthenticationProperties? Properties)> SignInCalls { get; } = [];

    public List<string?> SignOutCalls { get; } = [];

    public List<string?> AuthenticateCalls { get; } = [];

    /// <summary>What <see cref="AuthenticateAsync"/> replays. Defaults to "no ticket present".</summary>
    public AuthenticateResult NextAuthenticateResult { get; set; } = AuthenticateResult.NoResult();

    /// <summary>When set, <see cref="AuthenticateAsync"/> throws it after recording the call.</summary>
    public Exception? AuthenticateFault { get; set; }

    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
    {
        AuthenticateCalls.Add(scheme);
        return AuthenticateFault is null
            ? Task.FromResult(NextAuthenticateResult)
            : Task.FromException<AuthenticateResult>(AuthenticateFault);
    }

    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        => throw new NotSupportedException("The service under test never challenges.");

    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        => throw new NotSupportedException("The service under test never forbids.");

    public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
    {
        SignInCalls.Add((scheme, principal, properties));
        return Task.CompletedTask;
    }

    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
    {
        SignOutCalls.Add(scheme);
        return Task.CompletedTask;
    }

    /// <summary>Builds a successful <see cref="AuthenticateResult"/> carrying <paramref name="claims"/>.</summary>
    public static AuthenticateResult Ticket(string scheme, params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), scheme));
    }
}
