using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MintPlayer.AspNetCore.Endpoints;

public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a single endpoint class. For automatic discovery, use the source-generated
    /// Map{AssemblyName}Endpoints() method instead.
    /// </summary>
    public static IEndpointRouteBuilder MapEndpoint<TEndpoint>(this IEndpointRouteBuilder app)
        where TEndpoint : class, IEndpoint
    {
        var factory = ActivatorUtilities.CreateFactory<TEndpoint>(Type.EmptyTypes);

        var builder = app.MapMethods(
            TEndpoint.Path,
            TEndpoint.Methods,
            async (HttpContext ctx) =>
            {
                var endpoint = factory(ctx.RequestServices, null);
                try
                {
                    return await endpoint.HandleAsync(ctx);
                }
                finally
                {
                    if (endpoint is IAsyncDisposable asyncDisposable)
                        await asyncDisposable.DisposeAsync();
                    else if (endpoint is IDisposable disposable)
                        disposable.Dispose();
                }
            });

        // Transfer class-level attributes to endpoint metadata
        builder.WithMetadata(typeof(TEndpoint).GetCustomAttributes(true));

        // Call the optional Configure hook
        TEndpoint.Configure(builder);

        return app;
    }
}
