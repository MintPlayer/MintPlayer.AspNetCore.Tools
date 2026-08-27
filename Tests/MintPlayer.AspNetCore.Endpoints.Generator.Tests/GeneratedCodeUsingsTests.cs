using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// Pins defect D-G25: the generated code only compiles when the consuming project supplies
/// ASP.NET Core global usings.
/// </summary>
/// <remarks>
/// <para>
/// The generator fully qualifies every <i>type</i> it names with <c>global::</c>, which correctly
/// makes it immune to the consumer's namespace imports — but it then calls the <i>extension
/// methods</i> <c>MapMethods</c>, <c>MapGroup</c> and <c>Produces</c>. An extension-method
/// invocation cannot be resolved from a <c>global::</c> type name; the declaring namespace has to
/// be in scope. The emitted file contains no using directives at all.
/// </para>
/// <para>
/// So it works in <c>Microsoft.NET.Sdk.Web</c> projects with <c>ImplicitUsings</c> enabled — which
/// is what the sample TestApp is, and why this went unnoticed — and fails in a plain
/// <c>Microsoft.NET.Sdk</c> library with <c>FrameworkReference Microsoft.AspNetCore.App</c>, or in
/// any project with <c>&lt;ImplicitUsings&gt;disable&lt;/ImplicitUsings&gt;</c>. Both are entirely
/// reasonable ways to consume the package.
/// </para>
/// <para>
/// The fix (deferred to the defect-fixing milestone) is for the producer to emit its own using
/// directives, or to call the extension methods as plain static invocations
/// (<c>global::Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(routes, …)</c>),
/// which is what "fully qualified" should have meant here. When that lands,
/// <see cref="GeneratedCode_WithoutImplicitUsings_DoesNotCompile_KnownBug"/> flips to asserting no
/// errors and this class's name stops mentioning a bug.
/// </para>
/// </remarks>
public class GeneratedCodeUsingsTests
{
    private const string TypedPostEndpoint = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;

        public record CreateUserRequest(string Name);
        public record CreateUserResponse(int Id);

        public partial class CreateUserEndpoint : IPostEndpoint<CreateUserRequest, CreateUserResponse>
        {
            public static string Path => "/users";

            public override Task<IResult> HandleAsync(CreateUserRequest request, CancellationToken cancellationToken)
                => Task.FromResult(Results.Ok(new CreateUserResponse(1)));
        }
        """;

    /// <summary>The supported case: a Web SDK consumer with implicit usings.</summary>
    [Fact]
    public void GeneratedCode_WithWebSdkImplicitUsings_Compiles()
    {
        var diagnostics = EndpointGeneratorHarness.RunAndCompile("Fixtures", TypedPostEndpoint);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// The defect: without those usings the generated code does not compile, and the errors point
    /// at generated source the consumer cannot edit.
    /// </summary>
    [Fact]
    public void GeneratedCode_WithoutImplicitUsings_DoesNotCompile_KnownBug()
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation(
            "Fixtures",
            [TypedPostEndpoint],
            includeImplicitUsings: false);

        var errors = EndpointGeneratorHarness
            .RunAndCompile(compilation)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        // Every error must be an unresolved extension method in the generated file — if this ever
        // reports something else, the fixture itself has broken and the test is lying.
        Assert.NotEmpty(errors);
        Assert.All(errors, error =>
        {
            Assert.Equal("CS1061", error.Id);
            Assert.Contains("EndpointMapping.g.cs", error.Location.GetLineSpan().Path);
        });

        var messages = string.Join(" | ", errors.Select(e => e.GetMessage()));
        Assert.Contains("MapMethods", messages);
    }
}
