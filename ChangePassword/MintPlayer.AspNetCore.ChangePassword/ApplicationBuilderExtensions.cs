namespace MintPlayer.AspNetCore.ChangePassword;

public static class ApplicationBuilderExtensions
{
    /// <summary>Most webbrowsers/password managers provide a shortcut to point the user to the page where they can change the password for the specific website. This middleware lets you handle these requests.</summary>
    /// <param name="changePasswordUrl">Async method or Task which returns the url for your web application where users can change their password.</param>
    public static IEndpointRouteBuilder MapChangePassword(this IEndpointRouteBuilder endpointRouteBuilder, Func<Task<string>> changePasswordUrl)
    {
        endpointRouteBuilder.MapGet("/.well-known/change-password", async (context) =>
        {
            var url = await changePasswordUrl();
            context.Response.Redirect(url);
        });
        return endpointRouteBuilder;
    }

    /// <summary>Most webbrowsers/password managers provide a shortcut to point the user to the page where they can change the password for the specific website. This middleware lets you handle these requests.</summary>
    /// <param name="changePasswordUrl">Async method or Task which returns the url for your web application where users can change their password.</param>
    public static IEndpointRouteBuilder MapChangePassword(this IEndpointRouteBuilder endpointRouteBuilder, Func<string> changePasswordUrl)
    {
        endpointRouteBuilder.MapGet("/.well-known/change-password", (context) =>
        {
            var url = changePasswordUrl();
            context.Response.Redirect(url);
            return Task.CompletedTask;
        });
        return endpointRouteBuilder;
    }
}