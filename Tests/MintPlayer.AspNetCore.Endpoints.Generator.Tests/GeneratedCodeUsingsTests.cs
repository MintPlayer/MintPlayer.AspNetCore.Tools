using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// The generated code compiles in any project that can reference ASP.NET Core, not only in a
/// <c>Microsoft.NET.Sdk.Web</c> one with implicit usings on.
/// </summary>
/// <remarks>
/// <para>
/// The generator fully qualifies every <i>type</i> it names with <c>global::</c>, which correctly
/// makes it immune to the consumer's namespace imports — and it then calls the <i>extension
/// methods</i> <c>MapMethods</c>, <c>MapGroup</c>, <c>Produces</c> and <c>WithMetadata</c>. An
/// extension-method invocation cannot be resolved from a <c>global::</c> type name; the declaring
/// namespace has to be in scope, and the emitted file contained no using directives at all.
/// </para>
/// <para>
/// So it worked in the sample TestApp — a Web SDK project with implicit usings, which is why this
/// went unnoticed — and failed in a plain <c>Microsoft.NET.Sdk</c> library with
/// <c>FrameworkReference Microsoft.AspNetCore.App</c>, or in any project with
/// <c>&lt;ImplicitUsings&gt;disable&lt;/ImplicitUsings&gt;</c>. Both are entirely reasonable ways to
/// consume the package, and the consumer got three CS1061 errors in generated source they cannot
/// edit. The file now emits the three usings its own calls need.
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

    /// <summary>A Web SDK consumer with implicit usings — the case that always worked.</summary>
    [Fact]
    public void GeneratedCode_WithWebSdkImplicitUsings_Compiles()
    {
        var diagnostics = EndpointGeneratorHarness.RunAndCompile("Fixtures", TypedPostEndpoint);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// A plain SDK consumer, or one with implicit usings disabled — the case that did not.
    /// </summary>
    [Fact]
    public void GeneratedCode_WithoutImplicitUsings_Compiles()
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation(
            "Fixtures",
            [TypedPostEndpoint],
            includeImplicitUsings: false);

        var errors = EndpointGeneratorHarness
            .RunAndCompile(compilation)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(errors);
    }

    /// <summary>
    /// The whole corpus, without implicit usings: the group and <c>Produces</c> calls go through
    /// different namespaces from <c>MapMethods</c>, so one endpoint would not have caught all three.
    /// </summary>
    [Fact]
    public void GeneratedCode_ForEveryShape_WithoutImplicitUsings_Compiles()
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation(
            "Fixtures",
            [FixtureSources.Corpus],
            includeImplicitUsings: false);

        var errors = EndpointGeneratorHarness
            .RunAndCompile(compilation)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(errors);
    }

    /// <summary>The using directives are actually in the file, rather than happening to be in scope.</summary>
    [Fact]
    public void GeneratedFile_EmitsTheUsingsItsExtensionCallsNeed()
    {
        var generated = string.Join(
            "\n",
            EndpointGeneratorHarness.Run("Fixtures", TypedPostEndpoint).GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("using Microsoft.AspNetCore.Builder;", generated);
        Assert.Contains("using Microsoft.AspNetCore.Http;", generated);
        Assert.Contains("using Microsoft.AspNetCore.Routing;", generated);
    }
}
