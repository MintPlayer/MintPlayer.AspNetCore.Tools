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
/// place that can be ambiguous (a group with two parents) or unbounded (a cycle). Neither is
/// resolved by guessing: the ambiguous shape is now the compiler's CS0579, because
/// <c>[MemberOf&lt;T&gt;]</c> is <c>AllowMultiple = false</c>, and the cycle — which C# cannot
/// forbid — is still the generator's MPEP005.
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

            [MemberOf<ReferencedRoot>]
            public class ListItems : IGetEndpoint
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
    /// A group declaring two parents is <c>CS0579</c>, on one declaration or split across partials.
    /// </summary>
    /// <remarks>
    /// Under <c>IMemberOf&lt;T&gt;</c> this shape was the generator's to catch, and it got it wrong
    /// once: the group became a <i>root</i> group, because <c>HasMultipleParents</c> removed it from
    /// the parent map and "not in the parent map" was exactly the test for being a root. The consumer
    /// got working routes in the wrong place — <c>/users/…</c> where they asked for
    /// <c>/api/users/…</c>. MPEP004 fixed that; <c>[MemberOf&lt;T&gt;]</c> being
    /// <c>AllowMultiple = false</c> retired it, because the compiler now refuses the shape outright.
    /// <para>
    /// Asserted for a group rather than only for an endpoint because the consequence is larger —
    /// every endpoint beneath the group would move — and because the partial case is the one a
    /// generator reading a single declaration would get wrong. Neither variant may produce a
    /// generator diagnostic: MPEP004 is retired, and its id is not reused.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupWithTwoParents_IsCS0579(bool splitAcrossPartials)
    {
        var usersApi = splitAcrossPartials
            ? """
              [MemberOf<ApiGroup>]
              public partial class UsersApi : IEndpointGroup
              {
                  public static string Prefix => "/users";
              }

              [MemberOf<AdminGroup>]
              public partial class UsersApi
              {
              }
              """
            : """
              [MemberOf<ApiGroup>]
              [MemberOf<AdminGroup>]
              public class UsersApi : IEndpointGroup
              {
                  public static string Prefix => "/users";
              }
              """;

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

            {{usersApi}}

            [MemberOf<UsersApi>]
            public class ListUsers : IGetEndpoint
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        // No MPEP004. MPEP016 at Info on the parent the rejected second attribute named is expected:
        // the generator reads the first [MemberOf<T>], so that group really is unjoined, and the
        // compilation already fails on CS0579.
        Assert.All(
            EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics,
            diagnostic => Assert.Equal("MPEP016", diagnostic.Id));

        var errors = EndpointGeneratorHarness.RunAndCompile("Fixtures", source)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("CS0579", Assert.Single(errors).Id);
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

            [MemberOf<GroupB>]
            public class GroupA : IEndpointGroup
            {
                public static string Prefix => "/a";
            }

            [MemberOf<GroupA>]
            public class GroupB : IEndpointGroup
            {
                public static string Prefix => "/b";
            }

            [MemberOf<GroupA>]
            public class InA : IGetEndpoint
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

            [MemberOf<Loop>]
            public class Loop : IEndpointGroup
            {
                public static string Prefix => "/loop";
            }

            [MemberOf<Loop>]
            public class InLoop : IGetEndpoint
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

            [MemberOf<RootOne>]
            public class ChildOne : IEndpointGroup { public static string Prefix => "/child"; }
            [MemberOf<RootTwo>]
            public class ChildTwo : IEndpointGroup { public static string Prefix => "/child"; }
            [MemberOf<RootThree>]
            public class ChildThree : IEndpointGroup { public static string Prefix => "/child"; }

            [MemberOf<ChildOne>]
            public class InOne : IGetEndpoint
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }

            [MemberOf<ChildTwo>]
            public class InTwo : IGetEndpoint
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }

            [MemberOf<ChildThree>]
            public class InThree : IGetEndpoint
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
