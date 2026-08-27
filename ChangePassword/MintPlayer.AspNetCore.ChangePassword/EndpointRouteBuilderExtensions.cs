namespace MintPlayer.AspNetCore.ChangePassword;

/// <summary>
/// Maps the well-known change-password endpoint onto an <see cref="IEndpointRouteBuilder"/>.
/// </summary>
/// <remarks>
/// Note this type shares its simple name with
/// <c>Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions</c>, which is an implicit global
/// using in Web SDK projects — so naming it explicitly from consumer code needs full
/// qualification. Calling the extension methods does not.
/// </remarks>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>The route name applied to the mapped endpoint.</summary>
    public const string EndpointName = "ChangePassword";

    private const string WellKnownPath = "/.well-known/change-password";

    /// <summary>
    /// Maps the <c>/.well-known/change-password</c> endpoint, which most webbrowsers and password
    /// managers use as a shortcut to the page where the user can change their password for this
    /// website. Requests are answered with a redirect to the url your factory returns.
    /// </summary>
    /// <param name="endpointRouteBuilder">The endpoint route builder to map the endpoint on.</param>
    /// <param name="changePasswordUrl">Async method which returns the url for your web application
    /// where users can change their password. Invoked once per request, so the url may vary by
    /// tenant, culture or configuration. It must return a non-empty url.</param>
    /// <returns>
    /// A builder for the mapped endpoint, so conventions can be attached to it. Applications with a
    /// global authorization fallback policy need <c>.AllowAnonymous()</c> here, which is not applied
    /// by default because <c>IAllowAnonymous</c> metadata cannot be undone by the caller afterwards
    /// — that would replace one un-exemptable policy with another.
    /// </returns>
    /// <remarks>
    /// A throw-only lambda (<c>() =&gt; throw …</c>) converts to both <see cref="Func{TResult}"/>
    /// shapes this method overloads on, so that one case has to be disambiguated with a cast.
    /// </remarks>
    public static IEndpointConventionBuilder MapChangePassword(this IEndpointRouteBuilder endpointRouteBuilder, Func<Task<string>> changePasswordUrl)
    {
        ArgumentNullException.ThrowIfNull(endpointRouteBuilder);
        ArgumentNullException.ThrowIfNull(changePasswordUrl);

        var builder = endpointRouteBuilder.MapGet(WellKnownPath, async (context) =>
        {
            var url = await changePasswordUrl();
            Redirect(context, url);
        });
        return builder.WithName(EndpointName);
    }

    /// <inheritdoc cref="MapChangePassword(IEndpointRouteBuilder, Func{Task{string}})"/>
    /// <param name="endpointRouteBuilder">The endpoint route builder to map the endpoint on.</param>
    /// <param name="changePasswordUrl">Method which returns the url for your web application where
    /// users can change their password. Invoked once per request, so the url may vary by tenant,
    /// culture or configuration. It must return a non-empty url.</param>
    public static IEndpointConventionBuilder MapChangePassword(this IEndpointRouteBuilder endpointRouteBuilder, Func<string> changePasswordUrl)
    {
        ArgumentNullException.ThrowIfNull(endpointRouteBuilder);
        ArgumentNullException.ThrowIfNull(changePasswordUrl);

        var builder = endpointRouteBuilder.MapGet(WellKnownPath, (context) =>
        {
            Redirect(context, changePasswordUrl());
            return Task.CompletedTask;
        });
        return builder.WithName(EndpointName);
    }

    /// <summary>
    /// Redirects to <paramref name="url"/>, refusing to produce a redirect no client can follow.
    /// </summary>
    /// <remarks>
    /// <c>Response.Redirect(null)</c> does not throw: it writes a 302 and simply omits the
    /// <c>Location</c> header, and an empty string writes the header empty. Both are syntactically
    /// valid responses that no browser or password manager can act on, produced with nothing
    /// recorded anywhere — so the factory's result is validated here instead.
    /// </remarks>
    private static void Redirect(HttpContext context, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                $"The change-password url factory passed to {nameof(MapChangePassword)} returned " +
                $"{(url is null ? "null" : "an empty url")}. Return the url of the page where users " +
                "can change their password.");
        }

        context.Response.Redirect(url);
    }
}
