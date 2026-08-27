using Microsoft.AspNetCore.Identity;

namespace MintPlayer.AspNetCore.MustChangePassword.Exceptions;

/// <summary>
/// The new password was refused: it was missing, its confirmation did not match, or Identity's own
/// validators rejected it. <see cref="Errors"/> carries the reasons, so a consumer can render
/// "your password needs a digit" instead of "unauthorized".
/// </summary>
/// <remarks>
/// This is a recoverable outcome — the change-password ticket stays valid so the user can correct the
/// form and try again.
/// </remarks>
public sealed class PasswordRejectedException : ChangePasswordFailedException
{
    /// <summary>Error code reported when the new password is missing.</summary>
    public const string PasswordRequiredCode = "PasswordRequired";

    /// <summary>Error code reported when the current password is missing.</summary>
    public const string CurrentPasswordRequiredCode = "CurrentPasswordRequired";

    /// <summary>Error code reported when the confirmation does not match the new password.</summary>
    public const string PasswordConfirmationMismatchCode = "PasswordConfirmationMismatch";

    /// <summary>Creates the exception carrying <paramref name="errors"/>.</summary>
    public PasswordRejectedException(IEnumerable<IdentityError> errors)
        : base(Describe(errors ?? []))
        => Errors = [.. errors ?? []];

    /// <summary>Creates the exception carrying <paramref name="errors"/> with a custom message.</summary>
    public PasswordRejectedException(IEnumerable<IdentityError> errors, string message)
        : base(message)
        => Errors = [.. errors ?? []];

    /// <summary>Every reason the new password was refused. Never null; may be empty.</summary>
    public IReadOnlyList<IdentityError> Errors { get; }

    private static string Describe(IEnumerable<IdentityError> errors)
    {
        var descriptions = errors
            .Select(e => string.IsNullOrEmpty(e.Description) ? e.Code : e.Description)
            .Where(d => !string.IsNullOrEmpty(d))
            .ToArray();

        return descriptions.Length == 0
            ? "The new password was rejected."
            : "The new password was rejected: " + string.Join(" ", descriptions);
    }
}
