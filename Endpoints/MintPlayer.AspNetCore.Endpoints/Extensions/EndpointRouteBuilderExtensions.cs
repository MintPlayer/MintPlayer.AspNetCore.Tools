using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>Manual endpoint registration, for one-off routes without the source generator.</summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a single endpoint class, inside its declared route group chain. For automatic discovery
    /// of every endpoint in an assembly, use the source-generated
    /// <c>Map{AssemblyName}Endpoints()</c> method instead.
    /// </summary>
    /// <remarks>
    /// An endpoint that declares <c>IMemberOf&lt;TGroup&gt;</c> is mapped under that group's prefix,
    /// exactly as the generated mapping would map it — so an application that mixes generated and
    /// manual registration gets the same route either way. Each call creates its own
    /// <c>RouteGroupBuilder</c> chain, so the group's <c>Configure</c> hook runs once per call.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The endpoint or one of its groups declares more than one <c>IMemberOf&lt;TGroup&gt;</c>, or
    /// the group nesting is cyclic. Neither has a single resolvable prefix, and guessing one would
    /// silently register the endpoint at the wrong route.
    /// </exception>
    public static IEndpointRouteBuilder MapEndpoint<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.Interfaces)] TEndpoint>(
        this IEndpointRouteBuilder app)
        where TEndpoint : class, IEndpoint
    {
        var factory = ActivatorUtilities.CreateFactory<TEndpoint>(Type.EmptyTypes);

        var routes = app;
        foreach (var groupType in ResolveGroupChain(typeof(TEndpoint)))
            routes = MapGroupOf(routes, groupType);

        var builder = routes.MapMethods(
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
        builder.WithMetadata(EndpointAttributes.ForMetadata(typeof(TEndpoint)));

        // Call the optional Configure hook
        TEndpoint.Configure(builder);

        return app;
    }

    /// <summary>
    /// The group types <paramref name="type"/> sits in, outermost first.
    /// </summary>
    private static List<Type> ResolveGroupChain(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        var chain = new List<Type>();
        var visited = new HashSet<Type>();

        for (var current = type; ;)
        {
            var parent = ParentGroupOf(current);
            if (parent is null)
                break;

            if (!visited.Add(parent))
                throw new InvalidOperationException(
                    $"Endpoint group nesting for '{type.FullName}' is cyclic at '{parent.FullName}'.");

            chain.Add(parent);
            current = parent;
        }

        chain.Reverse();
        return chain;
    }

    private static Type? ParentGroupOf(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        var memberships = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMemberOf<>))
            .Select(i => i.GetGenericArguments()[0])
            .Distinct()
            .ToArray();

        return memberships.Length switch
        {
            0 => null,
            1 => memberships[0],
            _ => throw new InvalidOperationException(
                $"'{type.FullName}' declares IMemberOf<> for {memberships.Length} groups " +
                $"({string.Join(", ", memberships.Select(g => g.Name))}); exactly one is allowed.")
        };
    }

    // Prefix and Configure are static abstract interface members, so they can only be reached
    // through a generic type parameter — reflection over the type's properties would miss an
    // explicit implementation.
    private static readonly MethodInfo mapGroupOf =
        typeof(EndpointRouteBuilderExtensions).GetMethod(nameof(MapGroupCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static IEndpointRouteBuilder MapGroupOf(IEndpointRouteBuilder routes, Type groupType) =>
        (RouteGroupBuilder)mapGroupOf.MakeGenericMethod(groupType).Invoke(null, [routes])!;

    private static RouteGroupBuilder MapGroupCore<TGroup>(IEndpointRouteBuilder routes)
        where TGroup : IEndpointGroup
    {
        var group = routes.MapGroup(TGroup.Prefix);
        TGroup.Configure(group);
        return group;
    }
}
