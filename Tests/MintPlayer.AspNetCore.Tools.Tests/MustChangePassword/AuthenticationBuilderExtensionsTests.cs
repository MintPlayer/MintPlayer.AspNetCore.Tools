using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.MustChangePassword.Constants;
using MintPlayer.AspNetCore.MustChangePassword.Extensions;
using System.Reflection;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword;

public class AuthenticationBuilderExtensionsTests
{
    private const string Scheme = "Identity.ChangePassword";

    /// <summary>
    /// Builds a container with only the change-password cookie registered.
    /// </summary>
    /// <remarks>
    /// <c>AddAuthentication()</c> already brings data protection and the web encoders that
    /// <c>PostConfigureCookieAuthenticationOptions</c> needs, so nothing else has to be wired to
    /// read the resulting options back.
    /// </remarks>
    private static ServiceProvider BuildProvider(Action<AuthenticationBuilder>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();

        var builder = services.AddAuthentication();
        builder.AddMustChangePasswordUserIdCookie();
        extra?.Invoke(builder);

        return services.BuildServiceProvider();
    }

    private static CookieAuthenticationOptions GetOptions(ServiceProvider provider, string scheme)
        => provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);

    [Fact]
    public async Task AddMustChangePasswordUserIdCookie_RegistersACookieSchemeUnderTheConstant()
    {
        using var provider = BuildProvider();

        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(Scheme);

        Assert.NotNull(scheme);
        Assert.Equal(typeof(CookieAuthenticationHandler), scheme!.HandlerType);
        Assert.Equal(MustChangePasswordConstants.MustChangePasswordScheme, scheme.Name);
    }

    /// <summary>
    /// The cookie name and the scheme name are the same string, which is what makes the browser-side
    /// cookie observable by name in the round-trip suite.
    /// </summary>
    [Fact]
    public void AddMustChangePasswordUserIdCookie_SetsCookieNameToTheSchemeName()
    {
        using var provider = BuildProvider();

        var options = GetOptions(provider, Scheme);

        Assert.Equal(Scheme, options.Cookie.Name);
    }

    [Fact]
    public void AddMustChangePasswordUserIdCookie_SetsAFiveMinuteExpiry()
    {
        using var provider = BuildProvider();

        var options = GetOptions(provider, Scheme);

        Assert.Equal(TimeSpan.FromMinutes(5), options.ExpireTimeSpan);
    }

    [Fact]
    public void AddMustChangePasswordUserIdCookie_ReturnsTheSameBuilderForChaining()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        var builder = services.AddAuthentication();

        var returned = builder.AddMustChangePasswordUserIdCookie();

        Assert.Same(builder, returned);
    }

    /// <summary>
    /// Registering the change-password cookie must not touch a scheme the application configured
    /// itself — the library is added <i>alongside</i> an app's own Identity cookies, so a shared
    /// <c>CookieAuthenticationOptions</c> mutation would be a cross-scheme bug.
    /// </summary>
    [Fact]
    public async Task AddMustChangePasswordUserIdCookie_LeavesOtherCookieSchemesAlone()
    {
        using var provider = BuildProvider(builder => builder.AddCookie("AppCookie", o =>
        {
            o.Cookie.Name = "App.Auth";
            o.ExpireTimeSpan = TimeSpan.FromHours(3);
        }));

        var appOptions = GetOptions(provider, "AppCookie");
        var ownOptions = GetOptions(provider, Scheme);
        var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();

        Assert.Equal("App.Auth", appOptions.Cookie.Name);
        Assert.Equal(TimeSpan.FromHours(3), appOptions.ExpireTimeSpan);
        Assert.Equal(Scheme, ownOptions.Cookie.Name);
        Assert.Equal(TimeSpan.FromMinutes(5), ownOptions.ExpireTimeSpan);
        Assert.Equal(2, schemes.Count());
    }

    /// <summary>
    /// D-M33: the extension configures nothing but the name and the expiry, so a cookie that carries
    /// the user's live credential (D-M23) inherits the framework defaults —
    /// <c>SameAsRequest</c>, meaning it will be sent over plain HTTP, and <c>Lax</c>. Pinned as the
    /// current defaults; changing them is a breaking behaviour change and needs the owner's call.
    /// </summary>
    [Fact]
    public void AddMustChangePasswordUserIdCookie_SetsNoSecurePolicyOrSameSiteOfItsOwn_KnownGap()
    {
        using var provider = BuildProvider();

        var options = GetOptions(provider, Scheme);
        var untouched = new CookieAuthenticationOptions();

        Assert.Equal(untouched.Cookie.SecurePolicy, options.Cookie.SecurePolicy);
        Assert.Equal(untouched.Cookie.SameSite, options.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
        Assert.True(options.Cookie.HttpOnly, "the framework default, not something the library sets");
    }

    /// <summary>
    /// D-M33: there is no overload taking an <c>Action&lt;CookieAuthenticationOptions&gt;</c>, so a
    /// consumer cannot tighten the cookie or shorten the window without re-registering the scheme
    /// by hand and duplicating the literal name.
    /// </summary>
    [Fact]
    public void AddMustChangePasswordUserIdCookie_HasNoConfigureOverload_KnownGap()
    {
        var overloads = typeof(AuthenticationBuilderExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(AuthenticationBuilderExtensions.AddMustChangePasswordUserIdCookie))
            .ToArray();

        var single = Assert.Single(overloads);
        var parameter = Assert.Single(single.GetParameters());
        Assert.Equal(typeof(AuthenticationBuilder), parameter.ParameterType);
    }

    /// <summary>
    /// Calling the extension twice throws, because <c>AddCookie</c> ultimately calls
    /// <c>AddScheme</c> and the scheme name is fixed. Worth pinning: the failure surfaces at
    /// <i>startup</i> with a message about a duplicate scheme, not at the call site, and an app that
    /// also registers <c>"Identity.ChangePassword"</c> itself hits the same wall.
    /// </summary>
    [Fact]
    public void AddMustChangePasswordUserIdCookie_CalledTwice_ThrowsOnDuplicateScheme()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        var builder = services.AddAuthentication();
        builder.AddMustChangePasswordUserIdCookie();
        builder.AddMustChangePasswordUserIdCookie();

        using var provider = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() =>
            provider.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(Scheme).GetAwaiter().GetResult());
    }
}
