using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// Which classes the generator picks up, which it skips, and what it does when a shape is not
/// supported.
/// </summary>
/// <remarks>
/// Discovery is a two-stage filter: a cheap syntactic predicate on the base list, then a semantic
/// check for <c>IEndpointBase</c>. Everything interesting happens in the gap between them — the
/// syntactic side matches on name prefixes, the semantic side on <c>AllInterfaces</c>, while the
/// details are read back from <c>Interfaces</c> (direct only) and from the single declaration that
/// happened to trigger the callback.
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

    [Fact]
    public void RawEndpoint_IsMapped_AndGetsNoGeneratedBaseClass()
    {
        var result = EndpointGeneratorHarness.Run("Fixtures", FixtureSources.RawGetEndpoint);

        var generated = Generated(result);

        Assert.Contains("Map<global::Fixtures.HealthCheck>(app, _f0);", generated);
        Assert.DoesNotContain("partial class HealthCheck", generated);
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

        Assert.Empty(result.GeneratedTrees);
    }

    /// <summary>
    /// Pins D-G1 (MPEP001 is never reported) and D-G2 (what the user gets instead).
    /// </summary>
    /// <remarks>
    /// A typed endpoint has to be <c>partial</c> so the generator can supply the base class holding
    /// <c>HandleAsync(TRequest, CancellationToken)</c>. When it is not, emission is silently skipped
    /// and the user's own <c>override</c> is left with nothing to override: a bare CS0115 on their
    /// own line, with no explanation and no mention of the missing <c>partial</c>. MPEP001 exists
    /// and says exactly that — it is simply never reported.
    /// </remarks>
    [Fact]
    public void NonPartialTypedEndpoint_ReportsNoDiagnostic_KnownBug()
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
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain("partial class CreateUser", Generated(result));

        // D-G2: the only thing the user ever sees.
        var errors = Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source));
        Assert.Contains(errors, error => error.Id == "CS0115");
    }

    /// <summary>
    /// Pins D-G1: MPEP002 is never reported for a typed endpoint that already has a base class.
    /// </summary>
    /// <remarks>
    /// The generator cannot inject a second base class, so it skips emission. That is the right
    /// call, but the user is told nothing — and unlike the non-partial case there is not even a
    /// compiler error to go on here, because the fixture's own base class happens to satisfy the
    /// override. Silence plus a missing route is the worst of the three.
    /// </remarks>
    [Fact]
    public void TypedEndpointWithExistingBaseClass_ReportsNoDiagnostic_KnownBug()
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

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain("partial class CreateUser", Generated(result));
        // The route itself is still emitted, so the shape "compiles and silently works" — which is
        // why nobody notices the missing base class until they rely on request binding.
        Assert.Contains("global::Fixtures.CreateUser", Generated(result));
    }

    /// <summary>
    /// Pins D-G1 (MPEP003 is never reported) and D-G4 (the endpoint is dropped entirely).
    /// </summary>
    /// <remarks>
    /// Two <c>IMemberOf&lt;T&gt;</c> means the generator cannot choose a prefix, so it filters the
    /// endpoint out of <c>valid</c> — which is also the list that drives the partial base class and
    /// the descriptor list. The endpoint therefore loses its route, its descriptor <i>and</i> its
    /// base class, and a typed one then fails to compile with an unexplained CS0115.
    /// </remarks>
    [Fact]
    public void EndpointInTwoGroups_IsDroppedEntirely_AndReportsNoDiagnostic_KnownBug()
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

            public partial class CreateUser : IPostEndpoint<UserRequest, UserResponse>, IMemberOf<ApiGroup>, IMemberOf<AdminGroup>
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

        Assert.Empty(result.Diagnostics);
        // Not the route, not the descriptor, not the base class.
        Assert.DoesNotContain("CreateUser", generated);
        Assert.Contains("global::Fixtures.HealthCheck", generated);

        // What the user is left with instead of MPEP003.
        var errors = Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source));
        Assert.Contains(errors, error => error.Id == "CS0115");
    }

    /// <summary>
    /// Pins D-G10: interfaces inherited through a user base class are invisible to the generator.
    /// </summary>
    /// <remarks>
    /// The semantic gate uses <c>AllInterfaces</c>, so the class is discovered; the details are then
    /// read from <c>Interfaces</c>, which is direct-only and does not contain
    /// <c>IPostEndpoint&lt;,&gt;</c> at all. The endpoint therefore degrades to Raw/Custom: it keeps
    /// its route (verb and path resolve through the base class at runtime) but silently loses its
    /// <c>Produces</c> metadata — the one thing the typed-with-response level exists for.
    /// </remarks>
    [Fact]
    public void EndpointInheritingItsInterfacesViaABaseClass_LosesItsProducesMetadata_KnownBug()
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

            public class CreateUser : CreateUserBase, IMemberOf<ApiGroup>
            {
                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var generated = Generated(EndpointGeneratorHarness.Run("Fixtures", source));

        Assert.Contains("Map<global::Fixtures.CreateUser>", generated);
        Assert.DoesNotContain("Produces<global::Fixtures.CreateUser", generated);
    }

    /// <summary>
    /// Pins D-G9: a partial class split across files yields one <see cref="EndpointInfo"/> per
    /// declaration, and <c>GroupBy(fqn).First()</c> keeps whichever the syntax provider produced
    /// first — so the outcome depends on the order the compiler happens to hand over the files.
    /// </summary>
    /// <remarks>
    /// Both declarations describe the same symbol, so the interface-derived facts agree; what
    /// differs is read from the declaration itself, here <c>HasExistingBaseClass</c>. One order sees
    /// the user's base class and skips emission (correct); the other does not and injects a second
    /// base class, which is a CS0263 in the consumer's own partial. The assertion is on the
    /// order-dependence rather than on either outcome, because either could be the one that shows
    /// up.
    /// </remarks>
    [Fact]
    public void PartialSplitAcrossFiles_ProducesOrderDependentOutput_KnownBug()
    {
        const string withUserBaseClass = """
            using MintPlayer.AspNetCore.Endpoints;

            namespace Fixtures;

            public partial class CreateUser : MyOwnBase, IMemberOf<ApiGroup>
            {
            }
            """;

        var withEndpointInterface = $$"""
            {{Preamble}}

            public class ApiGroup : IEndpointGroup
            {
                public static string Prefix => "/api";
            }

            // A base class of the consumer's own, which is what makes the two declarations of
            // CreateUser disagree about HasExistingBaseClass.
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

        var baseClassFirst = Generated(EndpointGeneratorHarness.Run("Fixtures", withUserBaseClass, withEndpointInterface));
        var interfaceFirst = Generated(EndpointGeneratorHarness.Run("Fixtures", withEndpointInterface, withUserBaseClass));

        Assert.NotEqual(baseClassFirst, interfaceFirst);
    }

    /// <summary>
    /// Pins D-G11: with nothing to map, the producer returns before writing a single line, so no
    /// file is emitted — and the consumer's <c>app.MapFixturesEndpoints()</c> does not resolve.
    /// </summary>
    /// <remarks>
    /// An empty method would be strictly better: a project that has not written its first endpoint
    /// yet, or one whose endpoints all live in another assembly, currently cannot compile the call
    /// it was told to write.
    /// </remarks>
    [Fact]
    public void ZeroEndpoints_EmitNoFileAtAll_KnownBug()
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
        Assert.Empty(result.GeneratedTrees);

        var errors = Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source));
        Assert.Contains(errors, error => error.Id == "CS1061");
    }
}
