using Microsoft.AspNetCore.Identity;
using MintPlayer.AspNetCore.MustChangePassword.Exceptions;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword;

/// <summary>
/// Covers the exception surface of the Abstractions package directly.
/// </summary>
/// <remarks>
/// <para>
/// The service tests reach these types through the flow that throws them, which only ever uses one
/// constructor each. The rest are public API of a contract package — a consumer catching, wrapping
/// or re-throwing them can call any of them — so they are exercised here rather than left as
/// untested surface. This is also what took the Abstractions package from 63% to full coverage;
/// the shortfall was entirely unused constructors.
/// </para>
/// <para>
/// The hierarchy itself is the contract: a caller must be able to catch
/// <see cref="ChangePasswordFailedException"/> and handle every failure mode, or catch one leaf and
/// handle exactly that one. Both are asserted.
/// </para>
/// </remarks>
public class ChangePasswordExceptionContractTests
{
    private static readonly Exception Inner = new InvalidOperationException("inner");

    [Fact]
    public void EveryFailureType_DerivesFromTheCommonBase()
    {
        Assert.All<Type>(
            [
                typeof(ChangePasswordSessionExpiredException),
                typeof(ChangePasswordSessionInvalidException),
                typeof(ChangePasswordUserNotFoundException),
                typeof(IncorrectCurrentPasswordException),
                typeof(PasswordRejectedException),
            ],
            type => Assert.True(
                typeof(ChangePasswordFailedException).IsAssignableFrom(type),
                $"{type.Name} must be catchable as ChangePasswordFailedException"));
    }

    /// <summary>
    /// The base is abstract, so "catch the base" cannot be defeated by someone throwing it
    /// directly with no failure mode attached.
    /// </summary>
    [Fact]
    public void CommonBase_IsAbstract() => Assert.True(typeof(ChangePasswordFailedException).IsAbstract);

    [Fact]
    public void EveryFailureType_IsSealed()
    {
        Assert.All<Type>(
            [
                typeof(ChangePasswordSessionExpiredException),
                typeof(ChangePasswordSessionInvalidException),
                typeof(ChangePasswordUserNotFoundException),
                typeof(IncorrectCurrentPasswordException),
                typeof(PasswordRejectedException),
            ],
            type => Assert.True(type.IsSealed, $"{type.Name} must be sealed"));
    }

    [Fact]
    public void SessionExpired_ParameterlessCtor_HasADefaultMessage()
        => Assert.False(string.IsNullOrWhiteSpace(new ChangePasswordSessionExpiredException().Message));

    [Fact]
    public void SessionExpired_MessageCtor_UsesTheMessage()
        => Assert.Equal("expired", new ChangePasswordSessionExpiredException("expired").Message);

    [Fact]
    public void SessionExpired_InnerExceptionCtor_KeepsBoth()
    {
        var ex = new ChangePasswordSessionExpiredException("expired", Inner);

        Assert.Equal("expired", ex.Message);
        Assert.Same(Inner, ex.InnerException);
    }

    [Fact]
    public void SessionInvalid_ParameterlessCtor_HasADefaultMessage()
        => Assert.False(string.IsNullOrWhiteSpace(new ChangePasswordSessionInvalidException().Message));

    [Fact]
    public void SessionInvalid_MessageCtor_UsesTheMessage()
        => Assert.Equal("invalid", new ChangePasswordSessionInvalidException("invalid").Message);

    [Fact]
    public void SessionInvalid_InnerExceptionCtor_KeepsBoth()
    {
        var ex = new ChangePasswordSessionInvalidException("invalid", Inner);

        Assert.Equal("invalid", ex.Message);
        Assert.Same(Inner, ex.InnerException);
    }

    /// <summary>
    /// The user id travels on the exception, so a handler can log or audit which account was
    /// involved without re-reading the ticket.
    /// </summary>
    [Fact]
    public void UserNotFound_CarriesTheUserId()
    {
        Assert.Equal("user-42", new ChangePasswordUserNotFoundException("user-42").UserId);
        Assert.Equal("user-42", new ChangePasswordUserNotFoundException("user-42", "gone").UserId);
    }

    [Fact]
    public void UserNotFound_MessageCtor_UsesTheMessage()
        => Assert.Equal("gone", new ChangePasswordUserNotFoundException("user-42", "gone").Message);

    [Fact]
    public void UserNotFound_UserIdOnlyCtor_HasADefaultMessage()
        => Assert.False(string.IsNullOrWhiteSpace(new ChangePasswordUserNotFoundException("user-42").Message));

    [Fact]
    public void IncorrectCurrentPassword_ParameterlessCtor_HasADefaultMessage()
        => Assert.False(string.IsNullOrWhiteSpace(new IncorrectCurrentPasswordException().Message));

    [Fact]
    public void IncorrectCurrentPassword_MessageCtor_UsesTheMessage()
        => Assert.Equal("nope", new IncorrectCurrentPasswordException("nope").Message);

    [Fact]
    public void IncorrectCurrentPassword_InnerExceptionCtor_KeepsBoth()
    {
        var ex = new IncorrectCurrentPasswordException("nope", Inner);

        Assert.Equal("nope", ex.Message);
        Assert.Same(Inner, ex.InnerException);
    }

    /// <summary>
    /// Identity's own errors travel on the exception, which is what lets a UI say "needs a digit"
    /// rather than "unauthorized".
    /// </summary>
    [Fact]
    public void PasswordRejected_CarriesTheIdentityErrors()
    {
        IdentityError[] errors =
        [
            new() { Code = "PasswordRequiresDigit", Description = "Passwords must have at least one digit." },
            new() { Code = "PasswordTooShort", Description = "Passwords must be at least 6 characters." },
        ];

        var ex = new PasswordRejectedException(errors);

        Assert.Equal(2, ex.Errors.Count);
        Assert.Contains(ex.Errors, error => error.Code == "PasswordRequiresDigit");
        Assert.Contains("digit", ex.Message);
    }

    [Fact]
    public void PasswordRejected_MessageCtor_UsesTheMessageAndKeepsTheErrors()
    {
        IdentityError[] errors = [new() { Code = "PasswordTooShort", Description = "too short" }];

        var ex = new PasswordRejectedException(errors, "rejected");

        Assert.Equal("rejected", ex.Message);
        Assert.Equal("PasswordTooShort", Assert.Single(ex.Errors).Code);
    }

    /// <summary>
    /// A null error collection is tolerated rather than throwing from a constructor.
    /// </summary>
    /// <remarks>
    /// Throwing while constructing the exception that reports a failure would replace a useful
    /// diagnostic with a confusing one, at the exact moment something has already gone wrong.
    /// </remarks>
    [Fact]
    public void PasswordRejected_NullErrors_YieldsAnEmptyCollection()
        => Assert.Empty(new PasswordRejectedException(null!).Errors);

    [Fact]
    public void PasswordRejected_ErrorsAreDefensivelyCopied()
    {
        var errors = new List<IdentityError> { new() { Code = "A", Description = "a" } };
        var ex = new PasswordRejectedException(errors);

        errors.Add(new IdentityError { Code = "B", Description = "b" });

        Assert.Single(ex.Errors);
    }

    /// <summary>The three well-known validation codes are part of the contract.</summary>
    [Fact]
    public void PasswordRejected_ExposesItsWellKnownCodes()
    {
        Assert.Equal("PasswordRequired", PasswordRejectedException.PasswordRequiredCode);
        Assert.Equal("CurrentPasswordRequired", PasswordRejectedException.CurrentPasswordRequiredCode);
        Assert.Equal("PasswordConfirmationMismatch", PasswordRejectedException.PasswordConfirmationMismatchCode);
    }

    // MustChangePasswordException is the signal the flow raises to redirect the user, not a
    // failure — so it deliberately does not derive from ChangePasswordFailedException.

    [Fact]
    public void MustChangePassword_ParameterlessCtor_HasADefaultMessage()
        => Assert.False(string.IsNullOrWhiteSpace(new MustChangePasswordException().Message));

    [Fact]
    public void MustChangePassword_MessageCtor_UsesTheMessage()
        => Assert.Equal("change it", new MustChangePasswordException("change it").Message);

    [Fact]
    public void MustChangePassword_InnerExceptionCtor_KeepsBoth()
    {
        var ex = new MustChangePasswordException("change it", Inner);

        Assert.Equal("change it", ex.Message);
        Assert.Same(Inner, ex.InnerException);
    }

    [Fact]
    public void MustChangePassword_IsNotAFailure()
        => Assert.False(typeof(ChangePasswordFailedException).IsAssignableFrom(typeof(MustChangePasswordException)));
}
