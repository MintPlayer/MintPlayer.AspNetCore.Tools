using Microsoft.AspNetCore.Identity;
using MintPlayer.AspNetCore.MustChangePassword.Abstractions;
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
/// The package is one interface with two abstract members and no executable statements, so there is
/// no behaviour to exercise — what it ships is a <i>contract</i>, and the only way it can break is
/// by changing shape. These are therefore reflection assertions on purpose, not for lack of a
/// better test: the whole value of the assembly is that a consumer compiled against it keeps
/// compiling.
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
    /// lets the implementation read <c>user.Email</c> without an extra abstraction, and
    /// <c>TKey : IEquatable&lt;TKey&gt;</c> is inherited from <c>IdentityUser&lt;TKey&gt;</c>'s own
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
    public void ChangePasswordSignInAsync_TakesTheUserAndTheOldPassword()
    {
        var method = Contract.GetMethod("ChangePasswordSignInAsync")!;
        var parameters = method.GetParameters();

        Assert.Equal(typeof(Task), method.ReturnType);
        Assert.Equal(2, parameters.Length);
        Assert.Equal(Contract.GetGenericArguments()[0], parameters[0].ParameterType);
        Assert.Equal("user", parameters[0].Name);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.Equal("oldPassword", parameters[1].Name);
    }

    [Fact]
    public void PerformChangePasswordAsync_TakesTheNewPasswordAndItsConfirmation()
    {
        var method = Contract.GetMethod("PerformChangePasswordAsync")!;
        var parameters = method.GetParameters();

        Assert.Equal(typeof(Task), method.ReturnType);
        Assert.Equal(2, parameters.Length);
        Assert.All(parameters, p => Assert.Equal(typeof(string), p.ParameterType));
        Assert.Equal(["newPassword", "newPasswordConfirmation"], parameters.Select(p => p.Name!).ToArray());
    }

    /// <summary>
    /// Shape gap (D-M31..D-M34 group): both members return a bare <see cref="Task"/>, so the contract
    /// can report an outcome only by throwing. Success is signalled by
    /// <see cref="MustChangePasswordException"/>, failure by a bare <c>Exception</c> or
    /// <see cref="UnauthorizedAccessException"/> (D-M27), and the <c>IdentityResult</c> the
    /// implementation already has in hand is discarded. A caller cannot render a useful message
    /// without catching and guessing.
    /// </summary>
    [Fact]
    public void Contract_ReportsOutcomesOnlyByThrowing_KnownGap()
    {
        Assert.All(Contract.GetMethods(), m =>
        {
            Assert.Equal(typeof(Task), m.ReturnType);
            Assert.False(m.ReturnType.IsGenericType, "a result-carrying Task<T> would let the caller branch without catching");
        });
    }

    /// <summary>
    /// Shape gap: <c>MustChangePasswordException</c> — the type a consumer is <i>required</i> to catch
    /// on the normal path — lives in the implementation package, not in <c>.Abstractions</c>. So a
    /// consumer that references only the abstraction can call the contract but cannot name the
    /// exception that its documented success path throws.
    /// </summary>
    [Fact]
    public void MustChangePasswordException_IsNotInTheAbstractionsPackage_KnownGap()
    {
        Assert.NotSame(Contract.Assembly, typeof(MustChangePasswordException).Assembly);
        Assert.DoesNotContain(Contract.Assembly.GetExportedTypes(), t => typeof(Exception).IsAssignableFrom(t));
    }

    /// <summary>
    /// Shape gap: the exception carries nothing — no message, no inner exception, no user id, and no
    /// constructor overloads. It cannot say <i>why</i> the password must be changed, and it cannot
    /// wrap an underlying fault.
    /// </summary>
    [Fact]
    public void MustChangePasswordException_HasOnlyADefaultConstructor_KnownGap()
    {
        var constructors = typeof(MustChangePasswordException).GetConstructors();

        var single = Assert.Single(constructors);
        Assert.Empty(single.GetParameters());
        Assert.Empty(typeof(MustChangePasswordException).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }

    /// <summary>
    /// The abstractions assembly is exactly one exported type. Pinned so that anything added to it
    /// later is a deliberate act — it is a published package, and every exported type is a
    /// permanent commitment.
    /// </summary>
    [Fact]
    public void AbstractionsAssembly_ExportsOnlyTheContract()
    {
        var exported = Contract.Assembly.GetExportedTypes();

        Assert.Equal([Contract], exported);
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
