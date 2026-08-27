using Microsoft.AspNetCore.Authentication;
using MintPlayer.AspNetCore.MustChangePassword.Constants;
using MintPlayer.AspNetCore.Tools.Tests.MustChangePassword.Fakes;
using System.Security.Claims;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword;

public class MustChangePasswordServicePerformTests
{
    private static string Scheme => MustChangePasswordConstants.MustChangePasswordScheme;

    /// <summary>
    /// Seeds a user and arms the recording authentication service with the ticket
    /// <c>ChangePasswordSignInAsync</c> would have produced for them.
    /// </summary>
    private static async Task<TestUser> ArmTicketAsync(
        MustChangePasswordTestHarness harness,
        string? userId = null,
        string? oldPassword = MustChangePasswordTestHarness.InitialPassword)
    {
        var user = await harness.SeedUserAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userId ?? user.Id),
            new(ClaimTypes.Email, user.Email!),
        };

        if (!string.IsNullOrEmpty(oldPassword))
        {
            claims.Add(new Claim("OldPassword", oldPassword));
        }

        harness.Authentication.NextAuthenticateResult = RecordingAuthenticationService.Ticket(Scheme, [.. claims]);

        // Seeding validated the initial password; only what happens after arming is of interest.
        harness.PasswordValidator.ValidatedPasswords.Clear();
        return user;
    }

    [Fact]
    public async Task PerformChangePasswordAsync_ChangesThePasswordForReal()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await ArmTicketAsync(harness);

        await harness.Service.PerformChangePasswordAsync(
            MustChangePasswordTestHarness.NewPassword,
            MustChangePasswordTestHarness.NewPassword);

        Assert.True(await harness.UserManager.CheckPasswordAsync(user, MustChangePasswordTestHarness.NewPassword));
        Assert.False(await harness.UserManager.CheckPasswordAsync(user, MustChangePasswordTestHarness.InitialPassword));
    }

    [Fact]
    public async Task PerformChangePasswordAsync_AuthenticatesTheChangePasswordScheme()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);

        await harness.Service.PerformChangePasswordAsync(
            MustChangePasswordTestHarness.NewPassword,
            MustChangePasswordTestHarness.NewPassword);

        Assert.Equal(Scheme, Assert.Single(harness.Authentication.AuthenticateCalls));
    }

    /// <summary>
    /// D-M26: on the success path the change-password cookie is never signed out. The five-minute
    /// ticket — which carries the user's <i>old</i> plaintext password (D-M23) — stays valid and
    /// replayable after the password has already been rotated.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_DoesNotSignOutOnSuccess_KnownBug()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);

        await harness.Service.PerformChangePasswordAsync(
            MustChangePasswordTestHarness.NewPassword,
            MustChangePasswordTestHarness.NewPassword);

        Assert.Empty(harness.Authentication.SignOutCalls);
    }

    /// <summary>
    /// D-M27: three distinct failure modes — no ticket at all, a ticket with no user id, and a user
    /// id that no longer resolves — all surface as a bare <c>throw new Exception()</c>. A caller
    /// cannot tell "your session expired, sign in again" from "your account was deleted", and neither
    /// can a log: <c>new Exception()</c> carries no message of its own.
    /// </summary>
    /// <remarks>
    /// The exact type is asserted (<c>IsType</c> is an exact match in xunit) rather than the message,
    /// because the default <c>Exception</c> message is a localized framework resource string and
    /// would differ between a Dutch dev box and the CI runner.
    /// </remarks>
    [Fact]
    public async Task PerformChangePasswordAsync_NoTicket_ThrowsBareException_KnownGap()
    {
        using var harness = new MustChangePasswordTestHarness();
        await harness.SeedUserAsync();
        harness.Authentication.NextAuthenticateResult = AuthenticateResult.NoResult();

        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => harness.Service.PerformChangePasswordAsync(
                MustChangePasswordTestHarness.NewPassword,
                MustChangePasswordTestHarness.NewPassword));

        Assert.IsType<Exception>(exception);
    }

    /// <summary>D-M27, second of the three indistinguishable sites: a ticket with no user id.</summary>
    [Fact]
    public async Task PerformChangePasswordAsync_TicketWithoutUserId_ThrowsBareException_KnownGap()
    {
        using var harness = new MustChangePasswordTestHarness();
        await harness.SeedUserAsync();
        harness.Authentication.NextAuthenticateResult = RecordingAuthenticationService.Ticket(
            Scheme,
            new Claim(ClaimTypes.Email, "alice@example.com"),
            new Claim("OldPassword", MustChangePasswordTestHarness.InitialPassword));

        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => harness.Service.PerformChangePasswordAsync(
                MustChangePasswordTestHarness.NewPassword,
                MustChangePasswordTestHarness.NewPassword));

        Assert.IsType<Exception>(exception);
    }

    /// <summary>D-M27, third site: the user id in the ticket no longer resolves to a user.</summary>
    [Fact]
    public async Task PerformChangePasswordAsync_UnknownUserId_ThrowsBareException_KnownGap()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness, userId: Guid.NewGuid().ToString("N"));

        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => harness.Service.PerformChangePasswordAsync(
                MustChangePasswordTestHarness.NewPassword,
                MustChangePasswordTestHarness.NewPassword));

        Assert.IsType<Exception>(exception);
    }

    /// <summary>
    /// The fourth bare <c>throw new Exception()</c> — a ticket whose <c>OldPassword</c> no longer
    /// matches, which is the normal outcome once the password has already been changed once. Also
    /// D-M27.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_WrongCurrentPassword_ThrowsBareException_KnownGap()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness, oldPassword: "Stale1!pass");

        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => harness.Service.PerformChangePasswordAsync(
                MustChangePasswordTestHarness.NewPassword,
                MustChangePasswordTestHarness.NewPassword));

        Assert.IsType<Exception>(exception);
    }

    /// <summary>
    /// Every failure path signs the user out of the change-password flow, so the ticket has to be
    /// re-obtained. This is the correct half of the <c>catch</c> block; D-M28 below is the incorrect half.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_SignsOutTheChangePasswordSchemeOnFailure()
    {
        using var harness = new MustChangePasswordTestHarness();
        await harness.SeedUserAsync();
        harness.Authentication.NextAuthenticateResult = AuthenticateResult.NoResult();

        await Assert.ThrowsAnyAsync<Exception>(
            () => harness.Service.PerformChangePasswordAsync(
                MustChangePasswordTestHarness.NewPassword,
                MustChangePasswordTestHarness.NewPassword));

        Assert.Equal(Scheme, Assert.Single(harness.Authentication.SignOutCalls));
    }

    [Fact]
    public async Task PerformChangePasswordAsync_MismatchedConfirmation_ThrowsUnauthorizedAccess()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await ArmTicketAsync(harness);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => harness.Service.PerformChangePasswordAsync(
                MustChangePasswordTestHarness.NewPassword,
                "Different1!pass"));

        Assert.Single(harness.Authentication.SignOutCalls);
        Assert.True(
            await harness.UserManager.CheckPasswordAsync(user, MustChangePasswordTestHarness.InitialPassword),
            "the password must be untouched when the confirmation does not match");

        // The guard fired before Identity was asked to do anything — the contrast the D-M29 pin needs.
        Assert.Empty(harness.PasswordValidator.ValidatedPasswords);
    }

    /// <summary>
    /// D-M29: the mismatch guard is a plain <c>!=</c>, so two nulls compare <b>equal</b> and fall
    /// straight through to <c>ChangePasswordAsync(user, current, null)</c>.
    /// </summary>
    /// <remarks>
    /// The exception the caller sees is <see cref="UnauthorizedAccessException"/> — byte for byte the
    /// same as a genuine mismatch — so the fall-through is invisible from the outside. What proves it
    /// happened is that Identity's password validation <i>ran</i>, with <c>null</c> as the candidate
    /// password: a real mismatch stops before that (see
    /// <c>PerformChangePasswordAsync_MismatchedConfirmation_ThrowsUnauthorizedAccess</c>, which
    /// asserts the empty list). So a request with no password fields at all is silently treated as
    /// "the user asked to set the password to nothing" rather than as a malformed request.
    /// </remarks>
    [Fact]
    public async Task PerformChangePasswordAsync_BothNullPasswords_FallThroughTheMismatchGuard_KnownBug()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await ArmTicketAsync(harness);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => harness.Service.PerformChangePasswordAsync(null!, null!));

        Assert.Equal([null], harness.PasswordValidator.ValidatedPasswords);
        Assert.True(
            await harness.UserManager.CheckPasswordAsync(user, MustChangePasswordTestHarness.InitialPassword),
            "the password must still be the original one");
    }

    /// <summary>
    /// Two empty strings compare equal too, and an empty new password is then rejected by Identity's
    /// password policy rather than by the service — surfacing as
    /// <see cref="UnauthorizedAccessException"/> from the <c>result.Succeeded</c> check, which is the
    /// same exception a mismatch produces. Companion to D-M29 and D-M27's indistinguishability theme.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_BothEmptyPasswords_RejectedByIdentityNotByTheGuard_KnownGap()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await ArmTicketAsync(harness);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => harness.Service.PerformChangePasswordAsync(string.Empty, string.Empty));

        Assert.Equal([string.Empty], harness.PasswordValidator.ValidatedPasswords);
        Assert.True(await harness.UserManager.CheckPasswordAsync(user, MustChangePasswordTestHarness.InitialPassword));
    }

    /// <summary>
    /// A new password that fails the Identity password policy is reported as
    /// <see cref="UnauthorizedAccessException"/>, discarding every <c>IdentityError</c> the manager
    /// produced. The user is told "unauthorized" when the real answer is "your password needs a
    /// digit". Same family as D-M27.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_WeakNewPassword_LosesTheIdentityErrors_KnownGap()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await ArmTicketAsync(harness);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => harness.Service.PerformChangePasswordAsync("abc", "abc"));

        Assert.True(await harness.UserManager.CheckPasswordAsync(user, MustChangePasswordTestHarness.InitialPassword));
    }

    /// <summary>
    /// D-M28: the blanket <c>catch (Exception)</c> cannot tell a rejected credential from a database
    /// being briefly unreachable, so a transient infrastructure failure also signs the user out of
    /// the change-password flow. The ticket is destroyed, so the user must restart the whole sign-in
    /// dance for a fault that had nothing to do with them.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_TransientStoreFailure_StillSignsTheUserOut_KnownBug()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);
        harness.UserData.FindByIdFault = new TimeoutException("the user store is briefly unreachable");

        await Assert.ThrowsAsync<TimeoutException>(
            () => harness.Service.PerformChangePasswordAsync(
                MustChangePasswordTestHarness.NewPassword,
                MustChangePasswordTestHarness.NewPassword));

        Assert.Equal(Scheme, Assert.Single(harness.Authentication.SignOutCalls));
    }

    /// <summary>
    /// D-M28, the other half: the original exception is re-thrown rather than swallowed, so the
    /// caller does at least see the real fault. Pinned so a "fix" for D-M28 does not accidentally
    /// start hiding infrastructure errors.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_RethrowsTheOriginalException()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);
        var fault = new TimeoutException("the user store is briefly unreachable");
        harness.UserData.FindByIdFault = fault;

        var thrown = await Assert.ThrowsAsync<TimeoutException>(
            () => harness.Service.PerformChangePasswordAsync(
                MustChangePasswordTestHarness.NewPassword,
                MustChangePasswordTestHarness.NewPassword));

        Assert.Same(fault, thrown);
    }

    /// <summary>
    /// D-M25 (second and third sites): with no <c>HttpContext</c>, <c>AuthenticateAsync</c> throws a
    /// <see cref="NullReferenceException"/>, the blanket <c>catch</c> then dereferences the same null
    /// <c>HttpContext</c> to sign out, and the exception the caller sees comes from the
    /// <i>error handler</i> rather than from the original failure.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_ThrowsNullReferenceExceptionWithoutAnHttpContext_KnownBug()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);
        harness.Accessor.HttpContext = null;

        await Assert.ThrowsAsync<NullReferenceException>(
            () => harness.Service.PerformChangePasswordAsync(
                MustChangePasswordTestHarness.NewPassword,
                MustChangePasswordTestHarness.NewPassword));

        Assert.Empty(harness.Authentication.SignOutCalls);
    }

    /// <summary>
    /// The <c>Email</c> claim is read out of the ticket into <c>MustChangePasswordInfo.Email</c> and
    /// then never used — the change is driven entirely by the user id and the old password. Pinned
    /// because it is the observable half of carrying the address in the cookie at all.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_SucceedsWithoutAnEmailClaim_KnownGap()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();
        harness.Authentication.NextAuthenticateResult = RecordingAuthenticationService.Ticket(
            Scheme,
            new Claim(ClaimTypes.Name, user.Id),
            new Claim("OldPassword", MustChangePasswordTestHarness.InitialPassword));

        await harness.Service.PerformChangePasswordAsync(
            MustChangePasswordTestHarness.NewPassword,
            MustChangePasswordTestHarness.NewPassword);

        Assert.True(await harness.UserManager.CheckPasswordAsync(user, MustChangePasswordTestHarness.NewPassword));
    }
}
