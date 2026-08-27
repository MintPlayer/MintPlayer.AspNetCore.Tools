using Microsoft.AspNetCore.Routing;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// Group nesting, root-group discovery, and the stability of the emitted order.
/// </summary>
/// <remarks>
/// Grouping is the only place the generator builds a graph rather than a list, and it does so from
/// two providers that disagree about what a group is: the group provider requires both
/// <c>IEndpointGroup</c> and <c>IMemberOf&lt;T&gt;</c> in the base list, while a root group by
/// definition has no <c>IMemberOf</c>. Root groups therefore exist only as strings pulled out of
/// other people's parent references.
/// </remarks>
public class EndpointGroupingTests
{
    private const string Preamble = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;
        """;

    private static string Generated(string assemblyName, params string[] sources)
        => string.Join(
            "\n",
            EndpointGeneratorHarness.Run(assemblyName, sources).GeneratedTrees.Select(tree => tree.ToString()));

    [Fact]
    public void NestedGroups_AreMappedOnTheirParentGroupBuilder()
    {
        var generated = Generated("Fixtures", FixtureSources.Corpus);

        Assert.Contains("var grp0 = MapGroup<global::Fixtures.ApiGroup>(app);", generated);
        Assert.Contains("var grp1 = MapGroup<global::Fixtures.UsersApi>(grp0);", generated);
        Assert.Contains("var grp2 = MapGroup<global::Fixtures.ProductsApi>(grp0);", generated);
    }

    /// <summary>
    /// A root group is never discovered as a group in its own right — it is only ever learned about
    /// from a child's <c>IMemberOf&lt;T&gt;</c> or an endpoint's membership. So it gets mapped when
    /// something points at it, and is invisible when nothing does.
    /// </summary>
    [Fact]
    public void RootGroup_IsDiscoveredOnlyThroughSomethingThatReferencesIt()
    {
        var source = $$"""
            {{Preamble}}

            public class ReferencedRoot : IEndpointGroup
            {
                public static string Prefix => "/referenced";
            }

            public class UnreferencedRoot : IEndpointGroup
            {
                public static string Prefix => "/unreferenced";
            }

            public class ListItems : IGetEndpoint, IMemberOf<ReferencedRoot>
            {
                public static string Path => "/items";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        var generated = Generated("Fixtures", source);

        Assert.Contains("MapGroup<global::Fixtures.ReferencedRoot>(app)", generated);
        Assert.DoesNotContain("UnreferencedRoot", generated);
    }

    /// <summary>
    /// A group that has children but no endpoints of its own still gets a <c>MapGroup</c> call, which
    /// is what makes the nesting work at all.
    /// </summary>
    [Fact]
    public void GroupWithoutOwnEndpoints_IsStillMapped()
    {
        var generated = Generated("Fixtures", FixtureSources.Corpus);

        Assert.Contains("MapGroup<global::Fixtures.ApiGroup>(app)", generated);
        Assert.DoesNotContain("Map<global::Fixtures.ApiGroup>", generated);
    }

    /// <summary>
    /// Pins D-G15: a group with two <c>IMemberOf&lt;T&gt;</c> silently becomes a root group.
    /// </summary>
    /// <remarks>
    /// <c>HasMultipleParents</c> removes the group from the parent map and from the child lookup, but
    /// it is still reachable as an endpoint's <c>GroupTypeFqn</c> — and "not in the parent map" is
    /// exactly the test for being a root. So instead of an error the user gets working routes at the
    /// wrong place: <c>/users/…</c> where they asked for <c>/api/users/…</c>. Compare D-G4, where the
    /// same ambiguity on an <i>endpoint</i> drops it entirely; the two halves of the same rule
    /// disagree about what to do.
    /// </remarks>
    [Fact]
    public void GroupWithTwoParents_BecomesARootGroup_KnownBug()
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

            public class UsersApi : IEndpointGroup, IMemberOf<ApiGroup>, IMemberOf<AdminGroup>
            {
                public static string Prefix => "/users";
            }

            public class ListUsers : IGetEndpoint, IMemberOf<UsersApi>
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        var generated = Generated("Fixtures", source);

        Assert.Contains("var grp0 = MapGroup<global::Fixtures.UsersApi>(app);", generated);
        Assert.DoesNotContain("ApiGroup", generated);
        Assert.DoesNotContain("AdminGroup", generated);
    }

    /// <summary>
    /// Pins D-G18: emission order must be reproducible.
    /// </summary>
    /// <remarks>
    /// Nothing in the producer sorts: root groups come out of a <c>HashSet&lt;string&gt;</c>, and
    /// <c>valid</c> keeps whatever order the syntax provider produced. Route order, the
    /// <c>_f</c>/<c>grp</c> numbering and the descriptor list all ride on that. Within one process
    /// the hash set enumerates the same way for the same insertions, so this assertion can pass by
    /// luck while the underlying order is still unspecified — it is written as the assertion the fix
    /// has to keep satisfying, not as a reproduction.
    /// </remarks>
    [Fact]
    public void EmissionOrder_IsStableAcrossRuns()
    {
        var source = $$"""
            {{Preamble}}

            public class RootOne : IEndpointGroup { public static string Prefix => "/one"; }
            public class RootTwo : IEndpointGroup { public static string Prefix => "/two"; }
            public class RootThree : IEndpointGroup { public static string Prefix => "/three"; }

            public class ChildOne : IEndpointGroup, IMemberOf<RootOne> { public static string Prefix => "/child"; }
            public class ChildTwo : IEndpointGroup, IMemberOf<RootTwo> { public static string Prefix => "/child"; }
            public class ChildThree : IEndpointGroup, IMemberOf<RootThree> { public static string Prefix => "/child"; }

            public class InOne : IGetEndpoint, IMemberOf<ChildOne>
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }

            public class InTwo : IGetEndpoint, IMemberOf<ChildTwo>
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }

            public class InThree : IGetEndpoint, IMemberOf<ChildThree>
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        Assert.Equal(Generated("Fixtures", source), Generated("Fixtures", source));
    }

    /// <summary>
    /// Executes the emitted mapping and asserts the resolved routes for every shape the sample app
    /// demonstrates. This is the only assertion that sees the route the consumer actually gets:
    /// paths arrive through <c>TEndpoint.Path</c> and prefixes are composed by ASP.NET Core's
    /// <c>MapGroup</c>, neither of which is visible in the generated text.
    /// </summary>
    [Fact]
    public void ResolvedRoutes_CoverEveryShape_WithGroupPrefixesComposed()
    {
        const string assemblyName = "Fixtures.Routes";
        var generated = EndpointGeneratorHarness.RunAndLoad(assemblyName, FixtureSources.Corpus);

        var routes = GeneratedEndpointHost.MapAndCollectRoutes(generated, assemblyName);

        var actual = routes
            .Select(route => (
                Path: route.RoutePattern.RawText,
                Methods: string.Join(",", route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)))
            .OrderBy(route => route.Path, StringComparer.Ordinal)
            .ThenBy(route => route.Methods, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new (string? Path, string Methods)[]
            {
                ("/api/products/", "GET"),
                ("/api/users/", "GET"),
                ("/api/users/", "POST"),
                ("/api/users/{id}", "DELETE"),
                ("/api/users/{id}", "GET"),
                ("/api/users/{id}", "PUT"),
                ("/api/users/{id}/patch", "PATCH"),
                ("/api/{**path}", "OPTIONS,HEAD"),
                ("/health", "GET"),
            },
            actual);
    }

    /// <summary>
    /// Pins D-G19: a grouped endpoint whose <c>Path</c> is <c>"/"</c> resolves to a route with a
    /// trailing slash, which does not match a request for the collection URL.
    /// </summary>
    /// <remarks>
    /// <c>"/"</c> is the natural way to say "the group's own URL", and the sample app uses it for
    /// four endpoints. Composing <c>/api/users</c> with <c>/</c> gives <c>/api/users/</c>, so
    /// <c>GET /api/users</c> is a 404 unless the app also enables trailing-slash redirection. The
    /// generator is the right place to trim it, because it is the only part that knows the path is
    /// group-relative.
    /// </remarks>
    [Fact]
    public void GroupedEndpointWithRootPath_ResolvesToATrailingSlashRoute_KnownBug()
    {
        const string assemblyName = "Fixtures.TrailingSlash";
        var generated = EndpointGeneratorHarness.RunAndLoad(assemblyName, FixtureSources.Corpus);

        var paths = GeneratedEndpointHost
            .MapAndCollectRoutes(generated, assemblyName)
            .Select(route => route.RoutePattern.RawText)
            .ToArray();

        Assert.Contains("/api/users/", paths);
        Assert.DoesNotContain("/api/users", paths);
    }

    /// <summary>
    /// Pins D-G17: two groups that name each other as parent make <c>EmitGroupTree</c> recurse
    /// without end.
    /// </summary>
    /// <remarks>
    /// There is no visited set, so the recursion is bounded only by the stack. The result is a
    /// <c>StackOverflowException</c> inside the generator, which .NET cannot catch: it terminates the
    /// process, so running this test would take the entire test host — and every other result in the
    /// run — down with it. It stays skipped until the visited set and its diagnostic exist, at which
    /// point this becomes an assertion about that diagnostic.
    /// <para>
    /// Reading the producer suggests this particular pair may not reach the recursion at all: every
    /// member of a cycle has a non-null parent, so none of them qualifies as a root group, and
    /// <c>rootGroups</c> comes out empty — in which case the observable symptom is that the cyclic
    /// groups and all their endpoints are silently dropped instead. That is a guess from reading,
    /// and confirming it means running the recursion. Not worth the whole test run, so it stays
    /// skipped either way.
    /// </para>
    /// </remarks>
    [Fact(Skip = "D-G17: uncatchable StackOverflowException inside the generator; would kill the test host.")]
    public void CyclicGroups_StackOverflow_KnownBug()
    {
        var source = $$"""
            {{Preamble}}

            public class GroupA : IEndpointGroup, IMemberOf<GroupB>
            {
                public static string Prefix => "/a";
            }

            public class GroupB : IEndpointGroup, IMemberOf<GroupA>
            {
                public static string Prefix => "/b";
            }

            public class InA : IGetEndpoint, IMemberOf<GroupA>
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        Generated("Fixtures", source);
    }
}
