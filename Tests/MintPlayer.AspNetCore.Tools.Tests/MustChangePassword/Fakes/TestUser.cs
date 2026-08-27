using Microsoft.AspNetCore.Identity;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword.Fakes;

/// <summary>
/// The <c>TUser</c> used throughout the MustChangePassword suite.
/// </summary>
/// <remarks>
/// <para>
/// <c>string</c> is chosen for <c>TKey</c> deliberately: it satisfies
/// <c>TKey : IEquatable&lt;TKey&gt;</c> and keeps the store trivial, while still exercising the
/// real generic constraint the library declares.
/// </para>
/// <para>
/// The id is assigned in the constructor because the open generic <see cref="IdentityUser{TKey}"/>
/// — unlike the closed <c>IdentityUser</c> — does <b>not</b> seed one, and
/// <c>UserManager.GetUserIdAsync</c> would otherwise hand the service a null user id.
/// </para>
/// </remarks>
internal sealed class TestUser : IdentityUser<string>
{
    public TestUser() => Id = Guid.NewGuid().ToString("N");
}
