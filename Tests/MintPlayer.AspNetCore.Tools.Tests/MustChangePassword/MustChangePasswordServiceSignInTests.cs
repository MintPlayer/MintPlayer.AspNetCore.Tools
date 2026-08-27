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
    /// The happy path is an exception. <c>ChangePasswordSignInAsync</c> throws
    /// <see cref="MustChangePasswordException"/> after storing the ticket — the exception <i>is</i>
    /// the signal, so the surrounding sign-in cannot silently continue. Pinned because the shape is
    /// load-bearing for consumers.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_ThrowsMustChangePasswordExceptionOnSuccess()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        var exception = await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        Assert.NotEmpty(exception.Message);
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

    /// <summary>
    /// D-M23/D-M41/D-M42 fixed, and the permanent regression guard for it: the ticket carries the user
    /// id and <b>nothing else</b>. It used to carry the user's live plaintext password in an
    /// <c>"OldPassword"</c> claim, on a ticket that leaves the server as a client-side cookie — so the
    /// credential round-tripped to the browser, stayed in the jar for the whole browser session, and
    /// (because the success path never signed out) stayed decryptable there after rotation.
    /// </summary>
    /// <remarks>
    /// Asserted as "no claim anywhere holds the password", not merely "no claim named OldPassword", so
    /// that reintroducing the credential under any other name fails this test too.
    /// </remarks>
    [Fact]
    public async Task ChangePasswordSignInAsync_PutsNothingButTheUserIdInTheTicket()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        var principal = harness.Authentication.SignInCalls[0].Principal;
        var claim = Assert.Single(principal.Claims);
        Assert.Equal(ClaimTypes.Name, claim.Type);
        Assert.Equal(user.Id, claim.Value);
        Assert.DoesNotContain(
            principal.Claims,
            c => c.Value.Contains(MustChangePasswordTestHarness.InitialPassword, StringComparison.Ordinal));
        Assert.Null(principal.FindFirstValue("OldPassword"));
    }

    /// <summary>
    /// D-M44 fixed: the e-mail address was read back out of the ticket and never used, so it was
    /// carried to the client for nothing. It is no longer in the ticket at all.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_DoesNotCarryTheEmailClaim()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync(email: "alice@example.com");

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        Assert.Null(harness.Authentication.SignInCalls[0].Principal.FindFirstValue(ClaimTypes.Email));
    }

    /// <summary>
    /// D-M24 fixed: a null <c>Email</c> is perfectly legal — <c>RequireUniqueEmail</c> is <c>false</c>
    /// by default and Identity never requires an address. It used to produce an
    /// <see cref="ArgumentNullException"/> from <c>AddClaim</c>, leaving the user in neither the old
    /// flow nor the new one. Dropping the dead claim (D-M44) defines the error out of existence.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_SucceedsWhenTheUserHasNoEmail()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync(email: null);

        await Assert.ThrowsAsync<MustChangePasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        Assert.Single(harness.Authentication.SignInCalls);
    }

    /// <summary>
    /// D-M43 fixed: the supplied current password is verified against the store <i>before</i> a ticket
    /// is issued. It used to be taken on trust, so garbage still got a ticket and the failure landed a
    /// full request later as a bare <c>Exception</c>.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_RejectsAnIncorrectCurrentPassword()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        await Assert.ThrowsAsync<IncorrectCurrentPasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, "not-the-password"));

        Assert.Empty(harness.Authentication.SignInCalls);
    }

    /// <summary>
    /// A missing password is the same failure as a wrong one, rather than a ticket with one fewer
    /// claim on it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ChangePasswordSignInAsync_RejectsAMissingCurrentPassword(string? currentPassword)
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();

        await Assert.ThrowsAsync<IncorrectCurrentPasswordException>(
            () => harness.Service.ChangePasswordSignInAsync(user, currentPassword!));

        Assert.Empty(harness.Authentication.SignInCalls);
    }

    [Fact]
    public async Task ChangePasswordSignInAsync_ThrowsArgumentNullExceptionForANullUser()
    {
        using var harness = new MustChangePasswordTestHarness();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Service.ChangePasswordSignInAsync(null!, MustChangePasswordTestHarness.InitialPassword));
    }

    /// <summary>
    /// D-M25 fixed (first of three sites): outside a request — a background job, a hosted service, a
    /// unit test — the caller gets a diagnosable <see cref="InvalidOperationException"/> instead of a
    /// bare <see cref="NullReferenceException"/>.
    /// </summary>
    [Fact]
    public async Task ChangePasswordSignInAsync_ThrowsInvalidOperationExceptionWithoutAnHttpContext()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();
        harness.Accessor.HttpContext = null;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.ChangePasswordSignInAsync(user, MustChangePasswordTestHarness.InitialPassword));

        Assert.Contains("IHttpContextAccessor", exception.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Authentication.SignInCalls);
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
