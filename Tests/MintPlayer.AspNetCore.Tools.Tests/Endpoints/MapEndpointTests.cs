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

    [MemberOf<ApiGroup>]
    private sealed class UsersGroup : IEndpointGroup
    {
        public static string Prefix => "/users";
        public static void Configure(RouteGroupBuilder group) => group.WithTags("Users");
    }

    private sealed class AdminGroup : IEndpointGroup
    {
        public static string Prefix => "/admin";
    }

    [MemberOf<UsersGroup>]
    private sealed class ListUsersEndpoint : IGetEndpoint
    {
        public static string Path => "/";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    // Membership declared on plain abstract bases, so the endpoints below differ only in where the
    // attribute sits. The bases do not implement IGetEndpoint: a class implementing it must supply the
    // static Path itself, and each endpoint needs its own.
    [MemberOf<UsersGroup>]
    private abstract class InUsersGroup;

    private abstract class InUsersGroupIndirectly : InUsersGroup;

    private sealed class InheritsGroupEndpoint : InUsersGroup, IGetEndpoint
    {
        public static string Path => "/inherited";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private sealed class InheritsGroupTwoLevelsUpEndpoint : InUsersGroupIndirectly, IGetEndpoint
    {
        public static string Path => "/two-up";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    [MemberOf<AdminGroup>]
    private sealed class OverridesGroupEndpoint : InUsersGroup, IGetEndpoint
    {
        public static string Path => "/overridden";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    [MemberOf<CycleB>]
    private sealed class CycleA : IEndpointGroup
    {
        public static string Prefix => "/a";
    }

    [MemberOf<CycleA>]
    private sealed class CycleB : IEndpointGroup
    {
        public static string Prefix => "/b";
    }

    [MemberOf<CycleA>]
    private sealed class InCycleEndpoint : IGetEndpoint
    {
        public static string Path => "/";
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private static bool IsMembership(object attribute)
        => attribute.GetType() is { IsGenericType: true } type
            && type.GetGenericTypeDefinition() == typeof(MemberOfAttribute<>);

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
    /// membership of <c>UsersGroup</c> (then <c>IMemberOf&lt;UsersGroup&gt;</c>, now
    /// <c>[MemberOf&lt;UsersGroup&gt;]</c>) with <c>Path =&gt; "/"</c> landed at <c>/</c> instead of
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
    /// An endpoint in two groups can no longer reach <c>MapEndpoint</c> at all: the attribute is
    /// single-use, so the shape is <c>CS0579</c>.
    /// </summary>
    /// <remarks>
    /// There is no single prefix to resolve for such an endpoint, and picking one arbitrarily would
    /// register it at a route the author never asked for. Under <c>IMemberOf&lt;T&gt;</c> this method
    /// had to refuse it with an <see cref="InvalidOperationException"/> at registration time; now the
    /// compiler refuses it earlier, and that guard was removed. What keeps the removal safe is the
    /// attribute's usage, so that is what is pinned here — <c>AllowMultiple = true</c> would bring the
    /// ambiguous shape back with nothing left to catch it. (The compile error itself is asserted by
    /// the generator suite, which can compile a fixture that does not build.)
    /// <para>
    /// <c>Inherited = true</c> is pinned alongside it: it is what makes membership on a base class
    /// meaningful to anything reading the attribute through reflection.
    /// </para>
    /// </remarks>
    [Fact]
    public void MemberOfAttribute_IsSingleUse_SoAnEndpointInTwoGroupsCannotBeDeclared()
    {
        var usage = Assert.Single(
            typeof(MemberOfAttribute<>).GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
                .Cast<AttributeUsageAttribute>());

        Assert.False(usage.AllowMultiple);
        Assert.True(usage.Inherited);
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
    }

    /// <summary>
    /// Cyclic group nesting is refused, loudly, at registration time.
    /// </summary>
    /// <remarks>
    /// This is the one grouping mistake C# cannot forbid — <c>[MemberOf&lt;B&gt;] class A</c> and
    /// <c>[MemberOf&lt;A&gt;] class B</c> compile — so the runtime still has to catch it, just as the
    /// generator reports MPEP005. A cycle has no outermost group and so no prefix; without the visited
    /// set the chain walk would never terminate.
    /// </remarks>
    [Fact]
    public void MapEndpoint_CyclicGroupNesting_Throws()
    {
        var app = WebApplication.CreateBuilder([]).Build();

        var exception = Assert.Throws<InvalidOperationException>(() => app.MapEndpoint<InCycleEndpoint>());

        Assert.Contains("cyclic", exception.Message);
    }

    /// <summary>Membership declared on a base class applies to the derived endpoint.</summary>
    /// <remarks>
    /// <c>ISymbol.GetAttributes()</c> never returns inherited attributes, so the generator walks the
    /// base chain explicitly; this path walks <see cref="Type.BaseType"/>. Both must land here, or an
    /// application mixing generated and manual registration maps one class at two routes.
    /// </remarks>
    [Fact]
    public void MapEndpoint_MembershipOnABaseClass_Applies()
    {
        var endpoint = Assert.Single(Map<InheritsGroupEndpoint>());

        Assert.Equal("/api/users/inherited", endpoint.RoutePattern.RawText);
    }

    /// <summary>The walk does not stop at the direct base.</summary>
    [Fact]
    public void MapEndpoint_MembershipTwoLevelsUp_Applies()
    {
        var endpoint = Assert.Single(Map<InheritsGroupTwoLevelsUpEndpoint>());

        Assert.Equal("/api/users/two-up", endpoint.RoutePattern.RawText);
    }

    /// <summary>
    /// A derived endpoint's own <c>[MemberOf&lt;T&gt;]</c> overrides its base's: nearest wins.
    /// </summary>
    /// <remarks>
    /// This is the case <c>GetCustomAttributes(inherit: true)</c> gets wrong, and the first assertion
    /// proves the premise rather than assuming it: the runtime only hides an inherited
    /// <c>AllowMultiple = false</c> attribute when the derived type carries the <i>same</i> attribute
    /// type, and <c>MemberOfAttribute&lt;AdminGroup&gt;</c> and
    /// <c>MemberOfAttribute&lt;UsersGroup&gt;</c> are different closed types — so it returns both. Any
    /// reading of membership through <c>inherit: true</c> that expects one match
    /// (<c>SingleOrDefault</c>, <c>GetCustomAttribute</c>) throws here, and one that takes an
    /// arbitrary match depends on an enumeration order reflection does not document.
    /// </remarks>
    [Fact]
    public void MapEndpoint_MembershipOnSelfAndOnBase_TheDerivedDeclarationWins()
    {
        Assert.Equal(
            2,
            typeof(OverridesGroupEndpoint).GetCustomAttributes(inherit: true).Count(IsMembership));

        var endpoint = Assert.Single(Map<OverridesGroupEndpoint>());

        Assert.Equal("/admin/overridden", endpoint.RoutePattern.RawText);
    }

    /// <summary>
    /// <c>[MemberOf&lt;T&gt;]</c> is an instruction to the mapper, not route metadata, and does not
    /// reach <c>endpoint.Metadata</c> — including for an endpoint that overrides its base's group.
    /// </summary>
    /// <remarks>
    /// Class-level attributes are transferred with <c>inherit: true</c>, and before R1.7 nothing
    /// excluded membership, so every grouped endpoint carried a library-internal attribute where
    /// middleware and OpenAPI transformers enumerate metadata. The overriding endpoint is the sharp
    /// case: it would have carried <i>two</i> of them, naming two different groups, one of which it
    /// is not in.
    /// </remarks>
    [Fact]
    public void MapEndpoint_DoesNotTransferMembershipIntoEndpointMetadata()
    {
        foreach (var endpoint in Map<ListUsersEndpoint>().Concat(Map<InheritsGroupEndpoint>()).Concat(Map<OverridesGroupEndpoint>()))
            Assert.DoesNotContain(endpoint.Metadata, IsMembership);

        Assert.DoesNotContain(EndpointAttributes.ForMetadata(typeof(OverridesGroupEndpoint)), IsMembership);
        Assert.DoesNotContain(EndpointAttributes.ForMetadata(typeof(ListUsersEndpoint)), IsMembership);
    }

    // ---------- M5: request-side OpenAPI metadata on the manual path ----------

    public sealed record CreateThing(string Name);

    private sealed class CreateThingEndpoint : IEndpoint<CreateThing>
    {
        public static string Path => "/things";
        public static IEnumerable<string> Methods => ["POST"];
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        public Task<IResult> HandleAsync(CreateThing request, CancellationToken cancellationToken) => Task.FromResult(Results.Ok());
    }

    private sealed class ThingByIdEndpoint : IGetEndpoint
    {
        public static string Path => "/things/{id}";
        [RouteParam] public int Id { get; set; }
        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private static int[] ProducedStatuses(RouteEndpoint endpoint) =>
        [.. endpoint.Metadata.OfType<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>().Select(m => m.StatusCode).Order()];

    /// <summary>
    /// A typed endpoint mapped by hand declares the same request body, 400 and 415 the generated
    /// mapping declares — through the same <c>EndpointDocumentation</c> helper.
    /// </summary>
    /// <remarks>
    /// Catches the two registrations drifting apart, and catches the body declaration regressing to
    /// <c>.Accepts&lt;T&gt;("application/json")</c>: content types on the metadata switch on routing's
    /// <c>AcceptsMatcherPolicy</c>, which then answers XML or any other type with an empty 415 before
    /// the library's formatters or its own <c>problem+json</c> 415 get a say. The default 200 must also
    /// survive, since ApiExplorer stops assuming it once any response is declared.
    /// </remarks>
    [Fact]
    public void MapEndpoint_TypedEndpoint_DeclaresRequestBodyWithoutContentTypes_And400And415()
    {
        var endpoint = Assert.Single(Map<CreateThingEndpoint>());

        var accepts = Assert.Single(endpoint.Metadata.OfType<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>());
        Assert.Equal(typeof(CreateThing), accepts.RequestType);
        Assert.Empty(accepts.ContentTypes);
        Assert.False(accepts.IsOptional);

        Assert.Equal([200, 400, 415], ProducedStatuses(endpoint));
    }

    /// <summary>
    /// An endpoint with a <c>[RouteParam]</c> declares the 400 its conversion can produce, and no body.
    /// </summary>
    /// <remarks>
    /// The manual path cannot declare the path parameter itself — it has no compile-time shadow type
    /// to hand ApiExplorer; that divergence is documented on <c>MapEndpoint</c>. This pins what it can do.
    /// </remarks>
    [Fact]
    public void MapEndpoint_EndpointWithBoundProperty_Declares400_AndNoBody()
    {
        var endpoint = Assert.Single(Map<ThingByIdEndpoint>());

        Assert.Empty(endpoint.Metadata.OfType<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>());
        Assert.Equal([200, 400], ProducedStatuses(endpoint));
    }

    /// <summary>An endpoint that binds nothing declares nothing extra — not even the default 200.</summary>
    [Fact]
    public void MapEndpoint_EndpointWithNothingToBind_DeclaresNoResponses()
        => Assert.Empty(ProducedStatuses(Assert.Single(Map<HealthEndpoint>())));
}
