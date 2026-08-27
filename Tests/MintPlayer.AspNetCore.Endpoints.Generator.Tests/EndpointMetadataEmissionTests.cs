using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// What the generator emits per endpoint: the partial base class, the <c>Produces</c> call, the
/// factory fields and the descriptor list — and whether all of it compiles.
/// </summary>
public class EndpointMetadataEmissionTests
{
    private const string Preamble = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;

        public record UserRequest(int Id);
        public record UserResponse(int Id, string Name);
        """;

    private static string Generated(string assemblyName, params string[] sources)
        => string.Join(
            "\n",
            EndpointGeneratorHarness.Run(assemblyName, sources).GeneratedTrees.Select(tree => tree.ToString()));

    /// <summary>
    /// The single highest-value assertion in the suite: everything the sample app demonstrates,
    /// emitted and then compiled. A malformed emit — an unbalanced brace, a wrong base class, an
    /// invalid identifier — shows up nowhere else.
    /// </summary>
    [Fact]
    public void Generate_OutputCompiles_WithZeroErrors()
    {
        var diagnostics = EndpointGeneratorHarness.RunAndCompile("Fixtures", FixtureSources.Corpus);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// The base class carries request binding and the <c>HttpContext</c> bridge, so the verb chosen
    /// here decides whether the user has to write <c>BindRequestAsync</c> themselves.
    /// </summary>
    [Theory]
    [InlineData("IPostEndpoint<UserRequest, UserResponse>", "PostEndpoint<global::Fixtures.UserRequest>")]
    [InlineData("IPutEndpoint<UserRequest, UserResponse>", "PutEndpoint<global::Fixtures.UserRequest>")]
    [InlineData("IPatchEndpoint<UserRequest, UserResponse>", "PatchEndpoint<global::Fixtures.UserRequest>")]
    [InlineData("IEndpoint<UserRequest, UserResponse>", "EndpointBase<global::Fixtures.UserRequest>")]
    public void TypedEndpoint_GetsThePartialBaseClassForItsVerb(string endpointInterface, string expectedBaseClass)
    {
        var source = $$"""
            {{Preamble}}

            public partial class MyEndpoint : {{endpointInterface}}
            {
                public static string Path => "/my";
                public static IEnumerable<string> Methods => ["POST"];

                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var generated = Generated("Fixtures", source);

        Assert.Contains(
            $"partial class MyEndpoint : global::MintPlayer.AspNetCore.Endpoints.{expectedBaseClass} {{ }}",
            generated);
    }

    /// <summary>
    /// GET and DELETE get a non-body base, which leaves <c>BindRequestAsync</c> abstract — the user
    /// must supply it. Asserted through the corpus, which does exactly that.
    /// </summary>
    [Fact]
    public void NonBodyEndpoints_GetTheNonBodyBaseClasses()
    {
        var generated = Generated("Fixtures", FixtureSources.Corpus);

        Assert.Contains("partial class GetUser : global::MintPlayer.AspNetCore.Endpoints.GetEndpoint<global::Fixtures.GetUserRequest> { }", generated);
        Assert.Contains("partial class DeleteUser : global::MintPlayer.AspNetCore.Endpoints.DeleteEndpoint<global::Fixtures.GetUserRequest> { }", generated);
    }

    /// <summary>
    /// <c>Produces</c> is the whole point of the typed-with-response level, and emitting it for a
    /// lower level would be wrong: there is no response type to name.
    /// </summary>
    [Fact]
    public void Produces_IsEmittedOnlyForTheTypedWithResponseLevel()
    {
        var source = $$"""
            {{Preamble}}

            public class RawEndpoint : IGetEndpoint
            {
                public static string Path => "/raw";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }

            public partial class TypedEndpoint : IPutEndpoint<UserRequest>
            {
                public static string Path => "/typed";

                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }

            public partial class TypedWithResponseEndpoint : IPostEndpoint<UserRequest, UserResponse>
            {
                public static string Path => "/typed-with-response";

                public override Task<IResult> HandleAsync(UserRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var generated = Generated("Fixtures", source);

        Assert.Contains(
            "Produces<global::Fixtures.TypedWithResponseEndpoint, global::Fixtures.UserRequest, global::Fixtures.UserResponse>(b);",
            generated);
        Assert.DoesNotContain("Produces<global::Fixtures.RawEndpoint", generated);
        Assert.DoesNotContain("Produces<global::Fixtures.TypedEndpoint", generated);
    }

    /// <summary>
    /// The <c>Produces</c> status code is read from <c>TEndpoint.SuccessStatusCode</c>, a static
    /// virtual interface member — so only running the emitted code shows whether an endpoint's
    /// override of it reaches the route metadata.
    /// </summary>
    [Fact]
    public void SuccessStatusCodeOverride_ReachesTheRouteMetadata()
    {
        const string assemblyName = "Fixtures.Produces";
        var generated = EndpointGeneratorHarness.RunAndLoad(assemblyName, FixtureSources.Corpus);

        var routes = GeneratedEndpointHost.MapAndCollectRoutes(generated, assemblyName);

        var created = Assert.Single(routes.Where(route =>
            route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST")));
        var produces = Assert.Single(created.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>());
        Assert.Equal(201, produces.StatusCode);

        // The default is 200, and it must survive the presence of an override elsewhere.
        var patched = Assert.Single(routes.Where(route =>
            route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("PATCH")));
        Assert.Equal(200, Assert.Single(patched.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()).StatusCode);

        // A raw endpoint declares no response type, so it gets no Produces metadata at all.
        var health = Assert.Single(routes.Where(route => route.RoutePattern.RawText == "/health"));
        Assert.Empty(health.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>());
    }

    /// <summary>
    /// One descriptor per mapped endpoint, in the same order as the factory fields.
    /// </summary>
    [Fact]
    public void DescriptorList_HasOneEntryPerMappedEndpoint()
    {
        const string assemblyName = "Fixtures.Descriptors";
        var generated = EndpointGeneratorHarness.RunAndLoad(assemblyName, FixtureSources.Corpus);

        var descriptors = GeneratedEndpointHost.Descriptors(generated, assemblyName);

        Assert.Equal(9, descriptors.Count);
        Assert.Equal(
            new[]
            {
                "CreateUser", "DeleteUser", "GetUser", "HealthCheck", "ListProducts",
                "ListUsers", "PatchUser", "PreflightEndpoint", "UpdateUser",
            },
            descriptors.Select(d => d.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Pins D-G20: the descriptor's <c>Path</c> is the group-relative one, not the route the request
    /// has to use.
    /// </summary>
    /// <remarks>
    /// <c>Describe&lt;T&gt;</c> passes <c>TEndpoint.Path</c> straight through, so an endpoint in
    /// <c>/api/users</c> is described as <c>"/{id}"</c>. The descriptor list is public API — the
    /// obvious use for it is a diagnostics or discovery page — and every grouped entry in it is
    /// unusable for that. The generator is the only component that knows the resolved route.
    /// </remarks>
    [Fact]
    public void DescriptorPath_IsGroupRelativeNotResolved_KnownBug()
    {
        const string assemblyName = "Fixtures.DescriptorPaths";
        var generated = EndpointGeneratorHarness.RunAndLoad(assemblyName, FixtureSources.Corpus);

        var descriptors = GeneratedEndpointHost.Descriptors(generated, assemblyName);

        var getUser = Assert.Single(descriptors.Where(d => d.Name == "GetUser"));
        Assert.Equal("/{id}", getUser.Path);

        var listUsers = Assert.Single(descriptors.Where(d => d.Name == "ListUsers"));
        Assert.Equal("/", listUsers.Path);

        // An ungrouped endpoint's path happens to be right, which is what hides this.
        var health = Assert.Single(descriptors.Where(d => d.Name == "HealthCheck"));
        Assert.Equal("/health", health.Path);
    }

    /// <summary>
    /// Pins D-G3: <c>EndpointNameAttribute</c> is never read — the descriptor's name is always the
    /// class name.
    /// </summary>
    /// <remarks>
    /// The attribute ships in the Abstractions package and has exactly one plausible purpose, which
    /// is to supply this name. Nothing in the generator looks at it, so it is dead weight that reads
    /// like a feature.
    /// </remarks>
    [Fact]
    public void EndpointNameAttribute_IsIgnored_KnownBug()
    {
        var source = $$"""
            {{Preamble}}

            [MintPlayer.AspNetCore.Endpoints.EndpointName("Health")]
            public class HealthCheckEndpoint : IGetEndpoint
            {
                public static string Path => "/health";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        var generated = Generated("Fixtures", source);

        Assert.Contains("Describe<global::Fixtures.HealthCheckEndpoint>(\"HealthCheckEndpoint\")", generated);
        Assert.DoesNotContain("\"Health\"", generated);
    }

    /// <summary>
    /// Pins D-G21: each mapping looks its endpoint up with <c>valid.IndexOf(ep)</c>, which is a
    /// linear scan per endpoint — quadratic in the number of endpoints, on every keystroke in the
    /// IDE.
    /// </summary>
    /// <remarks>
    /// The observable contract is the numbering: field <c>_f{i}</c> belongs to the i-th endpoint in
    /// emission order, and the descriptor list uses that same order. Replacing the scan with a
    /// dictionary must keep that mapping intact, which is what this asserts.
    /// </remarks>
    [Fact]
    public void FactoryFieldNumbering_FollowsEmissionOrder()
    {
        var endpoints = string.Join(
            "\n",
            Enumerable.Range(0, 12).Select(i => $$"""
                public class Endpoint{{i}} : IGetEndpoint
                {
                    public static string Path => "/e{{i}}";
                    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
                }
                """));

        var generated = Generated("Fixtures", $"{Preamble}\n\n{endpoints}");

        for (var i = 0; i < 12; i++)
        {
            Assert.Contains($"ObjectFactory<global::Fixtures.Endpoint{i}> _f{i} =", generated);
            Assert.Contains($"Map<global::Fixtures.Endpoint{i}>(app, _f{i});", generated);
        }
    }
}
