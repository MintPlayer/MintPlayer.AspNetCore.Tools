using Microsoft.AspNetCore.Identity;
using MintPlayer.AspNetCore.MustChangePassword.Abstractions;
using MintPlayer.AspNetCore.MustChangePassword.Constants;
using MintPlayer.AspNetCore.MustChangePassword.Exceptions;
using MintPlayer.AspNetCore.MustChangePassword.Services;
using MintPlayer.AspNetCore.Tools.Tests.MustChangePassword.Fakes;
using System.Reflection;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword;

/// <summary>
/// Shape guards for <c>MintPlayer.AspNetCore.MustChangePassword.Abstractions</c>.
/// </summary>
/// <remarks>
/// What the package ships is a <i>contract</i>, and the way a contract breaks is by changing shape.
/// These are therefore reflection assertions on purpose, not for lack of a better test: the whole
/// value of the assembly is that a consumer compiled against it keeps compiling.
/// </remarks>
public class IMustChangePasswordServiceContractTests
{
    private static Type Contract => typeof(IMustChangePasswordService<,>);

    [Fact]
    public void Contract_IsAPublicInterfaceWithTwoTypeParameters()
    {
        Assert.True(Contract.IsInterface);
        Assert.True(Contract.IsPublic);
        Assert.Equal(2, Contract.GetGenericArguments().Length);
    }

    /// <summary>
    /// The constraints are the contract's real teeth: <c>TUser : IdentityUser&lt;TKey&gt;</c> is what
    /// lets the implementation talk to <c>UserManager&lt;TUser&gt;</c> without an extra abstraction,
    /// and <c>TKey : IEquatable&lt;TKey&gt;</c> is inherited from <c>IdentityUser&lt;TKey&gt;</c>'s own
    /// requirement. Relaxing either is a source-breaking change for every implementor.
    /// </summary>
    [Fact]
    public void Contract_ConstrainsTUserToIdentityUserOfTKey()
    {
        var arguments = Contract.GetGenericArguments();
        var user = arguments[0];
        var key = arguments[1];

        Assert.Equal("TUser", user.Name);
        Assert.Equal("TKey", key.Name);
        Assert.Contains(typeof(IdentityUser<>).MakeGenericType(key), user.GetGenericParameterConstraints());
        Assert.Contains(typeof(IEquatable<>).MakeGenericType(key), key.GetGenericParameterConstraints());
    }

    [Fact]
    public void Contract_DeclaresExactlyTwoMethods()
    {
        var methods = Contract.GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(["ChangePasswordSignInAsync", "PerformChangePasswordAsync"], methods);
    }

    [Fact]
    public void ChangePasswordSignInAsync_TakesTheUserAndTheCurrentPassword()
    {
        var method = Contract.GetMethod("ChangePasswordSignInAsync")!;
        var parameters = method.GetParameters();

        Assert.Equal(typeof(Task), method.ReturnType);
        Assert.Equal(2, parameters.Length);
        Assert.Equal(Contract.GetGenericArguments()[0], parameters[0].ParameterType);
        Assert.Equal("user", parameters[0].Name);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.Equal("currentPassword", parameters[1].Name);
    }

    /// <summary>
    /// The D-M23 redesign in the signature: the current password is a <i>parameter of the second
    /// request</i>, re-entered by the user, rather than something the library carried across the two
    /// requests in a cookie.
    /// </summary>
    [Fact]
    public void PerformChangePasswordAsync_TakesTheCurrentPasswordTheNewPasswordAndItsConfirmation()
    {
        var method = Contract.GetMethod("PerformChangePasswordAsync")!;
        var parameters = method.GetParameters();

        Assert.Equal(typeof(Task), method.ReturnType);
        Assert.All(parameters, p => Assert.Equal(typeof(string), p.ParameterType));
        Assert.Equal(["currentPassword", "newPassword", "newPasswordConfirmation"], parameters.Select(p => p.Name!).ToArray());
    }

    /// <summary>
    /// D-M27/D-M44 fixed: every exception the contract documents lives in the abstractions package, so
    /// a consumer referencing only the contract can name what it has to catch — including
    /// <see cref="MustChangePasswordException"/>, which the documented success path throws.
    /// </summary>
    [Fact]
    public void EveryDocumentedException_LivesInTheAbstractionsPackage()
    {
        Type[] expected =
        [
            typeof(MustChangePasswordException),
            typeof(ChangePasswordFailedException),
            typeof(ChangePasswordSessionExpiredException),
            typeof(ChangePasswordSessionInvalidException),
            typeof(ChangePasswordUserNotFoundException),
            typeof(IncorrectCurrentPasswordException),
            typeof(PasswordRejectedException),
        ];

        Assert.All(expected, t => Assert.Same(Contract.Assembly, t.Assembly));
    }

    /// <summary>
    /// D-M27 fixed: the five failure modes the service can hit are five distinct types under one
    /// catchable base, so a consumer can handle them together or tell them apart.
    /// </summary>
    [Fact]
    public void EveryFailure_DerivesFromOneCatchableBase()
    {
        Type[] failures =
        [
            typeof(ChangePasswordSessionExpiredException),
            typeof(ChangePasswordSessionInvalidException),
            typeof(ChangePasswordUserNotFoundException),
            typeof(IncorrectCurrentPasswordException),
            typeof(PasswordRejectedException),
        ];

        Assert.True(typeof(ChangePasswordFailedException).IsAbstract, "the base is not throwable on its own");
        Assert.All(failures, t =>
        {
            Assert.True(typeof(ChangePasswordFailedException).IsAssignableFrom(t));
            Assert.True(t.IsSealed, "a leaf outcome should not be extended");
        });
        Assert.Equal(failures.Length, failures.Distinct().Count());

        // The success signal is deliberately not one of them.
        Assert.False(typeof(ChangePasswordFailedException).IsAssignableFrom(typeof(MustChangePasswordException)));
    }

    /// <summary>
    /// D-M32 fixed: <see cref="MustChangePasswordException"/> gained message and inner-exception
    /// constructors, so it can say <i>why</i> and can wrap an underlying fault.
    /// </summary>
    [Fact]
    public void MustChangePasswordException_HasMessageAndInnerExceptionConstructors()
    {
        var signatures = typeof(MustChangePasswordException)
            .GetConstructors()
            .Select(c => string.Join(",", c.GetParameters().Select(p => p.ParameterType.Name)))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["", "String", "String,Exception"], signatures);

        var inner = new InvalidOperationException("root cause");
        var exception = new MustChangePasswordException("rotate it", inner);
        Assert.Equal("rotate it", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.NotEmpty(new MustChangePasswordException().Message);
    }

    /// <summary>
    /// D-M45 fixed: the rejection carries Identity's own <see cref="IdentityError"/>s, so a consumer
    /// can render "your password needs a digit" instead of "unauthorized".
    /// </summary>
    [Fact]
    public void PasswordRejectedException_CarriesTheIdentityErrors()
    {
        var errors = new[] { new IdentityError { Code = "PasswordTooShort", Description = "Too short." } };

        var exception = new PasswordRejectedException(errors);

        Assert.Equal("PasswordTooShort", Assert.Single(exception.Errors).Code);
        Assert.Contains("Too short.", exception.Message, StringComparison.Ordinal);
        Assert.Empty(new PasswordRejectedException([]).Errors);
    }

    /// <summary>
    /// Both members still return a bare <see cref="Task"/>, so the contract reports outcomes only by
    /// throwing. That is now a deliberate choice rather than a gap: the exception hierarchy above is
    /// the reporting channel, every outcome has a type, and the rejection carries its reasons — so a
    /// result object would only duplicate it. Pinned so a future <c>Task&lt;T&gt;</c> is a conscious
    /// break.
    /// </summary>
    [Fact]
    public void Contract_ReportsOutcomesByThrowingTypedExceptions()
    {
        Assert.All(Contract.GetMethods(), m =>
        {
            Assert.Equal(typeof(Task), m.ReturnType);
            Assert.False(m.ReturnType.IsGenericType);
        });
    }

    /// <summary>
    /// The abstractions assembly's exported surface, pinned. It is a published package, and every
    /// exported type is a permanent commitment — so anything added later has to be a deliberate act.
    /// </summary>
    [Fact]
    public void AbstractionsAssembly_ExportsTheContractTheConstantsAndTheExceptions()
    {
        var exported = Contract.Assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();

        Type[] expected =
        [
            typeof(IMustChangePasswordService<,>),
            typeof(MustChangePasswordConstants),
            typeof(ChangePasswordFailedException),
            typeof(ChangePasswordSessionExpiredException),
            typeof(ChangePasswordSessionInvalidException),
            typeof(ChangePasswordUserNotFoundException),
            typeof(IncorrectCurrentPasswordException),
            typeof(MustChangePasswordException),
            typeof(PasswordRejectedException),
        ];

        Assert.Equal(expected.OrderBy(t => t.FullName, StringComparer.Ordinal), exported);
    }

    /// <summary>
    /// The shipped implementation really does satisfy the contract for a concrete closed pair. Cheap,
    /// but it is the only assertion here that ties the two assemblies together rather than inspecting
    /// one of them alone.
    /// </summary>
    [Fact]
    public void ShippedImplementation_ImplementsTheClosedContract()
    {
        var closed = typeof(IMustChangePasswordService<TestUser, string>);

        Assert.True(closed.IsAssignableFrom(typeof(MustChangePasswordService<TestUser, string>)));
    }
}
