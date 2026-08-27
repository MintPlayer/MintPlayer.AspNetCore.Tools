using Microsoft.AspNetCore.Identity;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword.Fakes;

/// <summary>
/// A pass-through <see cref="IPasswordValidator{TUser}"/> that records the password it was asked to
/// validate.
/// </summary>
/// <remarks>
/// It exists to make one otherwise-invisible thing observable: whether
/// <c>UserManager.ChangePasswordAsync</c> was reached at all. Several of the service's failure paths
/// converge on the same <see cref="UnauthorizedAccessException"/>, so "which guard fired" cannot be
/// read from the exception — but it can be read from whether a password validation happened. It
/// always returns success so the real <c>PasswordValidator</c> stays in charge of the verdict.
/// </remarks>
internal sealed class RecordingPasswordValidator : IPasswordValidator<TestUser>
{
    public List<string?> ValidatedPasswords { get; } = [];

    public Task<IdentityResult> ValidateAsync(UserManager<TestUser> manager, TestUser user, string? password)
    {
        ValidatedPasswords.Add(password);
        return Task.FromResult(IdentityResult.Success);
    }
}
