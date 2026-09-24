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
    /// An endpoint in a group — through <c>[MemberOf&lt;TGroup&gt;]</c> on itself or on a base
    /// class — is mapped under that group's prefix, exactly as the generated mapping would map it,
    /// so an application that mixes generated and manual registration gets the same route either
    /// way. Each call creates its own <c>RouteGroupBuilder</c> chain, so the group's
    /// <c>Configure</c> hook runs once per call.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The group nesting is cyclic. A cycle has no outermost group and so no prefix, and guessing
    /// one would silently register the endpoint at the wrong route. (Two memberships on one type is
    /// no longer possible to express: it is <c>CS0579</c>.)
    /// </exception>
    public static IEndpointRouteBuilder MapEndpoint<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEndpoint>(
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
    private static List<Type> ResolveGroupChain(Type type)
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

    /// <summary>
    /// The group <paramref name="type"/> belongs to: the nearest <c>[MemberOf&lt;TGroup&gt;]</c>
    /// walking up the base chain, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// This must implement exactly the rule the source generator implements, or an application
    /// mixing generated and manual registration maps the same endpoint at two different routes.
    /// <para>
    /// <b>It deliberately does not use <c>GetCustomAttributes(inherit: true)</c>.</b> The runtime
    /// only hides an inherited <c>AllowMultiple = false</c> attribute when the derived type carries
    /// the <i>same attribute type</i> — and <c>MemberOfAttribute&lt;UsersApi&gt;</c> and
    /// <c>MemberOfAttribute&lt;OtherApi&gt;</c> are different closed types. So for an endpoint that
    /// overrides its base's group, <c>inherit: true</c> returns both, and picking either one is a
    /// guess. Walking <see cref="Type.BaseType"/> with <c>inherit: false</c> and taking the first
    /// hit is the generator's rule, stated the same way.
    /// </para>
    /// </remarks>
    private static Type? ParentGroupOf(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetCustomAttributes(inherit: false))
            {
                var attributeType = attribute.GetType();
                if (attributeType.IsGenericType &&
                    attributeType.GetGenericTypeDefinition() == typeof(MemberOfAttribute<>))
                    return attributeType.GetGenericArguments()[0];
            }
        }

        return null;
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
