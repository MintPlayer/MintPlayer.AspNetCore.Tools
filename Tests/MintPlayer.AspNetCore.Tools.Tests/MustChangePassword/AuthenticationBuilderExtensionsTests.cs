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

    /// <summary>
    /// D-M41 fixed: <c>ExpireTimeSpan</c> bounds the server-side ticket only, so the browser was told
    /// nothing and kept the cookie for the whole browsing session. <c>Cookie.MaxAge</c> puts the same
    /// five minutes on the wire.
    /// </summary>
    [Fact]
    public void AddMustChangePasswordUserIdCookie_BoundsTheBrowserSideLifetimeToo()
    {
        using var provider = BuildProvider();

        var options = GetOptions(provider, Scheme);

        Assert.Equal(options.ExpireTimeSpan, options.Cookie.MaxAge);
    }

    /// <summary>
    /// D-M33 fixed: the flow's cookie is explicitly hardened rather than left on the framework
    /// defaults — <c>SameAsRequest</c> (which would send it over plain HTTP) and <c>Lax</c>.
    /// </summary>
    [Fact]
    public void AddMustChangePasswordUserIdCookie_SetsSafeCookieDefaults()
    {
        using var provider = BuildProvider();

        var options = GetOptions(provider, Scheme);

        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Strict, options.Cookie.SameSite);
        Assert.True(options.Cookie.HttpOnly);

        var untouched = new CookieAuthenticationOptions();
        Assert.NotEqual(untouched.Cookie.SecurePolicy, options.Cookie.SecurePolicy);
        Assert.NotEqual(untouched.Cookie.SameSite, options.Cookie.SameSite);
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
        Assert.Equal(CookieSecurePolicy.SameAsRequest, appOptions.Cookie.SecurePolicy);
        Assert.Equal(Scheme, ownOptions.Cookie.Name);
        Assert.Equal(TimeSpan.FromMinutes(5), ownOptions.ExpireTimeSpan);
        Assert.Equal(2, schemes.Count());
    }

    /// <summary>
    /// D-M33 fixed: there is an <c>Action&lt;CookieAuthenticationOptions&gt;</c> overload, and it runs
    /// after the library's defaults so it can override any of them — which a host served over plain
    /// HTTP has to do for <c>SecurePolicy</c>.
    /// </summary>
    [Fact]
    public void AddMustChangePasswordUserIdCookie_ConfigureOverload_RunsAfterTheDefaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAuthentication().AddMustChangePasswordUserIdCookie(o =>
        {
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(2);
        });

        using var provider = services.BuildServiceProvider();
        var options = GetOptions(provider, Scheme);

        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
        Assert.Equal(TimeSpan.FromMinutes(2), options.ExpireTimeSpan);

        // Untouched defaults survive.
        Assert.Equal(Scheme, options.Cookie.Name);
        Assert.Equal(SameSiteMode.Strict, options.Cookie.SameSite);
    }

    [Fact]
    public void AddMustChangePasswordUserIdCookie_HasAParameterlessAndAConfigureOverload()
    {
        var overloads = typeof(AuthenticationBuilderExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(AuthenticationBuilderExtensions.AddMustChangePasswordUserIdCookie))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToArray();

        Assert.Equal(2, overloads.Length);
        Assert.Contains(overloads, p => p.Length == 1 && p[0] == typeof(AuthenticationBuilder));
        Assert.Contains(overloads, p => p.Length == 2 && p[1] == typeof(Action<CookieAuthenticationOptions>));
    }

    /// <summary>
    /// D-M40 fixed: the extension is idempotent. It used to end in <c>AddScheme</c> twice, which threw
    /// on a duplicate scheme name — and threw when the scheme provider was first resolved, nowhere near
    /// the call site.
    /// </summary>
    [Fact]
    public async Task AddMustChangePasswordUserIdCookie_CalledTwice_RegistersTheSchemeOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        var builder = services.AddAuthentication();
        builder.AddMustChangePasswordUserIdCookie();
        builder.AddMustChangePasswordUserIdCookie();

        using var provider = services.BuildServiceProvider();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var scheme = await schemeProvider.GetSchemeAsync(Scheme);
        Assert.NotNull(scheme);
        Assert.Single(await schemeProvider.GetAllSchemesAsync());
        Assert.Equal(TimeSpan.FromMinutes(5), GetOptions(provider, Scheme).ExpireTimeSpan);
    }

    /// <summary>
    /// A second call is not silently discarded either: its configuration is still applied, so an app
    /// can harden or relax a cookie a library already registered.
    /// </summary>
    [Fact]
    public async Task AddMustChangePasswordUserIdCookie_CalledTwice_AppliesTheSecondCallsConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        var builder = services.AddAuthentication();
        builder.AddMustChangePasswordUserIdCookie();
        builder.AddMustChangePasswordUserIdCookie(o => o.ExpireTimeSpan = TimeSpan.FromMinutes(1));

        using var provider = services.BuildServiceProvider();

        Assert.Single(await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync());
        Assert.Equal(TimeSpan.FromMinutes(1), GetOptions(provider, Scheme).ExpireTimeSpan);
    }

    [Fact]
    public void AddMustChangePasswordUserIdCookie_ThrowsOnNullArguments()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        var builder = services.AddAuthentication();

        Assert.Throws<ArgumentNullException>(() => ((AuthenticationBuilder)null!).AddMustChangePasswordUserIdCookie());
        Assert.Throws<ArgumentNullException>(() => builder.AddMustChangePasswordUserIdCookie(null!));
    }
}
