using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// Which classes the generator picks up, which it skips, and what it tells the consumer when a shape
/// is not supported.
/// </summary>
/// <remarks>
/// Discovery is a two-stage filter: a cheap syntactic predicate on the base list, then a semantic
/// check for <c>IEndpointBase</c>. Everything interesting happens in the gap between them, and every
/// case where the semantic stage gives up has a diagnostic — MPEP001 and MPEP002 — because the
/// alternative the consumer used to get was a bare CS0115 or CS0263 in their own file. (MPEP003,
/// an endpoint in two groups, is retired: that shape is now the compiler's CS0579.)
/// <para>
/// The syntactic stage accepts any non-abstract class with a base list. It used to look for an
/// endpoint interface or <c>IMemberOf</c> by name, which silently missed an endpoint inheriting
/// its verb from a base class of its own — see the <c>InheritedVerb</c> tests below.
/// </para>
/// </remarks>
public class EndpointDiscoveryTests
{
    private const string Preamble = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;

        public record UserRequest(int Id);
        public record UserResponse(int Id, string Name);
        """;

    private static string Generated(GeneratorDriverRunResult result)
        => string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

    private static Diagnostic[] Errors(IEnumerable<Diagnostic> diagnostics)
        => [.. diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];

    /// <summary>The source text a diagnostic's location covers.</summary>
    private static string TextAt(Location location)
        => location.SourceTree!.GetText().ToString(location.SourceSpan);

    [Fact]
    public void RawEndpoint_IsMapped_AndGetsNoGeneratedBaseClass()
    {
        var result = EndpointGeneratorHarness.Run("Fixtures", FixtureSources.RawGetEndpoint);

        var generated = Generated(result);

        Assert.Contains("Map<global::Fixtures.HealthCheck>(app, _f0);", generated);
        Assert.DoesNotContain("partial class HealthCheck", generated);
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// An abstract endpoint is a base for other endpoints, not a route of its own. Nothing is
    /// emitted for it — not even the partial base class, which is why an abstract typed endpoint has
    /// to name its base class itself.
    /// </summary>
    [Fact]
    public void AbstractEndpoint_IsSkippedEntirely()
    {
        var source = $$"""
            {{Preamble}}

            public abstract partial class AbstractEndpoint : IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/abstract";
            }

            public class HealthCheck : IGetEndpoint
            {
                public static string Path => "/health";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        var generated = Generated(EndpointGeneratorHarness.Run("Fixtures", source));

        Assert.DoesNotContain("AbstractEndpoint", generated);
        Assert.Contains("global::Fixtures.HealthCheck", generated);
    }

    /// <summary>
    /// The syntactic predicate matches any base type whose name starts with <c>IEndpoint</c>, so an
    /// unrelated interface of the consumer's own gets through it. The semantic
    /// <c>AllInterfaces</c> check is what rejects it, and it must — otherwise the generator would
    /// emit a route for a class that has no <c>Path</c>.
    /// </summary>
    [Fact]
    public void ClassImplementingAForeignInterfaceNamedLikeAnEndpoint_IsNotDiscovered()
    {
        var source = """
            namespace Fixtures;

            public interface IEndpointOfMyOwn { }

            public class NotAnEndpoint : IEndpointOfMyOwn { }
            """;

        var result = EndpointGeneratorHarness.Run("Fixtures", source);

        Assert.DoesNotContain("NotAnEndpoint", Generated(result));
    }

    /// <summary>
    /// A typed endpoint that is not <c>partial</c> gets MPEP001.
    /// </summary>
    /// <remarks>
    /// The generator cannot supply the base class holding
    /// <c>HandleAsync(TRequest, CancellationToken)</c>, so the user's own <c>override</c> has nothing
    /// to override. Silently skipping emission left them a bare CS0115 on their own line with no
    /// mention of the missing <c>partial</c> — the diagnostic that says exactly that existed and was
    /// referenced from nowhere.
    /// </remarks>
    [Fact]
    public void NonPartialTypedEndpoint_ReportsMPEP001()
    {
        var source = $$"""
            {{Preamble}}

            public class CreateUser : IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/users";

                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var result = EndpointGeneratorHarness.Run("Fixtures", source);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MPEP001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("CreateUser", diagnostic.GetMessage());
        // Pointing at the class's own identifier, not at the file or at nothing.
        Assert.Equal("CreateUser", TextAt(diagnostic.Location));

        Assert.DoesNotContain("partial class CreateUser", Generated(result));
    }

    /// <summary>
    /// A typed endpoint whose own base class does not reach an endpoint base gets MPEP002.
    /// </summary>
    /// <remarks>
    /// The generator cannot inject a second base class, so it skips emission. Before, that was the
    /// end of it: no diagnostic, and — unlike the non-partial case — not even a compiler error to go
    /// on, because the fixture's base class happens to satisfy the override. Silence plus a route
    /// with no request binding was the worst of the three.
    /// </remarks>
    [Fact]
    public void TypedEndpointWithAForeignBaseClass_ReportsMPEP002()
    {
        var source = $$"""
            {{Preamble}}

            public abstract class MyOwnBase
            {
            }

            public partial class CreateUser : MyOwnBase, IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/users";

                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());

                public Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var result = EndpointGeneratorHarness.Run("Fixtures", source);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MPEP002", diagnostic.Id);
        Assert.Contains("CreateUser", diagnostic.GetMessage());
        Assert.Equal("CreateUser", TextAt(diagnostic.Location));
        Assert.DoesNotContain("partial class CreateUser", Generated(result));
    }

    /// <summary>
    /// A base class that already derives from one of the library's endpoint bases is the supported
    /// way to share endpoint behaviour, so it reports nothing.
    /// </summary>
    /// <remarks>
    /// The distinction matters in both directions: emission still has to be suppressed (a second
    /// base clause on the partial is a CS0263), but there is nothing missing to warn about. Nor may
    /// MPEP001 fire here — the class does not need to be <c>partial</c> when there is no base clause
    /// left to add.
    /// </remarks>
    [Fact]
    public void TypedEndpointWhoseBaseClassAlreadyDerivesFromAnEndpointBase_ReportsNothing()
    {
        var source = $$"""
            {{Preamble}}

            public abstract class CreateUserBase : PostEndpoint<UserRequest>
            {
                public static string Path => "/users";
            }

            public class CreateUser : CreateUserBase, IPostEndpoint<UserRequest, UserResponse>
            {
                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var result = EndpointGeneratorHarness.Run("Fixtures", source);

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain("partial class CreateUser", Generated(result));
        Assert.Contains("Map<global::Fixtures.CreateUser>", Generated(result));
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));
    }

    /// <summary>
    /// An endpoint declaring two groups is <c>CS0579</c> — and still gets its base class.
    /// </summary>
    /// <remarks>
    /// Two memberships means there is no single prefix, so the endpoint cannot be routed. Under
    /// <c>IMemberOf&lt;T&gt;</c> the generator had to detect it and report MPEP003; with
    /// <c>[MemberOf&lt;T&gt;]</c> being <c>AllowMultiple = false</c> the compiler refuses it, and
    /// this test pins that the refusal really comes from the compiler rather than from nowhere.
    /// <para>
    /// The second half is older and still load-bearing. The endpoint used to be filtered out of the
    /// one list that drives the partial base class as well, so a typed endpoint lost its base class
    /// and failed with an unexplained CS0115 on top of the real error. The base class has nothing to
    /// do with grouping, so it is emitted regardless and the one real problem is reported once.
    /// </para>
    /// </remarks>
    [Fact]
    public void EndpointInTwoGroups_IsCS0579_ButKeepsItsBaseClass()
    {
        var source = $$"""
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
            [MemberOf<AdminGroup>]
            public partial class CreateUser : IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/users";

                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }

            public class HealthCheck : IGetEndpoint
            {
                public static string Path => "/health";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        var result = EndpointGeneratorHarness.Run("Fixtures", source);
        var generated = Generated(result);

        // Nothing from the generator: the retired MPEP003 must not come back. The one exception is
        // MPEP016 at Info on the group the rejected second attribute named — the generator reads the
        // first [MemberOf<T>], so that group really is unjoined — which is invisible by default and
        // sits in a compilation CS0579 already fails.
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("MPEP016", diagnostic.Id));

        // The base class, so the user's override compiles and the only error is the one that explains
        // the actual mistake.
        Assert.Contains(
            "partial class CreateUser : global::MintPlayer.AspNetCore.Endpoints.PostEndpoint<global::Fixtures.UserRequest> { }",
            generated);
        Assert.Contains("global::Fixtures.HealthCheck", generated);

        var errors = Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source));
        var duplicate = Assert.Single(errors);
        Assert.Equal("CS0579", duplicate.Id);
        Assert.Contains("MemberOf", TextAt(duplicate.Location));
    }

    /// <summary>
    /// One <c>[MemberOf&lt;T&gt;]</c> on each of two partial declarations is also <c>CS0579</c>.
    /// </summary>
    /// <remarks>
    /// This is the shape that made MPEP003 hard to retire: each declaration looks innocent on its
    /// own, and a generator reading only the declaration that triggered it would see one group and
    /// map it. The compiler merges attributes across partial parts before applying
    /// <c>AllowMultiple</c>, which is what R1.6 relies on — pinned here so a change to the attribute's
    /// usage (or an accidental <c>AllowMultiple = true</c>) cannot quietly bring back an endpoint
    /// that is in two groups at once.
    /// </remarks>
    [Fact]
    public void EndpointInTwoGroupsAcrossPartialDeclarations_IsCS0579()
    {
        const string firstPart = """
            using MintPlayer.AspNetCore.Endpoints;

            namespace Fixtures;

            [MemberOf<AdminGroup>]
            public partial class CreateUser
            {
            }
            """;

        var secondPart = $$"""
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
            public partial class CreateUser : IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/users";

                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        // Only MPEP016 (Info) on the group the rejected attribute named; see EndpointInTwoGroups_IsCS0579_ButKeepsItsBaseClass.
        Assert.All(
            EndpointGeneratorHarness.Run("Fixtures", firstPart, secondPart).Diagnostics,
            diagnostic => Assert.Equal("MPEP016", diagnostic.Id));

        var errors = Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", firstPart, secondPart));
        Assert.Equal("CS0579", Assert.Single(errors).Id);
    }

    /// <summary>
    /// An endpoint whose verb comes from its own abstract base class, carrying its own
    /// <c>[MemberOf&lt;T&gt;]</c>, is discovered and mapped under the group's prefix.
    /// </summary>
    /// <remarks>
    /// The syntactic pre-filter used to match base-list names that began with an endpoint
    /// interface, plus <c>IMemberOf</c>. This class names neither — its base list is just
    /// <c>CreateUserBase</c> — and it only ever worked because an <c>IMemberOf&lt;T&gt;</c> sat in
    /// the same base list. Moving membership to an attribute removed that accident, so without the
    /// widened pre-filter this shape would have stopped being mapped, with no diagnostic.
    /// </remarks>
    [Fact]
    public void InheritedVerb_WithOwnMembership_IsDiscoveredAndMappedUnderTheGroup()
    {
        var source = $$"""
            {{Preamble}}

            public class ApiGroup : IEndpointGroup
            {
                public static string Prefix => "/api";
            }

            public abstract class CreateUserBase : PostEndpoint<UserRequest>, IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/users";
            }

            [MemberOf<ApiGroup>]
            public partial class CreateUser : CreateUserBase
            {
                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        Assert.Empty(EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics);
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));

        const string assemblyName = "Fixtures.InheritedVerbOwnMembership";
        var generated = EndpointGeneratorHarness.RunAndLoad(assemblyName, source);
        var route = Assert.Single(GeneratedEndpointHost.MapAndCollectRoutes(generated, assemblyName));

        Assert.Equal("/api/users", route.RoutePattern.RawText);
    }

    /// <summary>
    /// An endpoint that declares <i>neither</i> its verb nor its group — both come from its base —
    /// is discovered and mapped under the inherited group's prefix.
    /// </summary>
    /// <remarks>
    /// This one was silently never mapped before M3: <c>partial class GetUser : UsersEndpointBase</c>
    /// has nothing in its base list the old name-based pre-filter recognised, and with
    /// <c>IMemberOf&lt;T&gt;</c> on the base there was not even the accidental match the previous
    /// test used to rely on. It is the shape membership inheritance (R1.4) exists to support, so it
    /// is asserted end to end — discovered, compiled, and mapped at the composed route.
    /// </remarks>
    [Fact]
    public void InheritedVerbAndInheritedMembership_IsDiscoveredAndMappedUnderTheInheritedGroup()
    {
        var source = $$"""
            {{Preamble}}

            public class ApiGroup : IEndpointGroup
            {
                public static string Prefix => "/api";
            }

            [MemberOf<ApiGroup>]
            public class UsersApi : IEndpointGroup
            {
                public static string Prefix => "/users";
            }

            [MemberOf<UsersApi>]
            public abstract class UsersEndpointBase : ResponseEndpoint, IGetEndpoint<UserResponse>
            {
                public static string Path => "/{id}";

                [RouteParam] public int Id { get; set; }
            }

            public partial class GetUser : UsersEndpointBase
            {
                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
            }
            """;

        var result = EndpointGeneratorHarness.Run("Fixtures", source);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("Map<global::Fixtures.GetUser, GetUser_Parameters", Generated(result));
        // M4: the [RouteParam] is inherited, and the binder that assigns it is emitted into the
        // concrete endpoint — an abstract base never gets a partial of its own.
        Assert.Contains("protected override void BindParameters(", Generated(result));
        Assert.Contains("this.Id = global::MintPlayer.AspNetCore.Endpoints.ParameterBinding.Parsable<int>(context, global::MintPlayer.AspNetCore.Endpoints.ParameterSource.Route, \"Id\");", Generated(result));
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));

        const string assemblyName = "Fixtures.InheritedVerbAndMembership";
        var generated = EndpointGeneratorHarness.RunAndLoad(assemblyName, source);
        var route = Assert.Single(GeneratedEndpointHost.MapAndCollectRoutes(generated, assemblyName));

        Assert.Equal("/api/users/{id}", route.RoutePattern.RawText);
    }

    /// <summary>
    /// An endpoint that inherits its endpoint interfaces through a base class keeps its verb, its
    /// request type and its <c>Produces</c> metadata.
    /// </summary>
    /// <remarks>
    /// The semantic gate uses <c>AllInterfaces</c>, so such a class is discovered; the details used
    /// to be read back from <c>Interfaces</c>, which is direct-only and does not contain
    /// <c>IPostEndpoint&lt;,&gt;</c> at all. The endpoint silently degraded to Raw/Custom — it kept
    /// its route, because verb and path resolve through the base class at runtime, and lost the one
    /// thing the typed-with-response level exists for.
    /// </remarks>
    [Fact]
    public void EndpointInheritingItsInterfacesViaABaseClass_KeepsItsProducesMetadata()
    {
        var source = $$"""
            {{Preamble}}

            public class ApiGroup : IEndpointGroup
            {
                public static string Prefix => "/api";
            }

            public abstract class CreateUserBase : PostEndpoint<UserRequest>, IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/users";
            }

            [MemberOf<ApiGroup>]
            public class CreateUser : CreateUserBase
            {
                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var result = EndpointGeneratorHarness.Run("Fixtures", source);
        var generated = Generated(result);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("Map<global::Fixtures.CreateUser>", generated);
        Assert.Contains(
            "Produces<global::Fixtures.CreateUser, global::Fixtures.UserRequest, global::Fixtures.UserResponse>(b);",
            generated);
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));
    }

    /// <summary>
    /// A partial class split across files produces the same output whichever order the compiler hands
    /// the files over in.
    /// </summary>
    /// <remarks>
    /// Both declarations describe the same symbol, so the interface-derived facts agree; what
    /// differed was read from the single declaration that triggered the callback — <c>partial</c> and
    /// the base list. <c>GroupBy(fqn).First()</c> then kept whichever came first, so one order saw
    /// the user's base class and skipped emission while the other injected a second base class and
    /// produced a CS0263 in the consumer's own partial. Both are now read from the symbol, which has
    /// no order.
    /// </remarks>
    [Fact]
    public void PartialSplitAcrossFiles_ProducesOrderIndependentOutput()
    {
        const string withUserBaseClass = """
            using MintPlayer.AspNetCore.Endpoints;

            namespace Fixtures;

            [MemberOf<ApiGroup>]
            public partial class CreateUser : MyOwnBase
            {
            }
            """;

        var withEndpointInterface = $$"""
            {{Preamble}}

            public class ApiGroup : IEndpointGroup
            {
                public static string Prefix => "/api";
            }

            // A base class of the consumer's own that already reaches an endpoint base — the shape
            // the two declarations of CreateUser used to disagree about.
            public abstract class MyOwnBase : PostEndpoint<UserRequest>
            {
            }

            public partial class CreateUser : IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/users";

                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var baseClassFirst = EndpointGeneratorHarness.Run("Fixtures", withUserBaseClass, withEndpointInterface);
        var interfaceFirst = EndpointGeneratorHarness.Run("Fixtures", withEndpointInterface, withUserBaseClass);

        Assert.Equal(Generated(baseClassFirst), Generated(interfaceFirst));
        Assert.Empty(baseClassFirst.Diagnostics);
        Assert.Empty(interfaceFirst.Diagnostics);

        // Neither order injects a second base class, so neither produces a CS0263.
        Assert.DoesNotContain("partial class CreateUser :", Generated(baseClassFirst));
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", withUserBaseClass, withEndpointInterface)));
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", withEndpointInterface, withUserBaseClass)));
    }

    /// <summary>
    /// An assembly with no endpoints still gets its mapping method, as a no-op.
    /// </summary>
    /// <remarks>
    /// The producer used to return before writing a line, so no file was emitted and the
    /// <c>app.MapFixturesEndpoints()</c> the consumer was told to write did not resolve. A project
    /// that has not written its first endpoint yet, or whose endpoints all live in another assembly,
    /// could not compile the call.
    /// </remarks>
    [Fact]
    public void ZeroEndpoints_StillEmitsANoOpMappingMethod()
    {
        const string source = """
            using Microsoft.AspNetCore.Routing;
            using MintPlayer.AspNetCore.Endpoints;

            namespace Fixtures;

            public class ApiGroup : IEndpointGroup
            {
                public static string Prefix => "/api";
            }

            public static class Startup
            {
                public static void Configure(IEndpointRouteBuilder app) => app.MapFixturesEndpoints();
            }
            """;

        var result = EndpointGeneratorHarness.Run("Fixtures", source);
        var generated = Generated(result);

        Assert.Contains("MapFixturesEndpoints(this", generated);
        Assert.Contains("return app;", generated);
        // Nothing to map, so no group is mapped either.
        Assert.DoesNotContain("var grp0", generated);

        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));
    }
}
