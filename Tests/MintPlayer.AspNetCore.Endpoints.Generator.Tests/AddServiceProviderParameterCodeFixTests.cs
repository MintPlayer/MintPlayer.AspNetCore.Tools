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
/// The "Add 'IServiceProvider services' parameter" code fix for MPEP035 (#36, AC3): applying it is
/// what proves the migration, since the repaired hook then implements the interface member again.
/// </summary>
/// <remarks>
/// Built like <see cref="MakePartialCodeFixTests"/>: the diagnostic comes from the source generator,
/// so the analyzer slot holds <see cref="EmptyDiagnosticAnalyzer"/>, and the fixed state compiles
/// together with what the generator emits for it.
/// </remarks>
public class AddServiceProviderParameterCodeFixTests
{
    private const string Preamble = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;
        """;

    private static string Fixture(string body) => Preamble + "\n\n" + body;

    private sealed class Test : CSharpCodeFixTest<EmptyDiagnosticAnalyzer, AddServiceProviderParameterCodeFixProvider, DefaultVerifier>
    {
        public Test(string source, string fixedSource)
        {
            TestCode = Fixture(source);
            FixedCode = Fixture(fixedSource);
            ReferenceAssemblies = new ReferenceAssemblies("net");
            SolutionTransforms.Add((solution, projectId) => solution.AddMetadataReferences(projectId, EndpointGeneratorHarness.DefaultReferences));
            TestBehaviors |= TestBehaviors.SkipGeneratedSourcesCheck;
        }

        protected override IEnumerable<Type> GetSourceGenerators() => [typeof(EndpointGenerator)];
    }

    [Fact]
    public async Task MPEP035_OnAGroupHook_AppendsTheServicesParameter()
    {
        await new Test(
            """
            public class UsersApi : IEndpointGroup
            {
                public static string Prefix => "/users";
                public static void {|MPEP035:Configure|}(RouteGroupBuilder group) => group.WithTags("Users");
            }

            [MemberOf<UsersApi>]
            public class ListUsers : IGetEndpoint
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """,
            """
            public class UsersApi : IEndpointGroup
            {
                public static string Prefix => "/users";
                public static void Configure(RouteGroupBuilder group, IServiceProvider services) => group.WithTags("Users");
            }

            [MemberOf<UsersApi>]
            public class ListUsers : IGetEndpoint
            {
                public static string Path => "/";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """).RunAsync(CancellationToken.None);
    }

    /// <summary>Fix All repairs a group hook and an endpoint hook in one pass.</summary>
    [Fact]
    public async Task FixAll_RepairsEveryHookInTheDocumentInOnePass()
    {
        var test = new Test(
            """
            public class UsersApi : IEndpointGroup
            {
                public static string Prefix => "/users";
                public static void {|MPEP035:Configure|}(RouteGroupBuilder group) { }
            }

            [MemberOf<UsersApi>]
            public class ListUsers : IGetEndpoint
            {
                public static string Path => "/";
                public static void {|MPEP035:Configure|}(RouteHandlerBuilder builder)
                {
                    builder.WithDisplayName("list");
                }

                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """,
            """
            public class UsersApi : IEndpointGroup
            {
                public static string Prefix => "/users";
                public static void Configure(RouteGroupBuilder group, IServiceProvider services) { }
            }

            [MemberOf<UsersApi>]
            public class ListUsers : IGetEndpoint
            {
                public static string Path => "/";
                public static void Configure(RouteHandlerBuilder builder, IServiceProvider services)
                {
                    builder.WithDisplayName("list");
                }

                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """);

        test.NumberOfFixAllIterations = 1;
        test.CodeActionEquivalenceKey = AddServiceProviderParameterCodeFixProvider.EquivalenceKey;

        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public void TheProviderIsExportedForCSharpAndShared_AndListensToTheGeneratorsId()
    {
        var type = typeof(AddServiceProviderParameterCodeFixProvider);

        var export = Assert.Single(type.GetCustomAttributes(typeof(ExportCodeFixProviderAttribute), false).Cast<ExportCodeFixProviderAttribute>());
        Assert.Contains(LanguageNames.CSharp, export.Languages);
        Assert.Single(type.GetCustomAttributes(typeof(SharedAttribute), false));

        Assert.Equal(["MPEP035"], new AddServiceProviderParameterCodeFixProvider().FixableDiagnosticIds.ToArray());
        Assert.Equal(AddServiceProviderParameterCodeFixProvider.LegacyConfigureHookIgnoredId, DiagnosticDescriptors.LegacyConfigureHookIgnored.Id);
    }
}
