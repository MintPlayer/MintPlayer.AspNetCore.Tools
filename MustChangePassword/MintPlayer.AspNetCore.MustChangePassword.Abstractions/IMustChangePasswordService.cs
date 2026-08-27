using MintPlayer.AspNetCore.MustChangePassword.Exceptions;

namespace MintPlayer.AspNetCore.MustChangePassword.Abstractions;

/// <summary>Interface used for the service which requires the user to change his password.</summary>
/// <typeparam name="TUser">Type of IdentityUser.</typeparam>
/// <typeparam name="TKey">Type of the ID of IdentityUser.</typeparam>
/// <remarks>
/// The flow is two requests. The first calls <see cref="ChangePasswordSignInAsync"/> instead of
/// completing sign-in; it issues a short-lived ticket carrying <b>nothing but the user id</b> and
/// throws <see cref="MustChangePasswordException"/> so the caller redirects to its change-password
/// page. The second calls <see cref="PerformChangePasswordAsync"/> with the current password
/// re-entered by the user, plus the new password and its confirmation. No credential is ever stored in
/// the ticket.
/// </remarks>
public interface IMustChangePasswordService<TUser, TKey>
    where TUser : Microsoft.AspNetCore.Identity.IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    /// <summary>
    /// Verifies <paramref name="currentPassword"/>, issues the change-password ticket for
    /// <paramref name="user"/> and then throws <see cref="MustChangePasswordException"/> to halt the
    /// sign-in the caller was performing.
    /// </summary>
    /// <param name="user">The user who must change his password.</param>
    /// <param name="currentPassword">
    /// The password the user just authenticated with. It is verified against the store and then
    /// discarded — it is never written to the ticket.
    /// </param>
    /// <exception cref="MustChangePasswordException">Always, once the ticket has been issued. This is the success signal.</exception>
    /// <exception cref="IncorrectCurrentPasswordException"><paramref name="currentPassword"/> is not the user's password; no ticket is issued.</exception>
    /// <exception cref="InvalidOperationException">There is no active HTTP request.</exception>
    Task ChangePasswordSignInAsync(TUser user, string currentPassword);

    /// <summary>
    /// Changes the password of the user named by the change-password ticket, and ends the flow.
    /// </summary>
    /// <param name="currentPassword">The current password, as re-entered by the user on the change-password form.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="newPasswordConfirmation">The new password, typed a second time.</param>
    /// <exception cref="ChangePasswordSessionExpiredException">No change-password ticket is present.</exception>
    /// <exception cref="ChangePasswordSessionInvalidException">The ticket carries no user id.</exception>
    /// <exception cref="ChangePasswordUserNotFoundException">The ticket's user no longer exists.</exception>
    /// <exception cref="IncorrectCurrentPasswordException"><paramref name="currentPassword"/> is not the user's password.</exception>
    /// <exception cref="PasswordRejectedException">The new password is missing, unconfirmed, or refused by Identity.</exception>
    /// <exception cref="InvalidOperationException">There is no active HTTP request.</exception>
    Task PerformChangePasswordAsync(string currentPassword, string newPassword, string newPasswordConfirmation);
}
