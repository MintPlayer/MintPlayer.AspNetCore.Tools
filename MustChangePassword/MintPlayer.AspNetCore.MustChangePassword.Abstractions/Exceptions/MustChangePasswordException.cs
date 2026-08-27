namespace MintPlayer.AspNetCore.MustChangePassword.Exceptions;

/// <summary>
/// Thrown by <see cref="Abstractions.IMustChangePasswordService{TUser, TKey}.ChangePasswordSignInAsync"/>
/// once the change-password ticket has been issued, to stop the surrounding sign-in flow.
/// </summary>
/// <remarks>
/// This is the <b>success</b> signal of that method: the ticket exists and the caller should redirect
/// the user to its change-password page. Every genuine failure derives from
/// <see cref="ChangePasswordFailedException"/> instead.
/// </remarks>
public class MustChangePasswordException : Exception
{
    /// <summary>Creates the exception with the default message.</summary>
    public MustChangePasswordException() : base("The user must change his password before he can continue.") { }

    /// <summary>Creates the exception with an explanation of why the password must be changed.</summary>
    public MustChangePasswordException(string message) : base(message) { }

    /// <summary>Creates the exception with an explanation and the underlying fault.</summary>
    public MustChangePasswordException(string message, Exception? innerException) : base(message, innerException) { }
}
