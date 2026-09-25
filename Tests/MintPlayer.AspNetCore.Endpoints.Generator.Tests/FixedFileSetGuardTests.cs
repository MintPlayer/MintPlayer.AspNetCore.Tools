using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// The endpoint generator emits a fixed set of files: the hint names do not depend on how many
/// endpoints there are, what they are called, or which namespaces they live in (PRD addendum 2, D23,
/// following MintPlayer.Dotnet.Tools' <c>FixedFileSetGuardTests</c>).
/// </summary>
/// <remarks>
/// A hint name derived from an input grows with it, and past 255 characters a build with
/// <c>EmitCompilerGeneratedFiles</c> fails with <c>CS0016</c>; one derived from a type name is also
/// renamed whenever the type is. Each run here has one or five endpoints, each in a file and a namespace
/// of its own and named <c>Item{i}</c>, so a per-input file or a name built from one changes the set.
/// The typed client's guard is <see cref="EndpointClientGeneratorTests.Client_EmitsTheSameFiles_ForOneReferencedServerAndForFive"/>.
/// </remarks>
public class FixedFileSetGuardTests
{
    private static string[] Corpus(int count) =>
    [
        .. Enumerable.Range(1, count).Select(i => $$"""
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using MintPlayer.AspNetCore.Endpoints;

            namespace Demo.N{{i}};

            public class Area{{i}}Group : IEndpointGroup
            {
                public static string Prefix => "/area{{i}}";
            }

            [MemberOf<Area{{i}}Group>]
            public partial class Item{{i}} : IGetEndpoint
            {
                public static string Path => "/item/{id}";
                [RouteParam] public int Id { get; set; }
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(Id));
            }
            """),
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Server_EmitsTheSameFiles_ForOneEndpointAndForFive(bool includeOpenApi)
    {
        var one = EndpointGeneratorHarness.CreateTrackingDriver()
            .RunGenerators(EndpointGeneratorHarness.CreateCompilation("Demo", Corpus(1), includeOpenApi: includeOpenApi))
            .GetRunResult();
        var five = EndpointGeneratorHarness.CreateTrackingDriver()
            .RunGenerators(EndpointGeneratorHarness.CreateCompilation("Demo", Corpus(5), includeOpenApi: includeOpenApi))
            .GetRunResult();

        Assert.NotEmpty(one.GeneratedTrees);
        var text = string.Join("\n", five.GeneratedTrees.Select(tree => tree.ToString()));
        foreach (var i in Enumerable.Range(1, 5))
            Assert.Contains($"global::Demo.N{i}.Item{i}", text);

        Assert.Equal(HintNames(one), HintNames(five));
        Assert.Equal(includeOpenApi, HintNames(five).Contains("EndpointOpenApi.g.cs"));
    }

    private static string[] HintNames(Microsoft.CodeAnalysis.GeneratorDriverRunResult result) =>
    [
        .. result.Results
            .SelectMany(generatorResult => generatorResult.GeneratedSources)
            .Select(source => source.HintName)
            .OrderBy(name => name, StringComparer.Ordinal),
    ];
}
