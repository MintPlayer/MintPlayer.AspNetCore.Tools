using Microsoft.AspNetCore.Identity;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword.Fakes;

/// <summary>
/// The backing storage for <see cref="TestUserStore"/>, registered as a singleton so a test can
/// seed and inspect users while the store itself stays scoped, exactly as
/// <c>AddUserStore&lt;T&gt;()</c> registers it.
/// </summary>
internal sealed class TestUserData
{
    public List<TestUser> Users { get; } = [];

    /// <summary>
    /// When set, <see cref="TestUserStore.FindByIdAsync"/> throws it.
    /// </summary>
    /// <remarks>
    /// Models a transient infrastructure failure (database unreachable) as opposed to a rejected
    /// credential — the distinction D-M28 says the service fails to make.
    /// </remarks>
    public Exception? FindByIdFault { get; set; }
}

/// <summary>
/// A hand-written Identity store, deliberately not a mock.
/// </summary>
/// <remarks>
/// <para>
/// Per PRD R3.4: <c>UserManager&lt;TUser&gt;</c> has a nine-parameter constructor, and mocking it
/// is the classic source of brittle Identity tests. Registering this store behind a real
/// <c>UserManager</c> gives the suite genuine <c>CheckPasswordAsync</c>,
/// <c>ChangePasswordAsync</c>, password-hasher and validator behaviour — so a test that says "the
/// password really changed" means it.
/// </para>
/// <para>
/// It deliberately does <b>not</b> implement <c>IUserSecurityStampStore</c>: with
/// <c>SupportsUserSecurityStamp</c> false the manager skips stamp maintenance, which is noise for
/// everything under test here.
/// </para>
/// </remarks>
internal sealed class TestUserStore(TestUserData data) : IUserStore<TestUser>, IUserPasswordStore<TestUser>, IUserEmailStore<TestUser>
{
    public Task<string> GetUserIdAsync(TestUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(TestUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);

    public Task SetUserNameAsync(TestUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(TestUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(TestUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task<IdentityResult> CreateAsync(TestUser user, CancellationToken cancellationToken)
    {
        data.Users.Add(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(TestUser user, CancellationToken cancellationToken)
    {
        if (!data.Users.Contains(user))
        {
            data.Users.Add(user);
        }

        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> DeleteAsync(TestUser user, CancellationToken cancellationToken)
    {
        data.Users.Remove(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<TestUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        if (data.FindByIdFault is not null)
        {
            throw data.FindByIdFault;
        }

        return Task.FromResult(data.Users.SingleOrDefault(u => u.Id == userId));
    }

    public Task<TestUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        => Task.FromResult(data.Users.SingleOrDefault(u => u.NormalizedUserName == normalizedUserName));

    public Task SetPasswordHashAsync(TestUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(TestUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(TestUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash is not null);

    public Task SetEmailAsync(TestUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(TestUser user, CancellationToken cancellationToken) => Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(TestUser user, CancellationToken cancellationToken) => Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(TestUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task<TestUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        => Task.FromResult(data.Users.SingleOrDefault(u => u.NormalizedEmail == normalizedEmail));

    public Task<string?> GetNormalizedEmailAsync(TestUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(TestUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
