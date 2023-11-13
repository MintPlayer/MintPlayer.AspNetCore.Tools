namespace MintPlayer.AspNetCore.MustChangePassword.Abstractions;

/// <summary>Interface used for the service which requires the user to change his password.</summary>
/// <typeparam name="TUser">Type of IdentityUser.</typeparam>
/// <typeparam name="TKey">Type of the ID of IdentityUser.</typeparam>
public interface IMustChangePasswordService<TUser, TKey>
    where TUser : Microsoft.AspNetCore.Identity.IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    Task ChangePasswordSignInAsync(TUser user, string oldPassword);
    Task PerformChangePasswordAsync(string newPassword, string newPasswordConfirmation);
}

