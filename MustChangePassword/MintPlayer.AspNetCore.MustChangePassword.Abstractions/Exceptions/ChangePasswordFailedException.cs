namespace MintPlayer.AspNetCore.MustChangePassword.Exceptions;

/// <summary>
/// Base type for every way the must-change-password flow can fail. Catch it to handle them all;
/// catch a derived type to tell them apart.
/// </summary>
/// <remarks>
/// The derived types map one-to-one onto the distinct outcomes a consumer has to render differently:
/// "your session expired, sign in again" (<see cref="ChangePasswordSessionExpiredException"/> and
/// <see cref="ChangePasswordSessionInvalidException"/>), "your account no longer exists"
/// (<see cref="ChangePasswordUserNotFoundException"/>), "that is not your current password"
/// (<see cref="IncorrectCurrentPasswordException"/>) and "that new password is not acceptable"
/// (<see cref="PasswordRejectedException"/>, which carries the reasons).
/// </remarks>
public abstract class ChangePasswordFailedException : Exception
{
    /// <summary>Creates the exception with the message the derived type documents.</summary>
    protected ChangePasswordFailedException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the underlying fault.</summary>
    protected ChangePasswordFailedException(string message, Exception? innerException) : base(message, innerException) { }
}
