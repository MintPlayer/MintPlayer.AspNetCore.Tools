using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using MintPlayer.AspNetCore.MustChangePassword.Abstractions;
using System.Security.Claims;

namespace MintPlayer.AspNetCore.MustChangePassword.Services;

internal class MustChangePasswordService<TUser, TKey> : IMustChangePasswordService<TUser, TKey>
    where TUser : Microsoft.AspNetCore.Identity.IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    #region Constructor
    private readonly UserManager<TUser> userManager;
    private readonly IHttpContextAccessor httpContextAccessor;
    public MustChangePasswordService(UserManager<TUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        this.userManager = userManager;
        this.httpContextAccessor = httpContextAccessor;
    }
    #endregion

    public async Task ChangePasswordSignInAsync(TUser user, string oldPassword)
    {
        var userId = await userManager.GetUserIdAsync(user);
        var principal = await StoreMustChangePasswordInfo(userId, user.Email, oldPassword);
        await httpContextAccessor.HttpContext.SignInAsync(Constants.MustChangePasswordConstants.MustChangePasswordScheme, principal);
        throw new Exceptions.MustChangePasswordException();
    }

    public async Task PerformChangePasswordAsync(string newPassword, string newPasswordConfirmation)
    {
        try
        {
            var info = await RetrieveMustChangePasswordInfo();
            if (info == null || info.UserId == null)
            {
                throw new Exception();
            }

            var user = await userManager.FindByIdAsync(info.UserId);
            if (user == null)
            {
                throw new Exception();
            }

            var isPasswordCorrect = await userManager.CheckPasswordAsync(user, info.CurrentPassword);
            if (!isPasswordCorrect)
            {
                throw new Exception();
            }

            if (newPassword != newPasswordConfirmation)
            {
                throw new UnauthorizedAccessException();
            }

            var result = await userManager.ChangePasswordAsync(user, info.CurrentPassword, newPassword);
            if (!result.Succeeded)
            {
                throw new UnauthorizedAccessException();
            }
        }
        catch (Exception)
        {
            await httpContextAccessor.HttpContext.SignOutAsync(Constants.MustChangePasswordConstants.MustChangePasswordScheme);
            throw;
        }
    }

    private Task<ClaimsPrincipal> StoreMustChangePasswordInfo(string userId, string email, string oldPassword)
    {
        var identity = new ClaimsIdentity(Constants.MustChangePasswordConstants.MustChangePasswordScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, userId));
        identity.AddClaim(new Claim(ClaimTypes.Email, email));
        if (!string.IsNullOrEmpty(oldPassword))
        {
            identity.AddClaim(new Claim("OldPassword", oldPassword));
        }
        return Task.FromResult(new ClaimsPrincipal(identity));
    }

    private async Task<Models.MustChangePasswordInfo?> RetrieveMustChangePasswordInfo()
    {
        var result = await httpContextAccessor.HttpContext.AuthenticateAsync(Constants.MustChangePasswordConstants.MustChangePasswordScheme);
        if (result?.Principal == null)
        {
            return null;
        }
        else
        {
            var userId = result.Principal.FindFirstValue(ClaimTypes.Name);
            var email = result.Principal.FindFirstValue(ClaimTypes.Email);
            var currentPassword = result.Principal.FindFirstValue("OldPassword");
            return new Models.MustChangePasswordInfo
            {
                UserId = userId,
                Email = email,
                CurrentPassword = currentPassword,
            };
        }
    }

}
