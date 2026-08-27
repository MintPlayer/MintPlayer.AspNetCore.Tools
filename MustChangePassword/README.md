# MintPlayer.AspNetCore.MustChangePassword

[![License](https://img.shields.io/badge/License-Apache%202.0-green.svg)](https://opensource.org/licenses/Apache-2.0)

ASP.NET Core services that force a user to change his password before he can sign in — for a
first login with a generated password, an administrative reset, or an expired-password policy.

The user's password is never stored anywhere but the Identity store: the flow carries only the
user id, in a short-lived `HttpOnly` cookie, and asks the user to re-enter his current password on
the change-password form.

## Version info
| Package                                               | Release                                                                                                                                                                                                       | Preview                                                                                                                                                                                                          | Downloads |
|-------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------|
| MintPlayer.AspNetCore.MustChangePassword              | [![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.AspNetCore.MustChangePassword.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.MustChangePassword)                           | [![NuGet Version](https://img.shields.io/nuget/vpre/MintPlayer.AspNetCore.MustChangePassword.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.MustChangePassword)                           | [![NuGet](https://img.shields.io/nuget/dt/MintPlayer.AspNetCore.MustChangePassword.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.MustChangePassword) |
| MintPlayer.AspNetCore.MustChangePassword.Abstractions | [![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.AspNetCore.MustChangePassword.Abstractions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.MustChangePassword.Abstractions) | [![NuGet Version](https://img.shields.io/nuget/vpre/MintPlayer.AspNetCore.MustChangePassword.Abstractions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.MustChangePassword.Abstractions) | [![NuGet](https://img.shields.io/nuget/dt/MintPlayer.AspNetCore.MustChangePassword.Abstractions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.MustChangePassword.Abstractions) |

## Installation
### NuGet package manager
Open the NuGet package manager and install `MintPlayer.AspNetCore.MustChangePassword` in your project
### Package manager console
Install-Package MintPlayer.AspNetCore.MustChangePassword

## Usage

### Registration

```csharp
builder.Services.AddIdentityCore<ApplicationUser>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddMustChangePassword<ApplicationUser, Guid>();

builder.Services.AddAuthentication()
    .AddMustChangePasswordUserIdCookie();
```

`AddMustChangePassword` registers `IMustChangePasswordService<TUser, TKey>` (scoped) and the
`IHttpContextAccessor` it depends on. Both extensions are idempotent.

`AddMustChangePasswordUserIdCookie` registers a cookie scheme named
`MustChangePasswordConstants.MustChangePasswordScheme` (`"Identity.ChangePassword"`) with a five-minute
lifetime on both the server ticket and the browser (`max-age`), `HttpOnly`, `SameSite=Strict` and
`SecurePolicy=Always`. A host served over plain HTTP has to relax the last one:

```csharp
builder.Services.AddAuthentication()
    .AddMustChangePasswordUserIdCookie(options =>
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(2);
        options.Cookie.MaxAge = options.ExpireTimeSpan;
    });
```

### Step 1 — halt sign-in

Where you would have completed sign-in, call `ChangePasswordSignInAsync` instead. It verifies the
password, issues the change-password ticket and then throws `MustChangePasswordException` — the
exception *is* the success signal, so the flow cannot silently continue as an authenticated user.

```csharp
try
{
    if (user.MustChangePassword)
    {
        await mustChangePassword.ChangePasswordSignInAsync(user, model.Password);
    }

    await signInManager.SignInAsync(user, isPersistent: false);
    return LocalRedirect(returnUrl);
}
catch (MustChangePasswordException)
{
    return RedirectToAction(nameof(ChangePassword));
}
catch (IncorrectCurrentPasswordException)
{
    ModelState.AddModelError(string.Empty, "Invalid credentials.");
    return View(model);
}
```

The ticket contains the user id and nothing else. The password is verified against the store and then
discarded.

### Step 2 — change the password

The change-password page is reached with only the ticket, so protect it with the scheme constant:

```csharp
[Authorize(AuthenticationSchemes = MustChangePasswordConstants.MustChangePasswordScheme)]
public class ChangePasswordController : Controller
{
    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        try
        {
            await mustChangePassword.PerformChangePasswordAsync(
                model.CurrentPassword, model.NewPassword, model.NewPasswordConfirmation);

            return RedirectToAction("Index", "Login");
        }
        catch (PasswordRejectedException ex)
        {
            // The ticket is still valid: the user can correct the form.
            foreach (var error in ex.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }
        catch (IncorrectCurrentPasswordException)
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "That is not your current password.");
            return View(model);
        }
        catch (ChangePasswordFailedException)
        {
            // Session expired, or the account is gone: the ticket has been revoked.
            return RedirectToAction("Index", "Login");
        }
    }
}
```

On success the change-password cookie is signed out, so the ticket cannot be replayed.

### Exceptions

Every failure derives from `ChangePasswordFailedException`, so a single `catch` handles them all while
the derived types stay distinguishable. All of them, and the scheme constant, live in
`MintPlayer.AspNetCore.MustChangePassword.Abstractions` — code referencing only the contract can name
what it must catch.

| Exception | Meaning | Ticket |
|---|---|---|
| `MustChangePasswordException` | Not a failure: the ticket was issued, redirect the user. | issued |
| `ChangePasswordSessionExpiredException` | No change-password ticket on the request. | revoked |
| `ChangePasswordSessionInvalidException` | A ticket authenticated but carries no user id. | revoked |
| `ChangePasswordUserNotFoundException` | The ticket's user no longer exists (carries `UserId`). | revoked |
| `IncorrectCurrentPasswordException` | The supplied current password does not verify. | kept |
| `PasswordRejectedException` | The new password is missing, unconfirmed, or refused by Identity — `Errors` carries the `IdentityError`s. | kept |

A transient infrastructure fault (an unreachable store) is never translated and never revokes the
ticket: it propagates as thrown.
