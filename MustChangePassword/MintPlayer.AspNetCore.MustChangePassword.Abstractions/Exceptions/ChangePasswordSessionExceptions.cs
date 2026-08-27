namespace MintPlayer.AspNetCore.MustChangePassword.Exceptions;

/// <summary>
/// There is no change-password ticket on the request at all: the cookie was never issued, it was
/// already consumed, or it has expired. The user has to sign in again.
/// </summary>
public sealed class ChangePasswordSessionExpiredException : ChangePasswordFailedException
{
    /// <summary>Creates the exception with the default message.</summary>
    public ChangePasswordSessionExpiredException()
        : base("There is no change-password session on this request. Sign in again to restart the flow.") { }

    /// <summary>Creates the exception with a custom message.</summary>
    public ChangePasswordSessionExpiredException(string message) : base(message) { }

    /// <summary>Creates the exception with a custom message and the underlying fault.</summary>
    public ChangePasswordSessionExpiredException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// A change-password ticket was present but does not carry a user id, so it cannot be acted on.
/// Distinct from <see cref="ChangePasswordSessionExpiredException"/>: the cookie authenticated, which
/// means it was issued by something other than this library, or by an incompatible version of it.
/// </summary>
public sealed class ChangePasswordSessionInvalidException : ChangePasswordFailedException
{
    /// <summary>Creates the exception with the default message.</summary>
    public ChangePasswordSessionInvalidException()
        : base("The change-password ticket carries no user id and cannot be used.") { }

    /// <summary>Creates the exception with a custom message.</summary>
    public ChangePasswordSessionInvalidException(string message) : base(message) { }

    /// <summary>Creates the exception with a custom message and the underlying fault.</summary>
    public ChangePasswordSessionInvalidException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// The user id in the change-password ticket no longer resolves to a user — the account was deleted
/// or renamed while the flow was in progress.
/// </summary>
public sealed class ChangePasswordUserNotFoundException : ChangePasswordFailedException
{
    /// <summary>Creates the exception for <paramref name="userId"/>.</summary>
    public ChangePasswordUserNotFoundException(string userId)
        : base($"The change-password ticket refers to user '{userId}', which no longer exists.")
        => UserId = userId;

    /// <summary>Creates the exception for <paramref name="userId"/> with a custom message.</summary>
    public ChangePasswordUserNotFoundException(string userId, string message) : base(message) => UserId = userId;

    /// <summary>The user id that could not be resolved.</summary>
    public string UserId { get; }
}
