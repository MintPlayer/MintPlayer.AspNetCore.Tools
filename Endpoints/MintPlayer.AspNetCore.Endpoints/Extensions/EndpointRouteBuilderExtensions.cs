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
    // Measured with IsAotCompatible (PRD R6.4): these two reflective steps are the whole trim/AOT
    // residue of this path, and both are inherent to it, so it says so rather than suppressing them.
    private const string ManualMappingIsReflective =
        "MapEndpoint<TEndpoint>() maps through the Delegate overload of MapMethods, which RequestDelegateFactory " +
        "binds by reflection, and reaches a [MemberOf<T>] group's static Prefix and Configure through " +
        "MethodInfo.MakeGenericMethod. Neither is trim- or AOT-safe.";

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
    /// <para>
    /// <b>OpenAPI: this path documents less than the generated one, deliberately.</b> It declares
    /// the same request body, 400 and 415 (through the same <see cref="EndpointDocumentation"/>
    /// helper), but it cannot declare route or query <i>parameters</i>. ApiExplorer only sees
    /// parameters of the request delegate, and the generator gets them there by passing a
    /// compile-time <c>string?</c> shadow type as an <c>[AsParameters]</c> argument; a
    /// reflection-based registration has no such type to pass, short of emitting one at run time.
    /// So an endpoint mapped here with a templated route is documented without its path
    /// parameters — which OpenAPI treats as an invalid document — and without the typed schemas
    /// the generated <c>EndpointOpenApi.g.cs</c> restores. Nor does it declare the
    /// <c>Produces&lt;TResponse&gt;</c> success response the generated mapping does. Use the
    /// generated <c>Map…Endpoints()</c> for anything that is documented.
    /// </para>
    /// <para>
    /// <b>The endpoint is named, and this path cannot prove the name is unique.</b> It applies
    /// <c>WithName</c> with the same name the generated mapping uses — the
    /// <see cref="EndpointDescriptorNameAttribute"/> value, else the class name — so the endpoint
    /// gets the same route name and OpenAPI <c>operationId</c> either way. But ASP.NET Core only
    /// checks endpoint names for uniqueness on the <i>first request</i>, where a duplicate throws
    /// <c>InvalidOperationException: Duplicate endpoint name</c>, and this method sees one endpoint
    /// at a time. The generator reports a duplicate at build time (MPEP012) because it sees every
    /// endpoint at once; here, two endpoints with the same class name in different namespaces — or
    /// one endpoint mapped both here and by the generated mapping — compile, start, and fail on the
    /// first request. Disambiguate with <see cref="EndpointDescriptorNameAttribute"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The group nesting is cyclic. A cycle has no outermost group and so no prefix, and guessing
    /// one would silently register the endpoint at the wrong route. (Two memberships on one type is
    /// no longer possible to express: it is <c>CS0579</c>.)
    /// </exception>
    [RequiresUnreferencedCode(ManualMappingIsReflective)]
    [RequiresDynamicCode(ManualMappingIsReflective)]
    public static IEndpointRouteBuilder MapEndpoint<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.Interfaces |
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.NonPublicProperties)] TEndpoint>(
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
                // Constructed inside the delegate, per request. Bound properties make an endpoint
                // stateful, so hoisting this out of the lambda would turn it into a process-wide
                // singleton leaking one request's route values into the next (PRD R2.12).
                var endpoint = factory(ctx.RequestServices, null);
                try
                {
                    // A raw endpoint has no base class to bind its [RouteParam]/[QueryParam]
                    // properties, so the mapper does it. Every other level binds inside its own
                    // HandleAsync(HttpContext). Must match the generated Map<TEndpoint> exactly.
                    if (endpoint is IParameterBinder binder && binder.BindParameters(ctx) is { } failed)
                        return failed;

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

        // The request-side metadata the generated mapping declares, through the same helper, after
        // Configure as there. See <remarks> for what this path cannot match.
        if (RequestTypeOf(typeof(TEndpoint)) is { } requestType)
            EndpointDocumentation.DeclareRequestBody(builder, requestType);
        else if (HasBoundProperties(typeof(TEndpoint)))
            EndpointDocumentation.DeclareBindingFailure(builder);

        // After Configure, as in the generated mapping: the name is the one typed links and the
        // OpenAPI operationId rely on, so a WithName inside Configure does not replace it.
        builder.WithName(EndpointNameOf(typeof(TEndpoint)));

        return app;
    }

    /// <summary>
    /// The endpoint's name: its <see cref="EndpointDescriptorNameAttribute"/>, else its class name —
    /// the rule the generator applies (<c>EndpointInfo.EffectiveDescriptorName</c>).
    /// </summary>
    /// <remarks>
    /// Read from the type itself only, not inherited: the attribute is <c>Inherited = false</c>, and
    /// the generator reads it from the endpoint's own symbol. An empty name is ignored there too.
    /// </remarks>
    internal static string EndpointNameOf(Type endpointType) =>
        endpointType.GetCustomAttribute<EndpointDescriptorNameAttribute>(inherit: false) is { Name.Length: > 0 } attribute
            ? attribute.Name
            : endpointType.Name;

    /// <summary>
    /// The request body type of a typed endpoint — the <c>TRequest</c> of the
    /// <c>IEndpoint&lt;TRequest&gt;</c> it implements — or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The same rule the generator applies: <c>IEndpoint&lt;TRequest, TResponse&gt;</c> derives from
    /// <c>IEndpoint&lt;TRequest&gt;</c>, and the response-only <c>IResponseEndpoint&lt;T&gt;</c> does
    /// not, so its type argument is never mistaken for a body.
    /// </remarks>
    private static Type? RequestTypeOf(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type endpointType)
    {
        foreach (var candidate in endpointType.GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEndpoint<>))
                return candidate.GetGenericArguments()[0];
        }

        return null;
    }

    /// <summary>
    /// True when the endpoint, or a base class of it, declares a <c>[RouteParam]</c> or
    /// <c>[QueryParam]</c> property — anything whose conversion can fail with a 400.
    /// </summary>
    private static bool HasBoundProperties(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)] Type endpointType)
    {
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        return endpointType.GetProperties(Declared).Any(property =>
            property.IsDefined(typeof(RouteParamAttribute), inherit: true) ||
            property.IsDefined(typeof(QueryParamAttribute), inherit: true));
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

    [RequiresUnreferencedCode(ManualMappingIsReflective)]
    [RequiresDynamicCode(ManualMappingIsReflective)]
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
