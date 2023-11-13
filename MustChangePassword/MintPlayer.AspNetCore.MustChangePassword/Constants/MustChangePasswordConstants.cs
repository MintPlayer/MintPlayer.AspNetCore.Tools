namespace MintPlayer.AspNetCore.MustChangePassword.Constants;

public class MustChangePasswordConstants
{
    private const string CookiePrefix = "Identity";

    /// <summary>
    /// The scheme used for must-change-password cookies.
    /// </summary>
    public static readonly string MustChangePasswordScheme = CookiePrefix + ".ChangePassword";
}
