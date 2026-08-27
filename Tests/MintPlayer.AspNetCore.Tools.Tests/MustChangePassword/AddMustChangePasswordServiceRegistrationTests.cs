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

        var descriptor = services.Single(d => d.ServiceType == typeof(IMustChangePasswordService<TestUser, string>));
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

        var descriptor = services.Single(d => d.ServiceType == typeof(IMustChangePasswordService<TestUser, string>));
        Assert.False(descriptor.ServiceType.ContainsGenericParameters);
        Assert.False(descriptor.ImplementationType!.ContainsGenericParameters);
    }

    /// <summary>
    /// D-M22 fixed: the extension registers the <c>IHttpContextAccessor</c> its own service depends
    /// on, so a consumer that follows the README and calls only <c>AddIdentityCore</c> +
    /// <c>AddMustChangePassword</c> gets a resolvable service.
    /// </summary>
    /// <remarks>
    /// The failure this prevents was invisible in an MVC app — ASP.NET Core registers the accessor
    /// itself — and bit exactly the "identity core, no MVC" setups the package targets, at the first
    /// request rather than at startup, because the service is scoped.
    /// </remarks>
    [Fact]
    public void AddMustChangePassword_RegistersIHttpContextAccessor()
    {
        var services = IdentityOnly();

        services.AddMustChangePassword<TestUser, string>();

        Assert.Contains(services, d => d.ServiceType == typeof(IHttpContextAccessor));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IMustChangePasswordService<TestUser, string>>();

        Assert.IsType<MustChangePasswordService<TestUser, string>>(service);
    }

    /// <summary>
    /// Registering the accessor must not clobber one the application already registered — an app that
    /// swapped in its own accessor keeps it, because the extension uses <c>TryAdd</c> semantics.
    /// </summary>
    [Fact]
    public void AddMustChangePassword_LeavesAnExistingHttpContextAccessorRegistrationAlone()
    {
        var services = IdentityOnly();
        var accessor = new HttpContextAccessor();
        services.AddSingleton<IHttpContextAccessor>(accessor);

        services.AddMustChangePassword<TestUser, string>();

        using var provider = services.BuildServiceProvider();
        Assert.Same(accessor, provider.GetRequiredService<IHttpContextAccessor>());
    }

    /// <summary>
    /// The extension is idempotent: calling it twice leaves one descriptor and resolves one instance,
    /// so a library and an application can both call it without the consumer getting two services.
    /// </summary>
    [Fact]
    public void AddMustChangePassword_CalledTwice_RegistersTheServiceOnce()
    {
        var services = IdentityOnly();

        services.AddMustChangePassword<TestUser, string>();
        services.AddMustChangePassword<TestUser, string>();

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(IMustChangePasswordService<TestUser, string>)));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Single(scope.ServiceProvider.GetServices<IMustChangePasswordService<TestUser, string>>());
    }

    [Fact]
    public void AddMustChangePassword_ThrowsOnANullServiceCollection()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddMustChangePassword<TestUser, string>());
    }

    /// <summary>
    /// Shape pin: the implementation type is <c>internal</c>, so a consumer cannot subclass it and
    /// overriding any part of the flow means reimplementing the interface. Deliberate — the type has no
    /// extension points and every one of its decisions is a security decision — but pinned so making
    /// it public would be a conscious act.
    /// </summary>
    [Fact]
    public void MustChangePasswordService_IsInternal()
    {
        var type = typeof(MustChangePasswordService<TestUser, string>);

        Assert.False(type.IsPublic);
        Assert.False(type.IsVisible);
    }
}
