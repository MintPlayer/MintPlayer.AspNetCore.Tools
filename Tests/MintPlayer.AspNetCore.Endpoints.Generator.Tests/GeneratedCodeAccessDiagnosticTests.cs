using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// MPEP024: an endpoint or group the generated files cannot name is not mapped, linked or
/// contracted, and the consumer is told which declaration to change.
/// </summary>
/// <remarks>
/// Before MPEP024 a <c>private</c> nested endpoint was mapped anyway, and the generated
/// <c>EndpointMapping.g.cs</c>, <c>EndpointRoutes.g.cs</c> and <c>EndpointContracts.g.cs</c> failed
/// with CS0122 — errors in files the consumer cannot edit. So every case asserts, as R3.4 asks, that
/// the full compile has MPEP024 as its only error, and that nothing generated names the type.
/// </remarks>
public class GeneratedCodeAccessDiagnosticTests
{
    private const string Preamble = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;

        public record UserResponse(int Id, string Name);
        """;

    private static string Fixture(string body) => Preamble + "\n\n" + body;

    private static string TextAt(Location location)
        => location.SourceTree!.GetText().ToString(location.SourceSpan);

    private static string Generated(string source)
        => string.Join("\n", EndpointGeneratorHarness.Run("Fixtures", source).GeneratedTrees.Select(t => t.ToString()));

    private static Diagnostic[] Errors(IEnumerable<Diagnostic> diagnostics)
        => [.. diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];

    private static Diagnostic AssertSoleError(string source)
    {
        var diagnostic = Assert.Single(EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics);
        Assert.Equal("MPEP024", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        var errors = Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source));
        Assert.True(
            errors.Length == 1 && errors[0].Id == "MPEP024",
            $"expected MPEP024 as the only error, got: {string.Join(" | ", errors.Select(e => $"{e.Id}: {e.GetMessage()}"))}");

        return diagnostic;
    }

    /// <summary>
    /// The reported shape: a nested endpoint with no access modifier is private.
    /// </summary>
    [Fact]
    public void PrivateNestedRawEndpoint_IsNotMapped_AndReportsMPEP024_Only()
    {
        var source = Fixture("""
            public partial class Outer
            {
                class Health : IGetEndpoint
                {
                    public static string Path => "/health";

                    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
                }
            }
            """);

        var diagnostic = AssertSoleError(source);

        Assert.Equal("Health", TextAt(diagnostic.Location));
        Assert.Equal(
            "Endpoint class 'Health' cannot be mapped because it is declared 'private'; the generated mapping code must be able to name it, so it and every type it is nested in must be declared at least 'internal'",
            diagnostic.GetMessage());

        Assert.DoesNotContain("Outer.Health", Generated(source));
    }

    /// <summary>
    /// Every modifier that hides a nested type from the rest of the assembly, on a typed endpoint with
    /// a bound property: it keeps its generated base class and binder (a partial inside the partial
    /// container can reach it), so its <c>override</c> still resolves and MPEP024 stays the only error.
    /// </summary>
    [Theory]
    [InlineData("private", "private")]
    [InlineData("protected", "protected")]
    [InlineData("private protected", "private protected")]
    public void HiddenNestedTypedEndpoint_KeepsItsBaseClass_ButIsNotMappedLinkedOrContracted(string modifier, string reported)
    {
        var source = Fixture($$"""
            public partial class Outer
            {
                {{modifier}} partial class GetUser : IGetEndpoint<UserResponse>
                {
                    public static string Path => "/users/{id}";

                    [RouteParam] public int Id { get; set; }

                    public override Task<IResult> HandleAsync(CancellationToken ct)
                        => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
                }
            }
            """);

        var diagnostic = AssertSoleError(source);

        Assert.Contains($"because it is declared '{reported}'", diagnostic.GetMessage());

        var generated = Generated(source);
        Assert.Contains("partial class GetUser : global::MintPlayer.AspNetCore.Endpoints.ResponseEndpoint", generated);
        Assert.DoesNotContain("Map<global::Fixtures.Outer.GetUser", generated);
        Assert.DoesNotContain("typeof(global::Fixtures.Outer.GetUser)", generated);
        Assert.DoesNotContain("Describe<global::Fixtures.Outer.GetUser>", generated);
    }

    /// <summary>
    /// An accessible endpoint inside an inaccessible container is just as unreachable, and the message
    /// names the container to fix.
    /// </summary>
    [Fact]
    public void PublicEndpoint_NestedInsidePrivateType_ReportsTheContainer()
    {
        var source = Fixture("""
            public partial class Outer
            {
                private partial class Inner
                {
                    public class Health : IGetEndpoint
                    {
                        public static string Path => "/health";

                        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
                    }
                }
            }
            """);

        var diagnostic = AssertSoleError(source);

        Assert.Equal("Health", TextAt(diagnostic.Location));
        Assert.Contains("because it is nested inside 'Inner', which is declared 'private'", diagnostic.GetMessage());
    }

    /// <summary>
    /// <c>internal</c> and <c>protected internal</c> nested types are visible to the whole assembly,
    /// which is all the generated files need: mapped, linked and contracted, with no diagnostic.
    /// </summary>
    [Theory]
    [InlineData("internal")]
    [InlineData("protected internal")]
    [InlineData("public")]
    public void AssemblyVisibleNestedEndpoint_IsMapped_WithNoDiagnostic(string modifier)
    {
        var source = Fixture($$"""
            public partial class Outer
            {
                {{modifier}} partial class GetUser : IGetEndpoint<UserResponse>
                {
                    public static string Path => "/users/{id}";

                    [RouteParam] public int Id { get; set; }

                    public override Task<IResult> HandleAsync(CancellationToken ct)
                        => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
                }
            }
            """);

        Assert.Empty(EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics);
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));

        var generated = Generated(source);
        Assert.Contains("Map<global::Fixtures.Outer.GetUser", generated);
        Assert.Contains("typeof(global::Fixtures.Outer.GetUser)", generated);
    }

    /// <summary>
    /// A <c>file</c> type cannot be named from another file at all. It gets no generated partial
    /// either: one would declare a second, unrelated <c>GetUser</c> in the generated file.
    /// </summary>
    [Fact]
    public void FileLocalRawEndpoint_IsNotMapped_AndReportsMPEP024_Only()
    {
        var source = Fixture("""
            file class Health : IGetEndpoint
            {
                public static string Path => "/health";

                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """);

        var diagnostic = AssertSoleError(source);

        Assert.Equal("Health", TextAt(diagnostic.Location));
        Assert.Contains("because it is a file-local type", diagnostic.GetMessage());
        Assert.DoesNotContain("Health", Generated(source));
    }

    /// <summary>
    /// A typed endpoint nested in a <c>file</c> type: MPEP024 names the container. No partial is
    /// emitted — reopening the container in the generated file would declare a different type — so,
    /// as with MPEP019 on a typed endpoint, the missing base class cascades. Pinned so that anything
    /// further, such as an error inside generated code, is noticed.
    /// </summary>
    [Fact]
    public void TypedEndpoint_NestedInFileLocalType_ReportsMPEP024_First()
    {
        var source = Fixture("""
            file partial class Outer
            {
                public partial class GetUser : IGetEndpoint<UserResponse>
                {
                    public static string Path => "/users";

                    public override Task<IResult> HandleAsync(CancellationToken ct)
                        => Task.FromResult(Results.Ok(new UserResponse(1, "Alice")));
                }
            }
            """);

        var diagnostic = Assert.Single(EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics);
        Assert.Equal("MPEP024", diagnostic.Id);
        Assert.Contains("because it is nested inside the file-local type 'Outer'", diagnostic.GetMessage());
        Assert.DoesNotContain("Outer", Generated(source));

        var errors = Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source));
        Assert.Equal("MPEP024", errors[0].Id);
        Assert.Equal(["CS0115", "CS0535"], errors.Skip(1).Select(e => e.Id).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// A group the generated code cannot name takes its members with it: the group gets MPEP024,
    /// and neither it nor the endpoint joining it is mapped. The endpoint is not blamed separately.
    /// </summary>
    [Fact]
    public void PrivateNestedGroup_ReportsMPEP024_AndItsEndpointsAreNotMapped()
    {
        var source = Fixture("""
            public partial class Api
            {
                private sealed class UsersGroup : IEndpointGroup
                {
                    public static string Prefix => "/users";
                }

                [MemberOf<UsersGroup>]
                public class ListUsers : IGetEndpoint
                {
                    public static string Path => "/";

                    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
                }
            }
            """);

        var diagnostic = AssertSoleError(source);

        Assert.Equal("UsersGroup", TextAt(diagnostic.Location));
        Assert.StartsWith("Endpoint group 'UsersGroup' cannot be mapped because it is declared 'private'", diagnostic.GetMessage());

        var generated = Generated(source);
        Assert.DoesNotContain("UsersGroup", generated);
        Assert.DoesNotContain("ListUsers", generated);
    }
}
