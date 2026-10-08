using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// MPEP036 (PRD R5.5, D12, AC13): a class is either a group or an endpoint, never both.
/// </summary>
/// <remarks>
/// With the endpoint-level <c>IsEnabled</c>, one implicit <c>public static bool IsEnabled</c> would
/// implement both interfaces' members, and one <c>Configure</c> name would cover two builders. Detection
/// is on every interface the class implements, so the shapes that reach both roles through a base class
/// or an intermediate interface count as well. The class is then treated as a group only.
/// </remarks>
public class GroupEndpointRoleDiagnosticTests
{
    private const string Preamble = """
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;
        """;

    private const string Handler = "public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());";

    private static string Fixture(string body) => Preamble + "\n\n" + body;

    private static Diagnostic[] Reported(string source)
        => [.. EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics.Where(d => d.Id == "MPEP036")];

    private static string Mapping(string source)
        => string.Join("\n", EndpointGeneratorHarness.Run("Fixtures", source).GeneratedTrees.Select(tree => tree.ToString()));

    private static string TextAt(Location location)
        => location.SourceTree!.GetText().ToString(location.SourceSpan);

    [Fact]
    public void ClassImplementingBothDirectly_ReportsMPEP036_OnItsIdentifier_AndIsAGroupOnly()
    {
        var source = Fixture($$"""
            public class Both : IEndpointGroup, IGetEndpoint
            {
                public static string Prefix => "/both";
                public static string Path => "/both";
                {{Handler}}
            }

            [MemberOf<Both>]
            public class Member : IGetEndpoint
            {
                public static string Path => "/member";
                {{Handler}}
            }
            """);

        var diagnostic = Assert.Single(Reported(source));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Both", TextAt(diagnostic.Location));
        Assert.Equal(
            "'Both' implements both IEndpointGroup and an endpoint interface. A class is either a group or an endpoint. Move the endpoint into its own class and join it with [MemberOf<Both>].",
            diagnostic.GetMessage());

        var mapping = Mapping(source);
        Assert.Contains("MapGroup<global::Fixtures.Both>(app)", mapping);
        Assert.DoesNotContain("Map<global::Fixtures.Both>", mapping);
        Assert.Contains("Map<global::Fixtures.Member>", mapping);
    }

    [Fact]
    public void BothRolesThroughABaseClass_ReportMPEP036()
    {
        var source = Fixture($$"""
            public abstract class GroupBase : IEndpointGroup
            {
                public static string Prefix => "/base";
            }

            public class Both : GroupBase, IGetEndpoint
            {
                public static string Path => "/both";
                {{Handler}}
            }
            """);

        Assert.Equal("Both", TextAt(Assert.Single(Reported(source)).Location));
    }

    [Fact]
    public void BothRolesThroughAnIntermediateInterface_ReportMPEP036()
    {
        var source = Fixture($$"""
            public interface IGroupedEndpoint : IEndpointGroup, IGetEndpoint { }

            public class Both : IGroupedEndpoint
            {
                public static string Prefix => "/both";
                public static string Path => "/both";
                {{Handler}}
            }
            """);

        Assert.Equal("Both", TextAt(Assert.Single(Reported(source)).Location));
    }

    [Fact]
    public void OrdinaryGroupAndEndpoint_AreSilent()
    {
        var source = Fixture($$"""
            public class Api : IEndpointGroup
            {
                public static string Prefix => "/api";
            }

            [MemberOf<Api>]
            public class Health : IGetEndpoint
            {
                public static string Path => "/health";
                {{Handler}}
            }
            """);

        Assert.Empty(Reported(source));
        Assert.Empty(Reported(FixtureSources.Corpus));
    }
}
