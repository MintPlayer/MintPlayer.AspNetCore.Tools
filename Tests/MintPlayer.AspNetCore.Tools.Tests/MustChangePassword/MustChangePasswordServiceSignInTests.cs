using MintPlayer.AspNetCore.MustChangePassword.Constants;
using MintPlayer.AspNetCore.MustChangePassword.Exceptions;
using MintPlayer.AspNetCore.Tools.Tests.MustChangePassword.Fakes;
using System.Security.Claims;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword;

public class MustChangePasswordServiceSignInTests
{
    private static string Scheme => MustChangePasswordConstants.MustChangePasswordScheme;

    /// <summary>
    /// The happy path is an exception. <c>ChangePasswordSignInAsync</c> always throws
    /// <see cref="MustChangePasswordException"/> after storing the ticket — the exception <i>is</i>
    /// the signal, so a caller must wrap every call in a <c>try</c>/<c>catch</c> to distinguish
    /// success from failure. Pinned because the shape is load-bearing for consumers.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_AlwaysThrowsMustChangePasswordException()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));
    }

    [Fact]
    public async Task ChangePasswordSignInAsync_SignsInWithTheChangePasswordSchemeBeforeThrowing()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        var call = Assert.Single(harness.Authentication.SignInCalls);
        Assert.Equal(Scheme, call.Scheme);
        Assert.Null(call.Properties);
    }

    /// <summary>
    /// The identity is stamped with the change-password scheme as its authentication type, which is
    /// what makes the cookie handler's ticket self-describing on the way back in.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_StampsTheIdentityWithTheScheme()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        var identity = Assert.IsType<ClaimsIdentity>(harness.Authentication.SignInCalls[0].Principal.Identity);
        Assert.Equal(Scheme, identity.AuthenticationType);
        Assert.True(identity.IsAuthenticated);
    }

    /// <summary>
    /// The user id comes from <c>UserManager.GetUserIdAsync</c> — the store's answer — and is carried
    /// as <c>ClaimTypes.Name</c>, not as <c>NameIdentifier</c>. That choice matters: any consumer
    /// reading the ticket has to look in <c>Name</c>.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_CarriesTheUserIdAsTheNameClaim()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        var principal = harness.Authentication.SignInCalls[0].Principal;
        Assert.Equal(user.Id, principal.FindFirstValue(ClaimTypes.Name));
        Assert.Null(principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task ChangePasswordSignInAsync_CarriesTheEmailClaim()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync(email: "alice@example.com");

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        Assert.Equal("alice@example.com", harness.Authentication.SignInCalls[0].Principal.FindFirstValue(ClaimTypes.Email));
    }

    /// <summary>
    /// D-M23: the user's <b>plaintext current password</b> is written into an <c>"OldPassword"</c>
    /// claim on a ticket that leaves the server as a client-side cookie. Data protection encrypts
    /// it, but the live credential still round-trips to the browser and sits in the cookie jar for
    /// the five-minute lifetime. The state should be held server-side, or the password re-entered.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_PutsThePlaintextCurrentPasswordInAClaim_KnownBug()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        var principal = harness.Authentication.SignInCalls[0].Principal;
        Assert.Equal(MustChangePasswordTestHarness.InitialPassword, principal.FindFirstValue("OldPassword"));
    }

    /// <summary>
    /// The one guard the claim assembly does have: an empty old password adds no claim, so the
    /// ticket is not padded with a meaningless value.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ChangePasswordSignInAsync_OmitsTheOldPasswordClaimWhenThereIsNoOldPassword(string? oldPassword)
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, oldPassword!));

        var principal = harness.Authentication.SignInCalls[0].Principal;
        Assert.Null(principal.FindFirstValue("OldPassword"));
        Assert.Equal(2, principal.Claims.Count());
    }

    /// <summary>
    /// D-M24: <c>AddClaim(new Claim(ClaimTypes.Email, user.Email))</c> has no null check, and a null
    /// <c>Email</c> is perfectly legal — <c>RequireUniqueEmail</c> is <c>false</c> by default and
    /// Identity never requires an address. The result is an
    /// <see cref="ArgumentNullException"/> escaping a method documented to throw
    /// <see cref="MustChangePasswordException"/>, with the sign-in never happening, so the user is
    /// left in neither the old flow nor the new one.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_ThrowsArgumentNullExceptionWhenEmailIsNull_KnownBug()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync(email: null);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        Assert.Empty(harness.Authentication.SignInCalls);
    }

    /// <summary>
    /// D-M25 (first of three sites): <c>httpContextAccessor.HttpContext</c> is dereferenced with no
    /// null check. Outside a request — a background job, a hosted service, a unit test — the caller
    /// gets a bare <see cref="NullReferenceException"/> instead of a diagnosable message.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_ThrowsNullReferenceExceptionWithoutAnHttpContext_KnownBug()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();
        harness.Accessor.HttpContext = null;

        await Assert.ThrowsAsync<NullReferenceException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));
    }

    /// <summary>
    /// Nothing in the flow validates that the supplied old password is actually the user's password —
    /// verification is deferred to <c>PerformChangePasswordAsync</c>. So a caller that passes garbage
    /// still gets a ticket, and the failure surfaces one request later.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_DoesNotVerifyTheSuppliedOldPassword_KnownGap()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, "not-the-password"));

        Assert.Single(harness.Authentication.SignInCalls);
        Assert.Equal("not-the-password", harness.Authentication.SignInCalls[0].Principal.FindFirstValue("OldPassword"));
    }

    /// <summary>
    /// Sign-in never signs anything out first, so an existing change-password ticket from an earlier
    /// attempt is replaced rather than revoked — and the flow never authenticates on the way in.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_DoesNotSignOutOrAuthenticate()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        Assert.Empty(harness.Authentication.SignOutCalls);
        Assert.Empty(harness.Authentication.AuthenticateCalls);
    }
}
