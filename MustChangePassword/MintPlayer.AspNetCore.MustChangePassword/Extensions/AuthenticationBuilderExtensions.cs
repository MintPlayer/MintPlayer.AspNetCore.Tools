using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MintPlayer.AspNetCore.MustChangePassword.Constants;

namespace MintPlayer.AspNetCore.MustChangePassword.Extensions;

/// <summary>Registration extensions for the must-change-password cookie.</summary>
public static class AuthenticationBuilderExtensions
{
    /// <summary>Adds the change-password cookie used for requiring the user to change his password before sign-in.</summary>
    /// <remarks>
    /// The cookie carries only the user id, and is configured for the shortest safe life: five minutes
    /// on both the server ticket and the browser (<c>max-age</c>), <c>HttpOnly</c>,
    /// <see cref="SameSiteMode.Strict"/> and <see cref="CookieSecurePolicy.Always"/>. Use the
    /// <see cref="AddMustChangePasswordUserIdCookie(AuthenticationBuilder, Action{CookieAuthenticationOptions})"/>
    /// overload to relax any of that — a host served over plain HTTP has to.
    /// <para>
    /// Calling this more than once is safe: the scheme is registered once, and any later call only
    /// applies its extra configuration.
    /// </para>
    /// </remarks>
    public static AuthenticationBuilder AddMustChangePasswordUserIdCookie(this AuthenticationBuilder builder)
        => builder.AddMustChangePasswordUserIdCookie(static _ => { });

    /// <summary>
    /// Adds the change-password cookie and applies <paramref name="configureOptions"/> on top of the
    /// library's defaults.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configureOptions">Runs after the defaults, so it can override any of them.</param>
    public static AuthenticationBuilder AddMustChangePasswordUserIdCookie(this AuthenticationBuilder builder, Action<CookieAuthenticationOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        // AddCookie ends in AddScheme, which throws on a duplicate name — and does so when the scheme
        // provider is first resolved, nowhere near the call. Registering the scheme once and only
        // layering the extra configuration afterwards makes repeated calls a no-op instead.
        if (builder.Services.Any(static d => d.ServiceType == typeof(MustChangePasswordCookieMarker)))
        {
            builder.Services.Configure(MustChangePasswordConstants.MustChangePasswordScheme, configureOptions);
            return builder;
        }

        // An instance, not a type registration: nothing ever has to activate it, so it stays invisible
        // to ValidateOnBuild.
        builder.Services.AddSingleton(new MustChangePasswordCookieMarker());

        return builder.AddCookie(MustChangePasswordConstants.MustChangePasswordScheme, o =>
        {
            var lifetime = TimeSpan.FromMinutes(5);

            o.Cookie.Name = MustChangePasswordConstants.MustChangePasswordScheme;
            o.ExpireTimeSpan = lifetime;

            // ExpireTimeSpan bounds the server-side ticket only. Without MaxAge the browser is told
            // nothing and keeps the cookie for the whole session, long after the ticket is dead.
            o.Cookie.MaxAge = lifetime;
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;

            configureOptions(o);
        });
    }

    /// <summary>Presence marker making <c>AddMustChangePasswordUserIdCookie</c> idempotent.</summary>
    private sealed class MustChangePasswordCookieMarker
    {
    }
}
