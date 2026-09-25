using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using MintPlayer.AspNetCore.Endpoints.Generator.CodeFixes;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// The "Make 'X' partial" code fix for MPEP001, MPEP014 and MPEP019 (M11): applying it, not only
/// reporting the diagnostic, is what proves the repair.
/// </summary>
/// <remarks>
/// <para>
/// The diagnostics come from the SOURCE GENERATOR, so the usual
/// <c>CSharpCodeFixTest&lt;TAnalyzer, TCodeFix&gt;</c> shape takes <see cref="EmptyDiagnosticAnalyzer"/>
/// in the analyzer slot and gets the diagnostics from <c>GetSourceGenerators()</c>, which
/// Microsoft.CodeAnalysis.Testing 1.1.2 folds into the set it offers the fix provider.
/// </para>
/// <para>
/// The verifier is <see cref="DefaultVerifier"/>, not <c>XUnitVerifier</c>: the <c>.XUnit</c> packages
/// stop at 1.1.2, built against xunit 2.4.x, and on xunit 2.9.3 every assertion throws
/// <c>MissingMethodException</c> before it can report anything (spike S5).
/// </para>
/// <para>
/// The cascading compiler errors are declared in the source state, and the fixed state declares
/// none: the harness compiles the fixed state together with what the generator emits for it, so each
/// test also proves the repaired document compiles.
/// </para>
/// </remarks>
public class MakePartialCodeFixTests
{
    private const string Preamble = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;

        public record UserRequest(int Id);
        public record UserResponse(int Id, string Name);
        """;

    private static string Fixture(string body) => Preamble + "\n\n" + body;

    private sealed class Test : CSharpCodeFixTest<EmptyDiagnosticAnalyzer, MakePartialCodeFixProvider, DefaultVerifier>
    {
        public Test(string source, string fixedSource)
        {
            TestCode = Fixture(source);
            FixedCode = Fixture(fixedSource);

            // Basic.Reference.Assemblies has no ASP.NET Core; like the generator harness, the fixture
            // compiles against this test host's own assemblies. A ReferenceAssemblies with no package
            // resolves and downloads nothing.
            ReferenceAssemblies = new ReferenceAssemblies("net");
            SolutionTransforms.Add((solution, projectId) => solution.AddMetadataReferences(projectId, EndpointGeneratorHarness.DefaultReferences));

            // The generator's output differs between the two states by design (the fixed state gains
            // the generated partial). Declaring it verbatim would make these tests change-detectors
            // for the generator's output, which its own suites cover; the edit is what is under test.
            TestBehaviors |= TestBehaviors.SkipGeneratedSourcesCheck;
        }

        protected override IEnumerable<Type> GetSourceGenerators() => [typeof(EndpointGenerator)];
    }

    [Fact]
    public async Task MPEP001_AppendsPartialAfterTheExistingModifiers()
    {
        await new Test(
            """
            public sealed class {|MPEP001:CreateUser|} : {|CS0535:IPostEndpoint<UserRequest, UserResponse>|}
            {
                public static string Path => "/users";

                public override Task<IResult> {|CS0115:HandleAsync|}(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """,
            """
            public sealed partial class CreateUser : IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/users";

                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """).RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// With no modifiers, <c>partial</c> becomes the first token of the declaration proper and takes
    /// over the keyword's leading trivia; the attribute line above it stays untouched.
    /// </summary>
    /// <remarks>
    /// Top level, because a nested type without modifiers is private, and a private endpoint is a
    /// CS0122 in the generated mapping whatever the fix does.
    /// </remarks>
    [Fact]
    public async Task MPEP001_WithoutModifiers_KeepsTheAttribute()
    {
        await new Test(
            """
            [System.Serializable]
            class {|MPEP001:CreateUser|} : {|CS0535:IPostEndpoint<UserRequest, UserResponse>|}
            {
                public static string Path => "/users";

                public override Task<IResult> {|CS0115:HandleAsync|}(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """,
            """
            [System.Serializable]
            partial class CreateUser : IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/users";

                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """).RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MPEP014_MakesTheEndpointWithBoundPropertiesPartial()
    {
        await new Test(
            """
            public abstract class UsersBase : ResponseEndpoint, IGetEndpoint<UserResponse>
            {
                public static string Path => "/users/{id}";
            }

            public class {|MPEP014:GetUser|} : UsersBase
            {
                [RouteParam] public int Id { get; set; }

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
            }
            """,
            """
            public abstract class UsersBase : ResponseEndpoint, IGetEndpoint<UserResponse>
            {
                public static string Path => "/users/{id}";
            }

            public partial class GetUser : UsersBase
            {
                [RouteParam] public int Id { get; set; }

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
            }
            """).RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// MPEP019 is reported on the endpoint but repaired on its containers: every enclosing type that
    /// is not <c>partial</c> becomes <c>partial</c>, and the one that already is stays as it is.
    /// </summary>
    [Fact]
    public async Task MPEP019_MakesEveryNonPartialContainerPartial()
    {
        await new Test(
            """
            public static class Outer
            {
                public partial class Middle
                {
                    internal class Inner
                    {
                        public partial class {|MPEP019:DeleteUser|} : IDeleteEndpoint
                        {
                            public static string Path => "/users/{id}";

                            [RouteParam] public int Id { get; set; }

                            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.NoContent());
                        }
                    }
                }
            }
            """,
            """
            public static partial class Outer
            {
                public partial class Middle
                {
                    internal partial class Inner
                    {
                        public partial class DeleteUser : IDeleteEndpoint
                        {
                            public static string Path => "/users/{id}";

                            [RouteParam] public int Id { get; set; }

                            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.NoContent());
                        }
                    }
                }
            }
            """).RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// Fix All in one pass: two endpoints nested in the same non-partial container ask for the same
    /// edit, which must land once, not twice and not as a conflict; a third, top-level endpoint is
    /// fixed alongside them under the same equivalence key.
    /// </summary>
    [Fact]
    public async Task FixAll_RepairsEveryDiagnosticInTheDocumentInOnePass()
    {
        var test = new Test(
            """
            public static class Outer
            {
                public partial class {|MPEP019:DeleteUser|} : IDeleteEndpoint
                {
                    public static string Path => "/users/{id}";

                    [RouteParam] public int Id { get; set; }

                    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.NoContent());
                }

                public partial class {|MPEP019:DeleteProduct|} : IDeleteEndpoint
                {
                    public static string Path => "/products/{id}";

                    [RouteParam] public int Id { get; set; }

                    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.NoContent());
                }
            }

            public class {|MPEP001:CreateUser|} : {|CS0535:IPostEndpoint<UserRequest, UserResponse>|}
            {
                public static string Path => "/users";

                public override Task<IResult> {|CS0115:HandleAsync|}(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """,
            """
            public static partial class Outer
            {
                public partial class DeleteUser : IDeleteEndpoint
                {
                    public static string Path => "/users/{id}";

                    [RouteParam] public int Id { get; set; }

                    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.NoContent());
                }

                public partial class DeleteProduct : IDeleteEndpoint
                {
                    public static string Path => "/products/{id}";

                    [RouteParam] public int Id { get; set; }

                    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.NoContent());
                }
            }

            public partial class CreateUser : IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/users";

                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """);

        // Applied one at a time: two actions, because fixing either MPEP019 clears both. Applied as
        // Fix All: exactly one pass.
        test.NumberOfIncrementalIterations = 2;
        test.NumberOfFixAllIterations = 1;
        test.CodeActionEquivalenceKey = MakePartialCodeFixProvider.EquivalenceKey;

        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// The harness instantiates the provider directly and never consults MEF, so whether the
    /// lightbulb appears at all rests on these attributes (and on the package layout, which the pack
    /// guards cover).
    /// </summary>
    [Fact]
    public void TheProviderIsExportedForCSharpAndShared()
    {
        var type = typeof(MakePartialCodeFixProvider);

        var export = Assert.Single(type.GetCustomAttributes(typeof(ExportCodeFixProviderAttribute), false).Cast<ExportCodeFixProviderAttribute>());
        Assert.Contains(LanguageNames.CSharp, export.Languages);
        Assert.Single(type.GetCustomAttributes(typeof(SharedAttribute), false));

        Assert.Equal(["MPEP001", "MPEP014", "MPEP019"], new MakePartialCodeFixProvider().FixableDiagnosticIds.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The ids are literals in the code-fix assembly, which does not reference the generator; this
    /// pins them to the descriptors so a renumbering cannot leave the fix listening to nothing.
    /// </summary>
    [Fact]
    public void TheFixableIdsAreTheGeneratorsDescriptors()
    {
        Assert.Equal(MakePartialCodeFixProvider.EndpointMustBePartialId, DiagnosticDescriptors.EndpointMustBePartial.Id);
        Assert.Equal(MakePartialCodeFixProvider.BoundPropertiesNeedPartialId, DiagnosticDescriptors.BoundPropertiesNeedPartial.Id);
        Assert.Equal(MakePartialCodeFixProvider.ContainingTypeNotPartialId, DiagnosticDescriptors.ContainingTypeNotPartial.Id);
    }
}
