using System.Reflection;
using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// MPEP012: two mappable endpoints with the same effective name.
/// </summary>
/// <remarks>
/// <para>
/// The failure this exists for does not happen at build time or at startup. ASP.NET Core checks
/// <c>WithName</c> uniqueness lazily, and a duplicate throws
/// <c>InvalidOperationException: Duplicate endpoint name</c> on the <b>first request</b> — measured,
/// and contrary to its documentation. Since the effective name is the class name, the ordinary
/// trigger is harmless-looking: two <c>GetUser</c> classes in two namespaces.
/// </para>
/// <para>
/// Asserted like the other diagnostics: it fires, on the right endpoint with the right message, and
/// the full compile has it as its <b>only</b> error (R3.4). Without the generator withholding the
/// second link, two <c>GetUser</c> methods in one class would add a <c>CS0111</c> in generated code.
/// </para>
/// </remarks>
public class DuplicateEndpointNameTests
{
    private const string Usings = """
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;
        """;

    /// <summary>Two <c>GetUser</c> endpoints, both ungrouped, in namespaces <c>A</c> and <c>B</c>.</summary>
    private static string TwoGetUsers(string attributeOnB = "") => Usings + $$"""


        namespace A
        {
            public class GetUser : IGetEndpoint
            {
                public static string Path => "/a/users/{id}";
                public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok());
            }
        }

        namespace B
        {
            {{attributeOnB}}
            public class GetUser : IGetEndpoint
            {
                public static string Path => "/b/users/{id}";
                public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok());
            }
        }
        """;

    private static string TextAt(Location location)
        => location.SourceTree!.GetText().ToString(location.SourceSpan);

    private static string Generated(string source, string file)
        => EndpointGeneratorHarness.Run("Fixtures", source).GeneratedTrees
            .Single(tree => tree.FilePath.EndsWith(file, StringComparison.Ordinal))
            .ToString();

    /// <summary>
    /// Same class name in two namespaces: MPEP012 on the later endpoint in ordinal order, naming
    /// both, and it is the only error the build has.
    /// </summary>
    [Fact]
    public void SameNameInTwoNamespaces_ReportsMPEP012_OnTheLaterEndpoint_AsTheOnlyError()
    {
        var source = TwoGetUsers();

        var diagnostic = Assert.Single(EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics);
        Assert.Equal("MPEP012", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // The later endpoint, B.GetUser — and the message tells the two apart by namespace.
        Assert.Equal("GetUser", TextAt(diagnostic.Location));
        var text = diagnostic.Location.SourceTree!.GetText().ToString();
        Assert.True(diagnostic.Location.SourceSpan.Start > text.IndexOf("namespace B", StringComparison.Ordinal), "MPEP012 must point at B.GetUser");
        Assert.Equal(
            "Endpoint 'B.GetUser' has the endpoint name 'GetUser', which 'A.GetUser' already has; endpoint names must be unique. Give one of them a distinct name with [EndpointDescriptorName(\"...\")].",
            diagnostic.GetMessage());

        var errors = EndpointGeneratorHarness.RunAndCompile("Fixtures", source)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            errors.Length == 1 && errors[0].Id == "MPEP012",
            $"expected MPEP012 as the only error, got: {string.Join(" | ", errors.Select(e => $"{e.Id}: {e.GetMessage()}"))}");
    }

    /// <summary>
    /// No cascade: the later endpoint is still mapped — only without a name — and the earlier one
    /// keeps both its name and its link.
    /// </summary>
    /// <remarks>
    /// Naming both would be the one way MPEP012 could become worse than useless: if the consumer
    /// demotes it, the app would compile, start, and throw on the first request. Withholding the
    /// second name means a demoted MPEP012 degrades to one unnamed endpoint.
    /// </remarks>
    [Fact]
    public void Duplicate_IsStillMapped_ButNotNamed_AndGetsNoLink()
    {
        var source = TwoGetUsers();

        var mapping = Generated(source, "EndpointMapping.g.cs");
        Assert.Contains("Map<global::A.GetUser", mapping);
        Assert.Contains("Map<global::B.GetUser", mapping);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(mapping, System.Text.RegularExpressions.Regex.Escape("WithName(b, \"GetUser\")")));

        var routes = Generated(source, "EndpointRoutes.g.cs");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(routes, " GetUser\\("));
        Assert.Contains("\"/a/users/{id}\"", routes);
        Assert.DoesNotContain("\"/b/users/{id}\"", routes);
    }

    /// <summary>
    /// Two same-named endpoints in the <b>same</b> group — where two link methods would land in one
    /// class — still produce MPEP012 alone, not a <c>CS0111</c> beside it.
    /// </summary>
    [Fact]
    public void SameNameInTheSameGroup_HasNoCompilerErrorBesideMPEP012()
    {
        var source = Usings + """


            namespace A
            {
                public class UsersApi : IEndpointGroup { public static string Prefix => "/users"; }

                [MemberOf<UsersApi>]
                public class Find : IGetEndpoint
                {
                    public static string Path => "/by-name";
                    public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok());
                }
            }

            namespace B
            {
                [MemberOf<A.UsersApi>]
                public class Find : IGetEndpoint
                {
                    public static string Path => "/by-email";
                    public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok());
                }
            }
            """;

        var errors = EndpointGeneratorHarness.RunAndCompile("Fixtures", source)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            errors.Length == 1 && errors[0].Id == "MPEP012",
            $"expected MPEP012 as the only error, got: {string.Join(" | ", errors.Select(e => $"{e.Id}: {e.GetMessage()}"))}");
    }

    /// <summary>
    /// <c>[EndpointDescriptorName]</c> on either endpoint is the fix the message names, and it
    /// silences MPEP012 completely: both endpoints are named, and both get a link.
    /// </summary>
    [Fact]
    public void DescriptorName_Silences_AndBothGetNamesAndLinks()
    {
        var source = TwoGetUsers("[EndpointDescriptorName(\"GetOtherUser\")]");

        Assert.Empty(EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics);

        var assembly = EndpointGeneratorHarness.RunAndLoad("Fixtures.Mpep012.Silenced", source);
        var routes = assembly.GetType("MintPlayer.AspNetCore.Endpoints.Generated.Routes")!;
        Assert.NotNull(routes.GetMethod("GetUser", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(routes.GetMethod("GetOtherUser", BindingFlags.Public | BindingFlags.Static));

        var mapping = Generated(source, "EndpointMapping.g.cs");
        Assert.Contains("WithName(b, \"GetUser\")", mapping);
        Assert.Contains("WithName(b, \"GetOtherUser\")", mapping);
    }

    /// <summary>
    /// A descriptor name that collides with another endpoint's <i>class</i> name is a duplicate too:
    /// the comparison is on the effective name, whatever produced it.
    /// </summary>
    [Fact]
    public void DescriptorNameCollidingWithAClassName_IsADuplicate()
    {
        var source = Usings + """


            namespace A
            {
                public class Health : IGetEndpoint
                {
                    public static string Path => "/health";
                    public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok());
                }

                [EndpointDescriptorName("Health")]
                public class Ping : IGetEndpoint
                {
                    public static string Path => "/ping";
                    public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok());
                }
            }
            """;

        var diagnostic = Assert.Single(EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics);
        Assert.Equal("MPEP012", diagnostic.Id);
        Assert.Equal("Ping", TextAt(diagnostic.Location));
    }
}
