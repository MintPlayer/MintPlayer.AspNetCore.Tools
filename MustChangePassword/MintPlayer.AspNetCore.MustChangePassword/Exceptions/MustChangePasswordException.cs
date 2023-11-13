namespace MintPlayer.AspNetCore.MustChangePassword.Exceptions;

/// <summary>
/// Thrown when the <see cref="MustChangePassword.Services.MustChangePasswordService{TUser, TKey}"/> requires the user to change his password before he's able to continue.
/// </summary>
public class MustChangePasswordException : Exception
{
}
