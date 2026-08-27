using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.MustChangePassword.Abstractions;
using MintPlayer.AspNetCore.MustChangePassword.Services;
using MintPlayer.AspNetCore.Tools.Tests.MustChangePassword.Fakes;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword;

public class AddMustChangePasswordServiceRegistrationTests
{
    /// <summary>Everything a consumer would have set up before reaching for this package.</summary>
    private static ServiceCollection IdentityOnly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<TestUserData>();
        services.AddIdentityCore<TestUser>().AddUserStore<TestUserStore>();
        return services;
    }

    [Fact]
    public void AddMustChangePassword_RegistersTheServiceAsScoped()
    {
        var services = new ServiceCollection();

        services.AddMustChangePassword<TestUser, string>();

        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(IMustChangePasswordService<TestUser, string>), descriptor.ServiceType);
        Assert.Equal(typeof(MustChangePasswordService<TestUser, string>), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AddMustChangePassword_ReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddMustChangePassword<TestUser, string>();

        Assert.Same(services, returned);
    }

    /// <summary>
    /// The registration is closed over the caller's <c>TUser</c>/<c>TKey</c>, so two different user
    /// types coexist. Cheap, but it is the reason the extension is generic at all.
    /// </summary>
    [Fact]
    public void AddMustChangePassword_ClosesTheGenericOverTheCallersUserType()
    {
        var services = new ServiceCollection();

        services.AddMustChangePassword<TestUser, string>();

        var descriptor = Assert.Single(services);
        Assert.False(descriptor.ServiceType.ContainsGenericParameters);
        Assert.False(descriptor.ImplementationType!.ContainsGenericParameters);
    }

    /// <summary>
    /// D-M22: <c>AddMustChangePassword</c> registers a service whose generated constructor takes
    /// <c>IHttpContextAccessor</c>, but never registers it. A consumer that follows the README and
    /// calls only <c>AddIdentityCore</c> + <c>AddMustChangePassword</c> therefore gets an
    /// unresolvable service — and, because the service is scoped and resolved per request, the
    /// failure lands at the first request rather than at startup.
    /// </summary>
    /// <remarks>
    /// Note this is not caught by <c>ValidateOnBuild</c> in a normal app either, since ASP.NET Core
    /// registers <c>IHttpContextAccessor</c> itself — the gap only bites the "identity core, no MVC"
    /// setups the package targets. The test asserts the dependency is genuinely missing from the
    /// collection, which is the part <c>AddMustChangePassword</c> owns.
    /// </remarks>
    [Fact]
    public void AddMustChangePassword_DoesNotRegisterIHttpContextAccessor_KnownBug()
    {
        var services = IdentityOnly();

        services.AddMustChangePassword<TestUser, string>();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHttpContextAccessor));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IMustChangePasswordService<TestUser, string>>());
        Assert.Contains(nameof(IHttpContextAccessor), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mirror of the bug above: add the one missing registration and the service resolves. This
    /// is what the fix in M9 must keep true, and it is what every other suite in this folder relies on.
    /// </summary>
    [Fact]
    public void AddMustChangePassword_ResolvesOnceIHttpContextAccessorIsRegistered()
    {
        var services = IdentityOnly();
        services.AddHttpContextAccessor();
        services.AddMustChangePassword<TestUser, string>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IMustChangePasswordService<TestUser, string>>();

        Assert.IsType<MustChangePasswordService<TestUser, string>>(service);
    }

    /// <summary>
    /// Pins the "double registration without a guard" entry in the PRD's robustness group: the
    /// extension uses <c>AddScoped</c>, not <c>TryAddScoped</c>, so calling it twice leaves two
    /// descriptors behind and the enumerable resolution reports two instances.
    /// </summary>
    [Fact]
    public void AddMustChangePassword_CalledTwice_LeavesTwoDescriptors_KnownGap()
    {
        var services = IdentityOnly();
        services.AddHttpContextAccessor();

        services.AddMustChangePassword<TestUser, string>();
        services.AddMustChangePassword<TestUser, string>();

        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IMustChangePasswordService<TestUser, string>)));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal(2, scope.ServiceProvider.GetServices<IMustChangePasswordService<TestUser, string>>().Count());
    }

    /// <summary>
    /// Shape pin (D-M31..D-M34 group): the implementation type is <c>internal</c>, so a consumer
    /// cannot subclass it, cannot register it under a second service type, and cannot resolve it
    /// concretely. Combined with the exception-only contract, overriding any part of the flow means
    /// reimplementing the whole interface.
    /// </summary>
    [Fact]
    public void MustChangePasswordService_IsInternal_KnownGap()
    {
        var type = typeof(MustChangePasswordService<TestUser, string>);

        Assert.False(type.IsPublic);
        Assert.False(type.IsVisible);
    }
}
