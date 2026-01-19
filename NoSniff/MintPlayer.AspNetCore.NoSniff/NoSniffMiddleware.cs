using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.AspNetCore.NoSniff;

// You may need to install the Microsoft.AspNetCore.Http.Abstractions package into your project
public partial class NoSniffMiddleware
{
    [Inject] private readonly RequestDelegate next;

    public async Task Invoke(HttpContext httpContext)
    {
        httpContext.Response.OnStarting((state) =>
        {
            var context = (HttpContext)state;
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Task.CompletedTask;

        }, httpContext);

        await next(httpContext);
    }
}

// Extension method used to add the middleware to the HTTP request pipeline.
public static class NoSniffMiddlewareExtensions
{
    public static IApplicationBuilder UseNoSniff(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<NoSniffMiddleware>();
    }
}