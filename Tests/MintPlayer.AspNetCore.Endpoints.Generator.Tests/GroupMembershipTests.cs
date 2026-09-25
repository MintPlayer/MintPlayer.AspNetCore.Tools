using System.Collections.Immutable;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// Group membership by <c>[MemberOf&lt;TGroup&gt;]</c>: that the generator finds the attribute at
/// all, that it resolves the same group as <c>MapEndpoint&lt;T&gt;()</c> for every inheritance
/// shape, and that the retired diagnostics stay retired.
/// </summary>
/// <remarks>
/// Membership now has two independent implementations — a Roslyn base-chain walk in the generator
/// and a <see cref="Type.BaseType"/> walk in the runtime — and an application may use both, mapping
/// some endpoints through the generated method and others by hand. If the two ever disagree, one
/// class is served at two different routes depending on how it was registered, with no error from
/// either side. The parity matrix below is the only place both implementations run against the
/// <i>same</i> types, which is why it compiles one fixture and maps it both ways.
/// </remarks>
public class GroupMembershipTests
{
    private const string Preamble = """
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;
        """;

    /// <summary>
    /// One endpoint per inheritance shape, each with a distinct path so its route identifies it.
    /// </summary>
    /// <remarks>
    /// Membership sits on plain abstract bases rather than on bases implementing
    /// <c>IGetEndpoint</c>: a class implementing the interface must supply its static <c>Path</c>
    /// itself, and each endpoint here needs its own.
    /// </remarks>
    private static readonly string ParitySource = $$"""
        {{Preamble}}

        public class ApiGroup : IEndpointGroup
        {
            public static string Prefix => "/api";
        }

        public class AdminGroup : IEndpointGroup
        {
            public static string Prefix => "/admin";
        }

        [MemberOf<ApiGroup>]
        public abstract class InApi;

        public abstract class InApiIndirectly : InApi;

        public class NoAttribute : IGetEndpoint
        {
            public static string Path => "/none";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }

        [MemberOf<ApiGroup>]
        public class OnSelf : IGetEndpoint
        {
            public static string Path => "/self";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }

        public class OnBaseOnly : InApi, IGetEndpoint
        {
            public static string Path => "/base";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }

        [MemberOf<AdminGroup>]
        public class OnSelfAndBase : InApi, IGetEndpoint
        {
            public static string Path => "/both";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }

        public class TwoLevelsUp : InApiIndirectly, IGetEndpoint
        {
            public static string Path => "/two-up";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }
        """;

    private const string ParityAssemblyName = "Fixtures.MembershipParity";

    // Loaded once: two images under one simple name would leave two unrelated Type identities in
    // the process (see EndpointGeneratorHarness.RunAndLoad).
    private static readonly Lazy<Assembly> parityAssembly =
        new(() => EndpointGeneratorHarness.RunAndLoad(ParityAssemblyName, ParitySource));

    private static readonly MethodInfo mapEndpoint =
        typeof(EndpointRouteBuilderExtensions).GetMethod(nameof(EndpointRouteBuilderExtensions.MapEndpoint))!;

    private static bool IsMembership(object attribute)
        => attribute.GetType() is { IsGenericType: true } type
            && type.GetGenericTypeDefinition() == typeof(MemberOfAttribute<>);

    /// <summary>Maps one fixture type through the manual <c>MapEndpoint&lt;T&gt;()</c> path.</summary>
    private static RouteEndpoint MapManually(Type endpointType)
    {
        var app = WebApplication.CreateBuilder().Build();

        mapEndpoint.MakeGenericMethod(endpointType).Invoke(null, [app]);

        return Assert.Single(((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>());
    }

    /// <summary>
    /// The generated and the manual registration put each inheritance shape in the same group.
    /// </summary>
    /// <remarks>
    /// Each row asserts both paths against the expected route, not just against each other, so two
    /// implementations that are wrong the same way still fail.
    /// <para>
    /// <c>OnSelfAndBase</c> is the row that matters most. <c>MemberOfAttribute&lt;AdminGroup&gt;</c>
    /// and <c>MemberOfAttribute&lt;ApiGroup&gt;</c> are different closed types, so reflection's
    /// <c>inherit: true</c> does not hide the base's declaration and returns both — a runtime that
    /// read membership that way would throw on a single-match lookup, or pick by an undocumented
    /// enumeration order. The premise is asserted in the row itself so it cannot silently stop
    /// holding. The generator side has the mirror-image hazard: Roslyn never returns inherited
    /// attributes at all, so a generator that stopped walking the base chain fails
    /// <c>OnBaseOnly</c> and <c>TwoLevelsUp</c>.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("NoAttribute", "/none")]
    [InlineData("OnSelf", "/api/self")]
    [InlineData("OnBaseOnly", "/api/base")]
    [InlineData("OnSelfAndBase", "/admin/both")]
    [InlineData("TwoLevelsUp", "/api/two-up")]
    public void GeneratedAndManualRegistration_AgreeOnTheGroup(string typeName, string expectedRoute)
    {
        var endpointType = parityAssembly.Value.GetType($"Fixtures.{typeName}", throwOnError: true)!;
        var path = (string)endpointType.GetProperty("Path", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        var generated = Assert.Single(
            GeneratedEndpointHost.MapAndCollectRoutes(parityAssembly.Value, ParityAssemblyName),
            route => route.RoutePattern.RawText!.EndsWith(path, StringComparison.Ordinal));
        var manual = MapManually(endpointType);

        Assert.Equal(expectedRoute, generated.RoutePattern.RawText);
        Assert.Equal(expectedRoute, manual.RoutePattern.RawText);

        if (typeName == "OnSelfAndBase")
            Assert.Equal(2, endpointType.GetCustomAttributes(inherit: true).Count(IsMembership));
    }

    /// <summary>
    /// <c>[MemberOf&lt;T&gt;]</c> does not reach a mapped endpoint's metadata on either path —
    /// including for the endpoint that overrides its base's group.
    /// </summary>
    /// <remarks>
    /// Both paths transfer class-level attributes through <c>EndpointAttributes.ForMetadata</c>,
    /// which reads them with <c>inherit: true</c>. Before R1.7 excluded membership, every grouped
    /// endpoint carried a library-internal attribute where middleware and OpenAPI transformers
    /// enumerate metadata, and <c>OnSelfAndBase</c> carried two of them — one naming a group it is
    /// not in. Asserted on the mapped endpoints, not on the filter, because that is what a consumer
    /// sees.
    /// </remarks>
    [Fact]
    public void MembershipAttribute_IsNotEndpointMetadata_OnEitherPath()
    {
        var generatedRoutes = GeneratedEndpointHost.MapAndCollectRoutes(parityAssembly.Value, ParityAssemblyName);

        // Non-vacuous: all five endpoints were mapped, four of them inside a group.
        Assert.Equal(5, generatedRoutes.Count);
        foreach (var route in generatedRoutes)
            Assert.DoesNotContain(route.Metadata, IsMembership);

        foreach (var typeName in new[] { "OnSelf", "OnBaseOnly", "OnSelfAndBase", "TwoLevelsUp" })
        {
            var manual = MapManually(parityAssembly.Value.GetType($"Fixtures.{typeName}", throwOnError: true)!);
            Assert.DoesNotContain(manual.Metadata, IsMembership);
        }
    }

    /// <summary>
    /// The metadata name the generator matches is the real attribute's, and a fixture using the
    /// attribute produces grouped endpoints.
    /// </summary>
    /// <remarks>
    /// R1.3. The name must be arity-encoded — <c>MemberOfAttribute`1</c>. Both plausible wrong forms,
    /// <c>MemberOfAttribute</c> and <c>MemberOfAttribute&lt;TGroup&gt;</c>, match <i>nothing</i>,
    /// with no error and no warning, and the result is indistinguishable from a project that simply
    /// declares no groups: every endpoint maps at its bare path. Only a positive assertion catches
    /// that, so this one fails loudly if the corpus's seven <c>[MemberOf&lt;…&gt;]</c> endpoints
    /// come out ungrouped — read from the generator's own model, before any emission can mask it.
    /// </remarks>
    [Fact]
    public void MemberOfAttribute_IsMatched_AndAKnownGoodFixtureProducesGroupedEndpoints()
    {
        // Against the real type, so the constant cannot drift from what the compiler emits.
        Assert.Equal(GroupMembership.AttributeMetadataName, typeof(MemberOfAttribute<>).Name);

        var result = EndpointGeneratorHarness.CreateTrackingDriver()
            .RunGenerators(EndpointGeneratorHarness.CreateCompilation("Fixtures", [FixtureSources.Corpus]))
            .GetRunResult();

        ImmutableArray<T> Step<T>(string name) => [.. result.Results
            .SelectMany(generatorResult => generatorResult.TrackedSteps)
            .Where(step => step.Key == name)
            .SelectMany(step => step.Value)
            .SelectMany(step => step.Outputs)
            .SelectMany(output => (ImmutableArray<T>)output.Value)];

        var endpoints = Step<EndpointInfo>(TrackingNames.Endpoints);
        var groups = Step<GroupInfo>(TrackingNames.Groups);

        var inUsersApi = endpoints.Where(info => info.GroupTypeFqn == "global::Fixtures.UsersApi").ToArray();
        Assert.True(
            inUsersApi.Length > 0,
            "No endpoint in the corpus resolved to [MemberOf<UsersApi>]. The attribute's metadata name is " +
            "probably wrong: a mismatch matches nothing, silently, and every group disappears.");
        Assert.Equal(6, inUsersApi.Length);
        Assert.Single(endpoints, info => info.GroupTypeFqn == "global::Fixtures.ProductsApi");

        // Group nesting goes through the same lookup.
        Assert.Equal(
            "global::Fixtures.ApiGroup",
            Assert.Single(groups, group => group.FullyQualifiedName == "global::Fixtures.UsersApi").ParentGroupFqn);
    }

    /// <summary>
    /// Naming a type that is not a group is <c>CS0311</c>, in the consumer's own file.
    /// </summary>
    /// <remarks>
    /// The reason R1.1 chose <c>[MemberOf&lt;T&gt;]</c> over <c>[MemberOf(typeof(T))]</c>: the
    /// <c>where TGroup : IEndpointGroup</c> constraint makes the mistake a compile error at the
    /// declaration. With a <c>typeof</c> argument it would compile, and the generator would have to
    /// detect it — or, worse, emit a <c>MapGroup&lt;NotAGroup&gt;</c> whose error points into
    /// generated code. Pinned so the constraint cannot be dropped unnoticed.
    /// </remarks>
    [Fact]
    public void MemberOfANonGroup_IsCS0311()
    {
        var source = $$"""
            {{Preamble}}

            public class NotAGroup
            {
            }

            [MemberOf<NotAGroup>]
            public class ListItems : IGetEndpoint
            {
                public static string Path => "/items";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        var errors = EndpointGeneratorHarness.RunAndCompile("Fixtures", source)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        // Source0.cs is the implicit-usings file the harness prepends; the fixture is Source1.cs.
        Assert.Contains(errors, error => error.Id == "CS0311" && error.Location.SourceTree?.FilePath == "Source1.cs");
    }

    /// <summary>
    /// MPEP003 and MPEP004 are never reported, and no descriptor carries either id.
    /// </summary>
    /// <remarks>
    /// R1.6 retires both — the shapes they described are now <c>CS0579</c> — and their ids are
    /// deliberately not reused, because a shipped diagnostic id must never change meaning: a
    /// consumer's <c>.editorconfig</c> or <c>NoWarn</c> entry for MPEP003 would otherwise silently
    /// start suppressing something else. So this checks both halves: the exact shapes that used to
    /// trigger them, and the descriptor table itself, which is where a reuse would first appear.
    /// </remarks>
    [Fact]
    public void RetiredDiagnostics_MPEP003AndMPEP004_AreNeverReported()
    {
        string[] retired = ["MPEP003", "MPEP004"];

        string[] formerTriggers =
        [
            // MPEP003: an endpoint in two groups.
            $$"""
            {{Preamble}}
            public class A : IEndpointGroup { public static string Prefix => "/a"; }
            public class B : IEndpointGroup { public static string Prefix => "/b"; }

            [MemberOf<A>]
            [MemberOf<B>]
            public class InBoth : IGetEndpoint
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """,
            // MPEP004: a group with two parents, split across partial declarations.
            $$"""
            {{Preamble}}
            public class A : IEndpointGroup { public static string Prefix => "/a"; }
            public class B : IEndpointGroup { public static string Prefix => "/b"; }

            [MemberOf<A>]
            public partial class Child : IEndpointGroup { public static string Prefix => "/child"; }

            [MemberOf<B>]
            public partial class Child { }

            [MemberOf<Child>]
            public class InChild : IGetEndpoint
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """,
        ];

        foreach (var source in formerTriggers)
        {
            var diagnostics = EndpointGeneratorHarness.RunAndCompile("Fixtures", source);

            // Non-vacuous: the shape really is still an error, just not ours.
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CS0579");
            Assert.DoesNotContain(diagnostics, diagnostic => retired.Contains(diagnostic.Id));
        }

        var descriptorIds = typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => ((DiagnosticDescriptor)field.GetValue(null)!).Id)
            .ToArray();

        // Non-vacuous: the reflection does find the table.
        Assert.Contains("MPEP005", descriptorIds);
        Assert.DoesNotContain(descriptorIds, id => retired.Contains(id));
    }
}
