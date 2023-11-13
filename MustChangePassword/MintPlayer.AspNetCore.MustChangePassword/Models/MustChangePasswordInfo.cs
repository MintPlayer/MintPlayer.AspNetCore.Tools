namespace MintPlayer.AspNetCore.MustChangePassword.Models;

internal class MustChangePasswordInfo
{
    public string? CurrentPassword { get; set; }
    public string UserId { get; set; }
    public string Email { get; set; }
}
