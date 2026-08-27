namespace MintPlayer.AspNetCore.MustChangePassword.Constants;

/// <summary>Wire values shared by the library and its consumers.</summary>
public static class MustChangePasswordConstants
{
    private const string CookiePrefix = "Identity";

    /// <summary>
    /// The authentication scheme — and the cookie name — used for the must-change-password flow.
    /// </summary>
    /// <remarks>
    /// A <see langword="const"/> on purpose: consumers need it in an attribute argument, as in
    /// <c>[Authorize(AuthenticationSchemes = MustChangePasswordConstants.MustChangePasswordScheme)]</c>.
    /// It lives in the abstractions package so that code referencing only the contract can name it.
    /// The literal is a wire value: changing it invalidates every in-flight cookie in a deployed app.
    /// </remarks>
    public const string MustChangePasswordScheme = CookiePrefix + ".ChangePassword";
}
