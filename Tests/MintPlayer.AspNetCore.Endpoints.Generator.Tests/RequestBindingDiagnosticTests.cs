using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// The diagnostics M4 added for bound properties: MPEP013 (type cannot be converted), MPEP014
/// (bound properties but not <c>partial</c>), MPEP019 (nested inside a non-partial type) and MPEP020
/// (no usable setter).
/// </summary>
/// <remarks>
/// Each is asserted three ways: it fires, it points at the right token with the right message, and
/// — PRD R3.4 — compiling the fixture together with what the generator emitted produces <b>no
/// <c>CS</c> error alongside it</b>. The failure mode R3.4 exists for is a generator that bails on a
/// bad input and leaves the consumer reading a <c>CS0534</c>/<c>CS0115</c> in their own file, acting
/// on it, and never seeing the real cause.
/// </remarks>
public class RequestBindingDiagnosticTests
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

    private static Diagnostic[] Errors(IEnumerable<Diagnostic> diagnostics)
        => [.. diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];

    /// <summary>
    /// Asserts the generator reports exactly one diagnostic, <paramref name="id"/>, and that the
    /// full compile has that as its only error — no cascading <c>CS</c> error.
    /// </summary>
    private static Diagnostic AssertSoleError(string source, string id)
    {
        var result = EndpointGeneratorHarness.Run("Fixtures", source);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        var errors = Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source));
        Assert.True(
            errors.Length == 1 && errors[0].Id == id,
            $"expected {id} as the only error, got: {string.Join(" | ", errors.Select(e => $"{e.Id}: {e.GetMessage()}"))}");

        return diagnostic;
    }

    // ---- MPEP013 --------------------------------------------------------------------------------

    /// <summary>
    /// A bound property whose type is not <c>string</c>, an enum or <c>IParsable&lt;T&gt;</c> is MPEP013 —
    /// there is no reflective fallback (R2.5).
    /// </summary>
    /// <remarks>
    /// The property is skipped rather than aborting the endpoint: the other bound property still
    /// binds, and the base class is still emitted, so the consumer's <c>override</c> still resolves
    /// and there is nothing to cascade.
    /// </remarks>
    [Fact]
    public void UnsupportedBoundPropertyType_ReportsMPEP013_OnTheProperty()
    {
        var source = Fixture("""
            public partial class GetThing : IGetEndpoint<UserResponse>
            {
                public static string Path => "/things/{id}/{other}";

                [RouteParam] public object Id { get; set; } = null!;
                [RouteParam] public int Other { get; set; }

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(Other, "x")));
            }
            """);

        var diagnostic = AssertSoleError(source, "MPEP013");

        Assert.Equal("Id", TextAt(diagnostic.Location));
        Assert.Equal(
            "Property 'Id' is bound from the route but its type 'object' is not string, an enum, or a type implementing IParsable<object>",
            diagnostic.GetMessage());

        var generated = string.Join("\n", EndpointGeneratorHarness.Run("Fixtures", source).GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("this.Other = ", generated);
        Assert.DoesNotContain("this.Id = ", generated);
    }

    /// <summary>MPEP013 names the query string as the source for a <c>[QueryParam]</c>.</summary>
    [Fact]
    public void UnsupportedQueryPropertyType_ReportsMPEP013_NamingTheQueryString()
    {
        var source = Fixture("""
            public partial class ListThings : IGetEndpoint
            {
                public static string Path => "/things";

                [QueryParam] public System.Collections.Generic.List<int>? Ids { get; set; }

                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """);

        var diagnostic = AssertSoleError(source, "MPEP013");

        Assert.Equal("Ids", TextAt(diagnostic.Location));
        Assert.StartsWith("Property 'Ids' is bound from the query string but its type 'List<int>", diagnostic.GetMessage());
    }

    // ---- MPEP014 --------------------------------------------------------------------------------

    /// <summary>
    /// A raw endpoint with a bound property must be <c>partial</c> — the binder is a generated
    /// explicit <c>IParameterBinder</c> implementation, and there is nowhere else to put it.
    /// </summary>
    /// <remarks>
    /// Without MPEP014 this shape compiles cleanly and the property silently stays at its default on
    /// every request — the worst kind of failure, because nothing points at it.
    /// </remarks>
    [Fact]
    public void RawEndpointWithBoundProperty_NotPartial_ReportsMPEP014()
    {
        var source = Fixture("""
            public class DeleteUser : IDeleteEndpoint
            {
                public static string Path => "/users/{id}";

                [RouteParam] public int Id { get; set; }

                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.NoContent());
            }
            """);

        var diagnostic = AssertSoleError(source, "MPEP014");

        Assert.Equal("DeleteUser", TextAt(diagnostic.Location));
        Assert.Equal(
            "Endpoint class 'DeleteUser' has [RouteParam]/[QueryParam] properties and must be declared as partial so they can be bound",
            diagnostic.GetMessage());
    }

    /// <summary>
    /// A typed endpoint whose own base class already reaches an endpoint base needs no
    /// <c>partial</c> for its base clause — MPEP001 does not apply — but it does for its binder, so
    /// it gets MPEP014.
    /// </summary>
    [Fact]
    public void TypedEndpointWithExistingBase_AndBoundProperty_NotPartial_ReportsMPEP014()
    {
        var source = Fixture("""
            public abstract class UsersBase : ResponseEndpoint, IGetEndpoint<UserResponse>
            {
                public static string Path => "/users/{id}";
            }

            public class GetUser : UsersBase
            {
                [RouteParam] public int Id { get; set; }

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
            }
            """);

        var diagnostic = AssertSoleError(source, "MPEP014");

        Assert.Equal("GetUser", TextAt(diagnostic.Location));
    }

    /// <summary>
    /// Where MPEP001 already applies — a typed endpoint with no base class of its own — MPEP014 is
    /// not reported as well. Both say "add <c>partial</c>"; saying it twice is noise that makes the
    /// consumer wonder whether there are two problems.
    /// </summary>
    [Fact]
    public void TypedEndpointWithBoundProperty_NotPartial_ReportsMPEP001_NotMPEP014()
    {
        var source = Fixture("""
            public class GetUser : IGetEndpoint<UserResponse>
            {
                public static string Path => "/users/{id}";

                [RouteParam] public int Id { get; set; }

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
            }
            """);

        var result = EndpointGeneratorHarness.Run("Fixtures", source);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MPEP001", diagnostic.Id);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "MPEP014");
    }

    // ---- MPEP019 --------------------------------------------------------------------------------

    /// <summary>
    /// An endpoint that needs a generated partial but sits inside a non-partial type gets MPEP019
    /// naming the offending container, and no partial is emitted — reopening a non-partial container
    /// would itself be a compile error in generated code the consumer cannot edit.
    /// </summary>
    /// <remarks>
    /// Asserted on a raw endpoint with a bound property, which is the shape where skipping the
    /// partial leaves nothing else broken, so MPEP019 must be the only error. (A <i>typed</i>
    /// endpoint in the same position also loses its generated base class; see the next test.)
    /// </remarks>
    [Fact]
    public void RawBoundEndpoint_NestedInNonPartialType_ReportsMPEP019_Only()
    {
        var source = Fixture("""
            public static class Outer
            {
                public partial class DeleteUser : IDeleteEndpoint
                {
                    public static string Path => "/users/{id}";

                    [RouteParam] public int Id { get; set; }

                    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.NoContent());
                }
            }
            """);

        var diagnostic = AssertSoleError(source, "MPEP019");

        Assert.Equal("DeleteUser", TextAt(diagnostic.Location));
        Assert.Equal(
            "Endpoint class 'DeleteUser' is nested inside 'Outer', which must also be declared as partial for the endpoint's generated code to reach it",
            diagnostic.GetMessage());

        var generated = string.Join("\n", EndpointGeneratorHarness.Run("Fixtures", source).GeneratedTrees.Select(t => t.ToString()));
        Assert.DoesNotContain("partial class Outer", generated);
    }

    /// <summary>
    /// A typed endpoint nested in a non-partial type also gets MPEP019, and it is reported by the
    /// generator — so it precedes whatever the compiler says about the missing base class.
    /// </summary>
    /// <remarks>
    /// C# offers no way to add a part to a type nested in a non-partial container, so the base
    /// class cannot be supplied and a throwing stub (R3.4) cannot be emitted either: this is the one
    /// M4 diagnostic that cannot be cascade-free. The generator's diagnostic arriving first, naming
    /// the container, is the most that can be done here.
    /// </remarks>
    [Fact]
    public void TypedEndpoint_NestedInNonPartialType_ReportsMPEP019_First()
    {
        var source = Fixture("""
            public static class Outer
            {
                public partial class GetUser : IGetEndpoint<UserResponse>
                {
                    public static string Path => "/users/{id}";

                    [RouteParam] public int Id { get; set; }

                    public override Task<IResult> HandleAsync(CancellationToken ct)
                        => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
                }
            }
            """);

        var result = EndpointGeneratorHarness.Run("Fixtures", source);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MPEP019", diagnostic.Id);
        Assert.Equal("GetUser", TextAt(diagnostic.Location));

        var errors = Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source));
        Assert.Equal("MPEP019", errors[0].Id);

        // Measured residue: the missing ResponseEndpoint base surfaces as CS0115 on the override and
        // CS0535 on IEndpoint.HandleAsync(HttpContext). Pinned so that anything further — a CS error
        // inside generated code, say — is noticed rather than absorbed.
        Assert.Equal(["CS0115", "CS0535"], errors.Skip(1).Select(e => e.Id).Order(StringComparer.Ordinal).ToArray());
    }

    // ---- MPEP020 --------------------------------------------------------------------------------

    /// <summary>
    /// A bound property the generated binder cannot assign is MPEP020: <c>init</c>-only, get-only,
    /// or a <c>private set</c> declared on a base class (the generated partial is the concrete
    /// endpoint, which cannot see it).
    /// </summary>
    /// <remarks>
    /// Without it the first two are a CS0200/CS8852 inside generated code and the third a CS0272 —
    /// errors in a file the consumer cannot edit, naming a line they never wrote. The property is
    /// skipped, so nothing cascades.
    /// </remarks>
    [Theory]
    [InlineData("init-only", "", "[RouteParam] public int Id { get; init; }")]
    [InlineData("get-only", "", "[RouteParam] public int Id { get; }")]
    [InlineData("private-on-base", "[RouteParam] public int Id { get; private set; }", "")]
    public void UnassignableBoundProperty_ReportsMPEP020_OnTheProperty(string shape, string onBase, string onEndpoint)
    {
        _ = shape;
        var source = Fixture($$"""
            public abstract class UsersBase : ResponseEndpoint, IGetEndpoint<UserResponse>
            {
                public static string Path => "/users/{id}";

                {{onBase}}
            }

            public partial class GetUser : UsersBase
            {
                {{onEndpoint}}

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(0, "Alice")));
            }
            """);

        var diagnostic = AssertSoleError(source, "MPEP020");

        Assert.Equal("Id", TextAt(diagnostic.Location));
        Assert.Equal(
            "Property 'Id' is bound from the route but has no setter the endpoint's generated code can use; give it a 'set' accessor (not 'init', and not 'private' on a base class)",
            diagnostic.GetMessage());
    }
}
