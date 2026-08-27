using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// Group nesting, root-group discovery, and the order the emitted mapping is written in.
/// </summary>
/// <remarks>
/// Grouping is the only place the generator builds a graph rather than a list, so it is the only
/// place that can be ambiguous (a group with two parents) or unbounded (a cycle). Both are reported
/// rather than resolved by guessing.
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

    /// <summary>
    /// Nested groups are mapped on their parent's group builder, in ordinal order of their fully
    /// qualified names.
    /// </summary>
    /// <remarks>
    /// The order is asserted exactly because it is emitted text: route order, the <c>grp</c>/<c>_f</c>
    /// numbering and the descriptor list all ride on it. It used to come from iterating a
    /// <c>HashSet&lt;string&gt;</c>, which enumerates identically within one process but not across
    /// processes — string hashing is randomised per process — so the generated file was not
    /// reproducible between builds.
    /// </remarks>
    [Fact]
    public void NestedGroups_AreMappedOnTheirParentGroupBuilder_InOrdinalOrder()
    {
        var generated = Generated("Fixtures", FixtureSources.Corpus);

        Assert.Contains("var grp0 = MapGroup<global::Fixtures.ApiGroup>(app);", generated);
        Assert.Contains("var grp1 = MapGroup<global::Fixtures.ProductsApi>(grp0);", generated);
        Assert.Contains("var grp2 = MapGroup<global::Fixtures.UsersApi>(grp0);", generated);

        Assert.True(
            generated.IndexOf("MapGroup<global::Fixtures.ProductsApi>", StringComparison.Ordinal)
                < generated.IndexOf("MapGroup<global::Fixtures.UsersApi>", StringComparison.Ordinal),
            "ProductsApi sorts before UsersApi, so it must be emitted first.");
    }

    /// <summary>
    /// A group nothing joins is not mapped, even though it is discovered.
    /// </summary>
    /// <remarks>
    /// Only the groups endpoints actually join — and their ancestors — are worth a <c>MapGroup</c>
    /// call. Mapping an unreferenced one would add an empty route group to every application that
    /// declared a group ahead of writing its endpoints.
    /// </remarks>
    [Fact]
    public void GroupWithNoEndpointsAnywhereBeneathIt_IsNotMapped()
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
    /// A group that has no endpoints of its own but has children that do still gets a
    /// <c>MapGroup</c> call, which is what makes the nesting work at all.
    /// </summary>
    [Fact]
    public void GroupWithoutOwnEndpoints_IsStillMapped()
    {
        var generated = Generated("Fixtures", FixtureSources.Corpus);

        Assert.Contains("MapGroup<global::Fixtures.ApiGroup>(app)", generated);
        Assert.DoesNotContain("Map<global::Fixtures.ApiGroup>", generated);
    }

    /// <summary>
    /// A group with two parents gets MPEP004, and neither it nor its endpoints are mapped.
    /// </summary>
    /// <remarks>
    /// It used to become a <i>root</i> group: <c>HasMultipleParents</c> removed it from the parent
    /// map, and "not in the parent map" was exactly the test for being a root. So instead of an error
    /// the consumer got working routes in the wrong place — <c>/users/…</c> where they asked for
    /// <c>/api/users/…</c>. A missing route is far easier to notice than a wrong one.
    /// <para>
    /// MPEP004 rather than MPEP003: the shapes and the consequences differ. MPEP003 is about an
    /// endpoint and its one route; this moves every endpoint beneath the group, and someone filtering
    /// diagnostics needs to be able to tell the two apart.
    /// </para>
    /// </remarks>
    [Fact]
    public void GroupWithTwoParents_ReportsMPEP004_AndIsNotMapped()
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

        var result = EndpointGeneratorHarness.Run("Fixtures", source);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MPEP004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("UsersApi", diagnostic.GetMessage());

        Assert.DoesNotContain("MapGroup<global::Fixtures.UsersApi>", generated);
        Assert.DoesNotContain("Map<global::Fixtures.ListUsers>", generated);
    }

    /// <summary>
    /// Two groups that name each other as parent get MPEP005, and nothing recurses.
    /// </summary>
    /// <remarks>
    /// This test was previously skipped, because <c>EmitGroupTree</c> had no visited set and a
    /// <c>StackOverflowException</c> cannot be caught: running it would have taken the whole test host
    /// down. Reading the producer suggested the recursion was in fact unreachable — every member of a
    /// cycle has a non-null parent, so none qualified as a root group and <c>rootGroups</c> came out
    /// empty. That reading is now confirmed, and it makes the real symptom quieter and worse than a
    /// crash: the cyclic groups and every endpoint in them were silently dropped.
    /// <para>
    /// Both halves are fixed. The cycle is detected and reported, so it is no longer silent; and
    /// <c>EmitGroupTree</c> carries a visited set anyway, so a future change that does reach it cannot
    /// take the compiler with it. That is what makes this test safe to run.
    /// </para>
    /// </remarks>
    [Fact]
    public void CyclicGroups_ReportMPEP005_AndAreNotMapped()
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

        var result = EndpointGeneratorHarness.Run("Fixtures", source);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Equal(2, result.Diagnostics.Length);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("MPEP005", diagnostic.Id));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.GetMessage().Contains("GroupA"));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.GetMessage().Contains("GroupB"));

        Assert.DoesNotContain("MapGroup<global::Fixtures.GroupA>", generated);
        Assert.DoesNotContain("MapGroup<global::Fixtures.GroupB>", generated);
        Assert.DoesNotContain("Map<global::Fixtures.InA>", generated);

        // The mapping method itself is still emitted, so the consumer's call site is not a second,
        // unrelated error on top of MPEP005.
        Assert.Contains("MapFixturesEndpoints(this", generated);
    }

    /// <summary>A group nested inside itself is a cycle of one.</summary>
    [Fact]
    public void SelfNestedGroup_ReportsMPEP005()
    {
        var source = $$"""
            {{Preamble}}

            public class Loop : IEndpointGroup, IMemberOf<Loop>
            {
                public static string Prefix => "/loop";
            }

            public class InLoop : IGetEndpoint, IMemberOf<Loop>
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        var result = EndpointGeneratorHarness.Run("Fixtures", source);

        Assert.Equal("MPEP005", Assert.Single(result.Diagnostics).Id);
    }

    /// <summary>
    /// Emission order is reproducible, and it is the sorted order rather than whatever the syntax
    /// provider happened to produce.
    /// </summary>
    /// <remarks>
    /// Comparing two runs in one process cannot catch the original defect on its own — a
    /// <c>HashSet&lt;string&gt;</c> enumerates identically for identical insertions within a process.
    /// So the order is also asserted against the ordinal sort, which is a property a hash set cannot
    /// satisfy by luck.
    /// </remarks>
    [Fact]
    public void EmissionOrder_IsTheOrdinalSortedOrder_AndStableAcrossRuns()
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

        var generated = Generated("Fixtures", source);

        Assert.Equal(Generated("Fixtures", source), generated);

        // Roots ordinally: RootOne, RootThree, RootTwo. Each carries its one child.
        Assert.Contains("var grp0 = MapGroup<global::Fixtures.RootOne>(app);", generated);
        Assert.Contains("var grp1 = MapGroup<global::Fixtures.ChildOne>(grp0);", generated);
        Assert.Contains("var grp2 = MapGroup<global::Fixtures.RootThree>(app);", generated);
        Assert.Contains("var grp3 = MapGroup<global::Fixtures.ChildThree>(grp2);", generated);
        Assert.Contains("var grp4 = MapGroup<global::Fixtures.RootTwo>(app);", generated);
        Assert.Contains("var grp5 = MapGroup<global::Fixtures.ChildTwo>(grp4);", generated);
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
    /// A grouped endpoint whose <c>Path</c> is <c>"/"</c> composes to a route with a trailing slash —
    /// and that is correct, not a defect.
    /// </summary>
    /// <remarks>
    /// The composed pattern really is <c>/api/users/</c>, but ASP.NET Core routing treats the
    /// trailing empty segment as equivalent, so <c>GET /api/users</c> matches too — measured against
    /// a live server in <c>TestAppEndToEndTests</c>. This test exists to stop someone "normalising"
    /// the pattern on the strength of reading it; trimming the slash here would change the pattern
    /// every route assertion in this suite names, for no behavioural gain.
    /// </remarks>
    [Fact]
    public void GroupedEndpointWithRootPath_ComposesToATrailingSlashRoute()
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
}
