using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using MintPlayer.AspNetCore.MustChangePassword.Abstractions;
using MintPlayer.AspNetCore.MustChangePassword.Constants;
using MintPlayer.AspNetCore.MustChangePassword.Exceptions;
using MintPlayer.SourceGenerators.Attributes;
using System.Security.Claims;

namespace MintPlayer.AspNetCore.MustChangePassword.Services;

/// <summary>
/// Default implementation of <see cref="IMustChangePasswordService{TUser, TKey}"/>.
/// </summary>
/// <remarks>
/// The change-password ticket carries the user id and nothing else. The current password is verified
/// on the way in and then discarded, and re-entered by the user on the change-password form — so no
/// credential is ever handed to the client, which is what a cookie-backed ticket amounts to.
/// </remarks>
internal partial class MustChangePasswordService<TUser, TKey> : IMustChangePasswordService<TUser, TKey>
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    [Inject] private readonly UserManager<TUser> userManager;
    [Inject] private readonly IHttpContextAccessor httpContextAccessor;

    /// <summary>
    /// Identity's own code for "that is not the current password", read from the describer rather than
    /// hard-coded, so it tracks the framework.
    /// </summary>
    private static readonly string PasswordMismatchCode = new IdentityErrorDescriber().PasswordMismatch().Code;

    public async Task ChangePasswordSignInAsync(TUser user, string currentPassword)
    {
        ArgumentNullException.ThrowIfNull(user);

        var httpContext = GetHttpContext();

        if (!await userManager.CheckPasswordAsync(user, currentPassword ?? string.Empty))
        {
            throw new IncorrectCurrentPasswordException();
        }

        var userId = await userManager.GetUserIdAsync(user);
        var identity = new ClaimsIdentity(MustChangePasswordConstants.MustChangePasswordScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, userId));

        await httpContext.SignInAsync(MustChangePasswordConstants.MustChangePasswordScheme, new ClaimsPrincipal(identity));

        throw new MustChangePasswordException();
    }

    public async Task PerformChangePasswordAsync(string currentPassword, string newPassword, string newPasswordConfirmation)
    {
        var httpContext = GetHttpContext();

        // Faults from here on propagate untouched: an unreachable store is not a reason to destroy
        // the ticket, so there is deliberately no blanket catch.
        var authentication = await httpContext.AuthenticateAsync(MustChangePasswordConstants.MustChangePasswordScheme);
        if (authentication?.Succeeded != true || authentication.Principal is null)
        {
            await EndFlowAsync(httpContext);
            throw new ChangePasswordSessionExpiredException();
        }

        var userId = authentication.Principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
        {
            await EndFlowAsync(httpContext);
            throw new ChangePasswordSessionInvalidException();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            await EndFlowAsync(httpContext);
            throw new ChangePasswordUserNotFoundException(userId);
        }

        // Recoverable input errors leave the ticket alone so the user can correct the form.
        var errors = new List<IdentityError>();
        if (string.IsNullOrEmpty(currentPassword))
        {
            errors.Add(new IdentityError
            {
                Code = PasswordRejectedException.CurrentPasswordRequiredCode,
                Description = "The current password is required.",
            });
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            errors.Add(new IdentityError
            {
                Code = PasswordRejectedException.PasswordRequiredCode,
                Description = "The new password is required.",
            });
        }
        else if (!string.Equals(newPassword, newPasswordConfirmation, StringComparison.Ordinal))
        {
            errors.Add(new IdentityError
            {
                Code = PasswordRejectedException.PasswordConfirmationMismatchCode,
                Description = "The new password and its confirmation do not match.",
            });
        }

        if (errors.Count > 0)
        {
            throw new PasswordRejectedException(errors);
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => string.Equals(e.Code, PasswordMismatchCode, StringComparison.Ordinal)))
            {
                throw new IncorrectCurrentPasswordException();
            }

            throw new PasswordRejectedException(result.Errors);
        }

        // The flow is over: the ticket must not outlive the password it was issued for.
        await EndFlowAsync(httpContext);
    }

    private HttpContext GetHttpContext() => httpContextAccessor.HttpContext ?? throw new InvalidOperationException(
        $"{nameof(IMustChangePasswordService<TUser, TKey>)} needs an active HTTP request, but IHttpContextAccessor.HttpContext is null. " +
        "The must-change-password flow cannot be driven from a background job or a hosted service.");

    private static Task EndFlowAsync(HttpContext httpContext)
        => httpContext.SignOutAsync(MustChangePasswordConstants.MustChangePasswordScheme);
}
