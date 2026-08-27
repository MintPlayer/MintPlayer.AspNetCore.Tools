using Microsoft.AspNetCore.Authentication;
using MintPlayer.AspNetCore.MustChangePassword.Constants;
using MintPlayer.AspNetCore.MustChangePassword.Exceptions;
using MintPlayer.AspNetCore.Tools.Tests.MustChangePassword.Fakes;
using System.Security.Claims;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword;

public class MustChangePasswordServicePerformTests
{
    private const string Initial = MustChangePasswordTestHarness.InitialPassword;
    private const string New = MustChangePasswordTestHarness.NewPassword;

    private static string Scheme => MustChangePasswordConstants.MustChangePasswordScheme;

    /// <summary>
    /// Seeds a user and arms the recording authentication service with the ticket
    /// <c>ChangePasswordSignInAsync</c> would have produced for them — since the D-M23 redesign, a
    /// single user-id claim.
    /// </summary>
    private static async Task<TestUser> ArmTicketAsync(MustChangePasswordTestHarness harness, string? userId = null)
    {
        var user = await harness.SeedUserAsync();

        harness.Authentication.NextAuthenticateResult = RecordingAuthenticationService.Ticket(
            Scheme,
            new Claim(ClaimTypes.Name, userId ?? user.Id));

        // Seeding validated the initial password; only what happens after arming is of interest.
        harness.PasswordValidator.ValidatedPasswords.Clear();
        return user;
    }

    [Fact]
    public async Task PerformChangePasswordAsync_ChangesThePasswordForReal()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await ArmTicketAsync(harness);

        await harness.Service.PerformChangePasswordAsync(Initial, New, New);

        Assert.True(await harness.UserManager.CheckPasswordAsync(user, New));
        Assert.False(await harness.UserManager.CheckPasswordAsync(user, Initial));
    }

    [Fact]
    public async Task PerformChangePasswordAsync_AuthenticatesTheChangePasswordScheme()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);

        await harness.Service.PerformChangePasswordAsync(Initial, New, New);

        Assert.Equal(Scheme, Assert.Single(harness.Authentication.AuthenticateCalls));
    }

    /// <summary>
    /// D-M26 fixed: the flow ends when it succeeds. The ticket used to stay valid and replayable for
    /// the rest of its five minutes, which — combined with D-M23 — left a decryptable copy of the
    /// former credential on the client after rotation (D-M42).
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_SignsOutOnSuccess()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);

        await harness.Service.PerformChangePasswordAsync(Initial, New, New);

        Assert.Equal(Scheme, Assert.Single(harness.Authentication.SignOutCalls));
    }

    /// <summary>
    /// D-M27 fixed, first of the five failure modes that used to be indistinguishable: no ticket on the
    /// request at all. A consumer can now render "your session expired, sign in again" for exactly
    /// this case.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_NoTicket_ThrowsSessionExpired()
    {
        using var harness = new MustChangePasswordTestHarness();
        await harness.SeedUserAsync();
        harness.Authentication.NextAuthenticateResult = AuthenticateResult.NoResult();

        var exception = await Assert.ThrowsAsync<ChangePasswordSessionExpiredException>(
            () => harness.Service.PerformChangePasswordAsync(Initial, New, New));

        Assert.NotEmpty(exception.Message);
        Assert.Equal(Scheme, Assert.Single(harness.Authentication.SignOutCalls));
    }

    /// <summary>
    /// D-M27, second failure mode: a ticket that authenticated but carries no user id. Distinct from
    /// "no ticket" — it means something other than this library issued the cookie — and the first bare
    /// <c>throw new Exception()</c> conflated the two.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_TicketWithoutUserId_ThrowsSessionInvalid()
    {
        using var harness = new MustChangePasswordTestHarness();
        await harness.SeedUserAsync();
        harness.Authentication.NextAuthenticateResult = RecordingAuthenticationService.Ticket(
            Scheme,
            new Claim(ClaimTypes.Email, "alice@example.com"));

        await Assert.ThrowsAsync<ChangePasswordSessionInvalidException>(
            () => harness.Service.PerformChangePasswordAsync(Initial, New, New));

        Assert.Equal(Scheme, Assert.Single(harness.Authentication.SignOutCalls));
    }

    /// <summary>
    /// D-M27, third failure mode: the account was deleted while the flow was in progress. The
    /// exception carries the user id, so it can be logged without guessing.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_UnknownUserId_ThrowsUserNotFound()
    {
        using var harness = new MustChangePasswordTestHarness();
        var missing = Guid.NewGuid().ToString("N");
        await ArmTicketAsync(harness, userId: missing);

        var exception = await Assert.ThrowsAsync<ChangePasswordUserNotFoundException>(
            () => harness.Service.PerformChangePasswordAsync(Initial, New, New));

        Assert.Equal(missing, exception.UserId);
        Assert.Equal(Scheme, Assert.Single(harness.Authentication.SignOutCalls));
    }

    /// <summary>
    /// D-M27, fourth failure mode: the re-entered current password does not verify. The ticket is
    /// deliberately <i>kept</i> — its holder already proved knowledge of the password once, so this is
    /// almost always a typo, and destroying the ticket would force a full re-authentication for it.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_WrongCurrentPassword_ThrowsIncorrectCurrentPasswordAndKeepsTheTicket()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await ArmTicketAsync(harness);

        await Assert.ThrowsAsync<IncorrectCurrentPasswordException>(
            () => harness.Service.PerformChangePasswordAsync(MustChangePasswordTestHarness.StalePassword, New, New));

        Assert.Empty(harness.Authentication.SignOutCalls);
        Assert.True(await harness.UserManager.CheckPasswordAsync(user, Initial));
    }

    /// <summary>
    /// D-M27, fifth failure mode: the new password is refused. D-M45 fixed with it — Identity's own
    /// <c>IdentityError</c>s reach the caller, so the user is told "your password needs a digit"
    /// instead of "unauthorized".
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_WeakNewPassword_SurfacesTheIdentityErrors()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await ArmTicketAsync(harness);

        var exception = await Assert.ThrowsAsync<PasswordRejectedException>(
            () => harness.Service.PerformChangePasswordAsync(Initial, "abc", "abc"));

        Assert.NotEmpty(exception.Errors);
        Assert.All(exception.Errors, e =>
        {
            Assert.False(string.IsNullOrEmpty(e.Code));
            Assert.False(string.IsNullOrEmpty(e.Description));
        });
        Assert.Contains(exception.Errors, e => e.Code.StartsWith("Password", StringComparison.Ordinal));
        Assert.Contains(exception.Errors.First().Description, exception.Message, StringComparison.Ordinal);

        // Recoverable: the user can correct the form, so the ticket survives.
        Assert.Empty(harness.Authentication.SignOutCalls);
        Assert.True(await harness.UserManager.CheckPasswordAsync(user, Initial));
    }

    [Fact]
    public async Task PerformChangePasswordAsync_MismatchedConfirmation_ThrowsPasswordRejected()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await ArmTicketAsync(harness);

        var exception = await Assert.ThrowsAsync<PasswordRejectedException>(
            () => harness.Service.PerformChangePasswordAsync(Initial, New, MustChangePasswordTestHarness.MismatchedPassword));

        Assert.Equal(PasswordRejectedException.PasswordConfirmationMismatchCode, Assert.Single(exception.Errors).Code);
        Assert.Empty(harness.Authentication.SignOutCalls);
        Assert.True(
            await harness.UserManager.CheckPasswordAsync(user, Initial),
            "the password must be untouched when the confirmation does not match");

        // The guard fires before Identity is asked to do anything.
        Assert.Empty(harness.PasswordValidator.ValidatedPasswords);
    }

    /// <summary>
    /// D-M29 fixed: <c>newPassword != newPasswordConfirmation</c> compared <b>equal</b> when both were
    /// null and fell straight through to <c>ChangePasswordAsync(user, current, null)</c> — a request
    /// with no password fields at all was silently treated as "set the password to nothing". Emptiness
    /// is now checked first, and reported as its own error code.
    /// </summary>
    /// <remarks>
    /// The fall-through used to be invisible from the outside: Identity's <c>PasswordValidator</c>
    /// fails a null candidate rather than throwing, so the caller saw the same
    /// <c>UnauthorizedAccessException</c> either way. What proves it no longer happens is that Identity
    /// is never reached — the validator records nothing.
    /// </remarks>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(null, MustChangePasswordTestHarness.NewPassword)]
    public async Task PerformChangePasswordAsync_MissingNewPassword_IsRejectedBeforeIdentityIsReached(string? newPassword, string? confirmation)
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await ArmTicketAsync(harness);

        var exception = await Assert.ThrowsAsync<PasswordRejectedException>(
            () => harness.Service.PerformChangePasswordAsync(Initial, newPassword!, confirmation!));

        Assert.Equal(PasswordRejectedException.PasswordRequiredCode, Assert.Single(exception.Errors).Code);
        Assert.Empty(harness.PasswordValidator.ValidatedPasswords);
        Assert.True(await harness.UserManager.CheckPasswordAsync(user, Initial), "the password must still be the original one");
    }

    /// <summary>
    /// A missing current password is reported as a missing field rather than as an incorrect password,
    /// and both missing fields are reported together — the form can highlight both at once.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_MissingCurrentPassword_IsReportedAsItsOwnError()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);

        var exception = await Assert.ThrowsAsync<PasswordRejectedException>(
            () => harness.Service.PerformChangePasswordAsync(null!, null!, null!));

        Assert.Equal(
            [PasswordRejectedException.CurrentPasswordRequiredCode, PasswordRejectedException.PasswordRequiredCode],
            exception.Errors.Select(e => e.Code));
        Assert.Empty(harness.PasswordValidator.ValidatedPasswords);
    }

    /// <summary>
    /// D-M28 fixed: the blanket <c>catch (Exception) { SignOutAsync(); throw; }</c> could not tell a
    /// rejected credential from a database being briefly unreachable, so a transient infrastructure
    /// fault destroyed the ticket and made the user restart the whole sign-in dance for something that
    /// had nothing to do with them. Infrastructure faults now propagate untouched.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_TransientStoreFailure_DoesNotSignTheUserOut()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);
        harness.UserData.FindByIdFault = new TimeoutException("the user store is briefly unreachable");

        await Assert.ThrowsAsync<TimeoutException>(
            () => harness.Service.PerformChangePasswordAsync(Initial, New, New));

        Assert.Empty(harness.Authentication.SignOutCalls);
    }

    /// <summary>
    /// D-M28, the half that had to stay true: the original exception still reaches the caller. Pinned
    /// so narrowing the <c>catch</c> did not start swallowing faults.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_RethrowsTheOriginalException()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);
        var fault = new TimeoutException("the user store is briefly unreachable");
        harness.UserData.FindByIdFault = fault;

        var thrown = await Assert.ThrowsAsync<TimeoutException>(
            () => harness.Service.PerformChangePasswordAsync(Initial, New, New));

        Assert.Same(fault, thrown);
    }

    /// <summary>
    /// The same for a fault raised by the authentication stack itself — the cookie handler failing is
    /// not a reason to declare the session over.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_AuthenticationFailure_PropagatesWithoutSigningOut()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);
        var fault = new InvalidOperationException("the data protection key ring is unavailable");
        harness.Authentication.AuthenticateFault = fault;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.PerformChangePasswordAsync(Initial, New, New));

        Assert.Same(fault, thrown);
        Assert.Empty(harness.Authentication.SignOutCalls);
    }

    /// <summary>
    /// D-M25 fixed (second and third sites): with no <c>HttpContext</c> the caller used to get a
    /// <see cref="NullReferenceException"/> raised by the <c>catch</c> block's own dereference — the
    /// exception came from the error handler rather than from the failure. There is now one guarded
    /// accessor, and it reports what is wrong.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_ThrowsInvalidOperationExceptionWithoutAnHttpContext()
    {
        using var harness = new MustChangePasswordTestHarness();
        await ArmTicketAsync(harness);
        harness.Accessor.HttpContext = null;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.PerformChangePasswordAsync(Initial, New, New));

        Assert.Contains("IHttpContextAccessor", exception.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Authentication.SignOutCalls);
        Assert.Empty(harness.Authentication.AuthenticateCalls);
    }

    /// <summary>
    /// A ticket that carries an e-mail claim from an older version of the library still works: only the
    /// user-id claim is read. This is the compatibility half of the D-M44 removal.
    /// </summary>
    [Fact]
    public async Task PerformChangePasswordAsync_IgnoresExtraClaimsOnTheTicket()
    {
        using var harness = new MustChangePasswordTestHarness();
        var user = await harness.SeedUserAsync();
        harness.Authentication.NextAuthenticateResult = RecordingAuthenticationService.Ticket(
            Scheme,
            new Claim(ClaimTypes.Name, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim("OldPassword", Initial));

        await harness.Service.PerformChangePasswordAsync(Initial, New, New);

        Assert.True(await harness.UserManager.CheckPasswordAsync(user, New));
    }
}
