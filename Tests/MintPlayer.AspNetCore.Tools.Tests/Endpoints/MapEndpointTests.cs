using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MintPlayer.AspNetCore.Endpoints;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// Stands in for a compiler-emitted attribute, by living in the namespace the filter recognises.
/// </summary>
/// <remarks>
/// Declared rather than borrowed. Which scope Roslyn stamps a real <c>NullableContextAttribute</c>
/// onto — module, type, or member — depends on where the nullable context is uniform, and in this
/// test assembly it lands on the module, so no fixture type here carries one and a test written
/// against one would pass vacuously. Naming the rule directly is the assertion that keeps working.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
[System.Runtime.CompilerServices.CompilerGenerated]
internal sealed class LooksCompilerEmittedAttribute : Attribute;

/// <summary>
/// Covers <c>MapEndpoint&lt;TEndpoint&gt;</c>'s registration-time behaviour.
/// </summary>
public class MapEndpointTests
{
    // The short name is usable: the library's attribute is EndpointDescriptorNameAttribute, not
    // EndpointNameAttribute — the latter would collide by simple name with
    // Microsoft.AspNetCore.Routing.EndpointNameAttribute, which a Web SDK project has in scope
    // through implicit usings, making `[EndpointName("x")]` a CS0104 in ordinary consumer code.
    [EndpointDescriptorName("health")]
    // A BCL attribute from a System.* namespace, kept for the attribute-transfer tests: the
    // filter excludes by namespace, so an attribute under System.* that is NOT compiler-emitted is
    // the case that has to survive. (ObsoleteAttribute used to play this part, at the cost of a
    // CS0618 on every use of the fixture.)
    [System.ComponentModel.Description("kept for the attribute-transfer test")]
    [System.Runtime.CompilerServices.InCompilerServicesNamespace]
    [LooksCompilerEmitted]
    private sealed class HealthEndpoint : IGetEndpoint
    {
        public static string Path => "/health";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok("healthy"));
    }

    private sealed class PreflightEndpoint : IEndpoint
    {
        public static string Path => "/api/{**rest}";
        public static IEnumerable<string> Methods => ["OPTIONS", "HEAD"];
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class ConfiguredEndpoint : IGetEndpoint
    {
        public static string Path => "/configured";
        public static void Configure(RouteHandlerBuilder builder) => builder.WithName("configured-by-hook");
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class CountingEndpoint : IGetEndpoint
    {
        public static int Constructions;
        public CountingEndpoint() => Interlocked.Increment(ref Constructions);
        public static string Path => "/counting";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class ApiGroup : IEndpointGroup
    {
        public static string Prefix => "/api";
    }

    private sealed class UsersGroup : IEndpointGroup, IMemberOf<ApiGroup>
    {
        public static string Prefix => "/users";
        public static void Configure(RouteGroupBuilder group) => group.WithTags("Users");
    }

    private sealed class AdminGroup : IEndpointGroup
    {
        public static string Prefix => "/admin";
    }

    private sealed class ListUsersEndpoint : IGetEndpoint, IMemberOf<UsersGroup>
    {
        public static string Path => "/";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class AmbiguousEndpoint : IGetEndpoint, IMemberOf<UsersGroup>, IMemberOf<AdminGroup>
    {
        public static string Path => "/";
        public static IEnumerable<string> Methods => ["GET"];
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private static IReadOnlyList<RouteEndpoint> Map<TEndpoint>() where TEndpoint : class, IEndpoint
    {
        var app = WebApplication.CreateBuilder([]).Build();
        app.MapEndpoint<TEndpoint>();

        return [.. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()];
    }

    [Fact]
    public void MapEndpoint_RegistersRouteAtTheEndpointsPath()
        => Assert.Equal("/health", Assert.Single(Map<HealthEndpoint>()).RoutePattern.RawText);

    [Fact]
    public void MapEndpoint_RegistersTheDeclaredHttpMethod()
    {
        var metadata = Assert.Single(Map<HealthEndpoint>()).Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.Equal(["GET"], metadata!.HttpMethods);
    }

    /// <summary>A multi-method endpoint produces one route carrying both verbs.</summary>
    [Fact]
    public void MapEndpoint_MultiMethodEndpoint_RegistersOneRouteWithAllVerbs()
    {
        var endpoint = Assert.Single(Map<PreflightEndpoint>());
        var metadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.Equal(["OPTIONS", "HEAD"], metadata!.HttpMethods);
    }

    /// <summary>
    /// Returns the route builder, not the <c>RouteHandlerBuilder</c>.
    /// </summary>
    /// <remarks>
    /// Chaining works, but the caller never gets a handle to the mapped route, so conventions can
    /// only be applied through the static <c>Configure</c> hook.
    /// </remarks>
    [Fact]
    public void MapEndpoint_ReturnsTheSameBuilderForChaining()
    {
        var app = WebApplication.CreateBuilder([]).Build();

        Assert.Same(app, app.MapEndpoint<HealthEndpoint>());
    }

    [Fact]
    public void MapEndpoint_TransfersClassLevelAttributesToEndpointMetadata()
    {
        var endpoint = Assert.Single(Map<HealthEndpoint>());

        Assert.Equal("health", endpoint.Metadata.GetMetadata<EndpointDescriptorNameAttribute>()?.Name);
        Assert.NotNull(endpoint.Metadata.GetMetadata<System.ComponentModel.DescriptionAttribute>());
    }

    /// <summary>
    /// Compiler-generated attributes of the endpoint class are kept out of what is transferred.
    /// </summary>
    /// <remarks>
    /// Transfer used to be a bare <c>GetCustomAttributes(true)</c>, so on a nullable-enabled class
    /// <c>NullableAttribute</c> and <c>NullableContextAttribute</c> became endpoint metadata. Harmless
    /// in itself, but it polluted <c>endpoint.Metadata</c> for any consumer that enumerates or filters
    /// it — and those are artifacts of how the class was compiled, never a routing convention.
    /// <para>
    /// Asserted on the selection, not on <c>endpoint.Metadata</c> — and that corrects the register.
    /// The compiler-generated attributes the original measurement found in <c>endpoint.Metadata</c>
    /// were never the transfer's: ASP.NET Core also contributes the handler delegate's own
    /// attributes, and that delegate is a compiler-generated lambda inside this library's extension
    /// method. They are still there after the fix, and correctly so. What the fix removes is the
    /// endpoint class's own, which is the only half this library controls.
    /// </para>
    /// </remarks>
    [Fact]
    public void ForMetadata_ExcludesCompilerEmittedAttributes()
    {
        var declared = typeof(HealthEndpoint).GetCustomAttributes(inherit: true);
        var transferred = EndpointAttributes.ForMetadata(typeof(HealthEndpoint));

        // Both rules, and both really are on the class — otherwise this passes vacuously.
        Assert.Contains(declared, attribute => attribute is LooksCompilerEmittedAttribute);
        Assert.Contains(declared, attribute => attribute.GetType().Namespace == "System.Runtime.CompilerServices");

        Assert.DoesNotContain(transferred, attribute => attribute is LooksCompilerEmittedAttribute);
        Assert.DoesNotContain(
            transferred,
            attribute => attribute.GetType().Namespace == "System.Runtime.CompilerServices");
    }

    /// <summary>Filtering must not take the consumer's own attributes with it.</summary>
    [Fact]
    public void ForMetadata_KeepsTheConsumersOwnAttributes()
    {
        var transferred = EndpointAttributes.ForMetadata(typeof(HealthEndpoint));

        Assert.Contains(transferred, attribute => attribute is EndpointDescriptorNameAttribute);
        Assert.Contains(transferred, attribute => attribute is System.ComponentModel.DescriptionAttribute);
    }

    [Fact]
    public void MapEndpoint_InvokesTheStaticConfigureHook()
    {
        var endpoint = Assert.Single(Map<ConfiguredEndpoint>());

        Assert.Equal("configured-by-hook", endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
    }

    /// <summary>
    /// The endpoint type is not instantiated until a request is dispatched.
    /// </summary>
    /// <remarks>
    /// Registration builds an <c>ObjectFactory</c> but must not construct anything: endpoints are
    /// resolved per request from the request scope, so constructing one at startup would capture
    /// the wrong scope and defeat the design.
    /// </remarks>
    [Fact]
    public void MapEndpoint_DoesNotConstructTheEndpointAtRegistrationTime()
    {
        CountingEndpoint.Constructions = 0;

        _ = Map<CountingEndpoint>();

        Assert.Equal(0, CountingEndpoint.Constructions);
    }

    /// <summary>
    /// Manual registration honours group membership, including nesting.
    /// </summary>
    /// <remarks>
    /// It used to map at <c>TEndpoint.Path</c> verbatim, so an endpoint declaring
    /// <c>IMemberOf&lt;UsersGroup&gt;</c> with <c>Path =&gt; "/"</c> landed at <c>/</c> instead of
    /// <c>/api/users/</c>. The README advertised manual registration with no such caveat, so an
    /// application mixing generated and manual registration got two different routes for the same
    /// endpoint class depending on how it was mapped — which is the kind of difference nobody looks
    /// for.
    /// </remarks>
    [Fact]
    public void MapEndpoint_MapsInsideTheDeclaredGroupChain()
    {
        var endpoint = Assert.Single(Map<ListUsersEndpoint>());

        Assert.Equal("/api/users/", endpoint.RoutePattern.RawText);
    }

    /// <summary>The group's own <c>Configure</c> hook runs, so its conventions apply too.</summary>
    [Fact]
    public void MapEndpoint_RunsTheGroupConfigureHook()
    {
        var endpoint = Assert.Single(Map<ListUsersEndpoint>());

        var tags = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.ITagsMetadata>();

        Assert.NotNull(tags);
        Assert.Contains("Users", tags!.Tags);
    }

    /// <summary>
    /// An endpoint in two groups is refused, loudly, at registration time.
    /// </summary>
    /// <remarks>
    /// There is no single prefix to resolve, and the generator reports MPEP003 for exactly this shape.
    /// Picking one arbitrarily would register the endpoint at a route the author never asked for, and
    /// silence is what made the whole class of grouping defects hard to find.
    /// </remarks>
    [Fact]
    public void MapEndpoint_EndpointInTwoGroups_Throws()
    {
        var app = WebApplication.CreateBuilder([]).Build();

        var exception = Assert.Throws<InvalidOperationException>(() => app.MapEndpoint<AmbiguousEndpoint>());

        Assert.Contains("IMemberOf", exception.Message);
        Assert.Contains(nameof(AmbiguousEndpoint), exception.Message);
    }
}
