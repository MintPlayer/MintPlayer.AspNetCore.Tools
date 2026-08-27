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
    /// D-M31: <c>MustChangePasswordConstants</c> is a plain instantiable <c>public class</c> rather
    /// than a <c>public static class</c>, so it can be constructed, inherited from and used as a
    /// generic argument. Pinned because fixing it is a breaking change and needs the owner's call.
    /// </summary>
    [Fact]
    public void Constants_IsAPlainInstantiableClass_KnownGap()
    {
        var type = typeof(MustChangePasswordConstants);

        Assert.True(type.IsClass);
        Assert.False(type.IsAbstract, "a static class would report IsAbstract");
        Assert.False(type.IsSealed, "a static class would report IsSealed");
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
    }

    /// <summary>
    /// D-M31: the scheme is a <c>static readonly</c> field, not a <c>const</c>, so it cannot be used
    /// in an attribute argument — which is exactly where consumers need it
    /// (<c>[Authorize(AuthenticationSchemes = ...)]</c>) and why they end up retyping the literal.
    /// </summary>
    [Fact]
    public void MustChangePasswordScheme_IsStaticReadonlyNotConst_KnownGap()
    {
        var field = typeof(MustChangePasswordConstants).GetField(
            nameof(MustChangePasswordConstants.MustChangePasswordScheme),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(field!.IsInitOnly, "readonly");
        Assert.False(field.IsLiteral, "a const would report IsLiteral, and only a const is usable in an attribute argument");
    }

    /// <summary>
    /// D-M31/D-M34 (shape): the scheme name lives in the implementation package, not in
    /// <c>.Abstractions</c>. A consumer that references only the abstraction — the package whose
    /// whole purpose is to be referenced by code that does not want the implementation — has no way
    /// to name the scheme. Pinned as the PRD's "move the constant" decision item.
    /// </summary>
    [Fact]
    public void AbstractionsAssembly_DoesNotCarryTheSchemeConstant_KnownGap()
    {
        var abstractions = typeof(IMustChangePasswordService<,>).Assembly;

        Assert.DoesNotContain(
            abstractions.GetExportedTypes(),
            t => t.Name.Contains("Constants", StringComparison.Ordinal));
        Assert.NotSame(abstractions, typeof(MustChangePasswordConstants).Assembly);
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
