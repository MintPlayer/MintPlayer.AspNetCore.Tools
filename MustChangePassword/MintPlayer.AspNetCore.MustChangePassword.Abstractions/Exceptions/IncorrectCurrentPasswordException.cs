namespace MintPlayer.AspNetCore.MustChangePassword.Exceptions;

/// <summary>
/// The supplied current password is not the user's password. Thrown by
/// <see cref="Abstractions.IMustChangePasswordService{TUser, TKey}.ChangePasswordSignInAsync"/> before
/// any ticket is issued, and by
/// <see cref="Abstractions.IMustChangePasswordService{TUser, TKey}.PerformChangePasswordAsync"/> when
/// the re-entered current password does not verify.
/// </summary>
/// <remarks>
/// The change-password ticket is deliberately <i>not</i> revoked when this is thrown from
/// <c>PerformChangePasswordAsync</c>: the holder of the ticket already proved knowledge of the
/// password once, so the overwhelmingly likely cause is a typo, and destroying the ticket would force
/// a full re-authentication for it.
/// </remarks>
public sealed class IncorrectCurrentPasswordException : ChangePasswordFailedException
{
    /// <summary>Creates the exception with the default message.</summary>
    public IncorrectCurrentPasswordException()
        : base("The supplied current password is incorrect.") { }

    /// <summary>Creates the exception with a custom message.</summary>
    public IncorrectCurrentPasswordException(string message) : base(message) { }

    /// <summary>Creates the exception with a custom message and the underlying fault.</summary>
    public IncorrectCurrentPasswordException(string message, Exception? innerException) : base(message, innerException) { }
}
