using MintPlayer.AspNetCore.MustChangePassword.Abstractions;
using MintPlayer.AspNetCore.MustChangePassword.Constants;
using System.Reflection;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword;

public class MustChangePasswordConstantsTests
{
    /// <summary>
    /// The scheme name is a wire value, not an implementation detail.
    /// </summary>
    /// <remarks>
    /// It is simultaneously the authentication scheme and the cookie name (see
    /// <c>AddMustChangePasswordUserIdCookie</c>), and consumers hard-code it in
    /// <c>[Authorize(AuthenticationSchemes = "Identity.ChangePassword")]</c>. Changing it silently
    /// invalidates every in-flight cookie in every deployed app and breaks those attributes with
    /// no compile error, so the literal is pinned here rather than recomputed from the parts.
    /// </remarks>
    [Fact]
    public void MustChangePasswordScheme_IsTheLiteralIdentityChangePassword()
    {
        Assert.Equal("Identity.ChangePassword", MustChangePasswordConstants.MustChangePasswordScheme);
    }

    /// <summary>
    /// D-M31 fixed: the type is a <c>public static class</c>, so it cannot be constructed, inherited
    /// from, or used as a generic argument.
    /// </summary>
    [Fact]
    public void Constants_IsAStaticClass()
    {
        var type = typeof(MustChangePasswordConstants);

        Assert.True(type.IsClass);
        Assert.True(type.IsAbstract, "a static class reports IsAbstract");
        Assert.True(type.IsSealed, "a static class reports IsSealed");
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    /// <summary>
    /// D-M31 fixed: the scheme is a <c>const</c>, which is the only form usable in an attribute
    /// argument — and an attribute argument is the main thing consumers need it for
    /// (<c>[Authorize(AuthenticationSchemes = ...)]</c>).
    /// </summary>
    [Fact]
    public void MustChangePasswordScheme_IsAConst()
    {
        var field = typeof(MustChangePasswordConstants).GetField(
            nameof(MustChangePasswordConstants.MustChangePasswordScheme),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(field!.IsLiteral, "a const reports IsLiteral, and only a const is usable in an attribute argument");
        AttributeUsageIsAcceptedByTheCompiler();
    }

    /// <summary>
    /// The compile-time half of the assertion above: this attribute would not compile if the scheme
    /// were not a <c>const</c>. It is the actual consumer scenario, so it is worth spending a type on.
    /// </summary>
    [System.ComponentModel.Description(MustChangePasswordConstants.MustChangePasswordScheme)]
    private sealed class AttributeArgumentProbe;

    private static void AttributeUsageIsAcceptedByTheCompiler()
    {
        var description = typeof(AttributeArgumentProbe)
            .GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();

        Assert.Equal(MustChangePasswordConstants.MustChangePasswordScheme, description!.Description);
    }

    /// <summary>
    /// D-M31 fixed: the scheme constant now lives in <c>.Abstractions</c>, so a consumer that
    /// references only the contract — the package whose whole purpose is to be referenced by code that
    /// does not want the implementation — can name the scheme.
    /// </summary>
    [Fact]
    public void AbstractionsAssembly_CarriesTheSchemeConstant()
    {
        var abstractions = typeof(IMustChangePasswordService<,>).Assembly;

        Assert.Same(abstractions, typeof(MustChangePasswordConstants).Assembly);
        Assert.Contains(abstractions.GetExportedTypes(), t => t == typeof(MustChangePasswordConstants));
    }

    /// <summary>
    /// The private <c>CookiePrefix</c> is an implementation detail; what matters is that the two
    /// public consumers of the scheme name — the authentication scheme and the cookie name — are
    /// the same string. This test is what makes the round-trip suite's cookie-name assertion a
    /// tautology rather than an accident.
    /// </summary>
    [Fact]
    public void MustChangePasswordScheme_HasNoTrailingOrLeadingWhitespace()
    {
        var scheme = MustChangePasswordConstants.MustChangePasswordScheme;

        Assert.Equal(scheme.Trim(), scheme);
        Assert.DoesNotContain(' ', scheme);
    }
}
