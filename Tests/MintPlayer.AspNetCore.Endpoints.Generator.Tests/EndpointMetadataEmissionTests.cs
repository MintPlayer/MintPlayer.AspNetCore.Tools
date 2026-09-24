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
    // R2.14a: a body-less verb that declares a request anyway takes a body, so it gets a base that
    // binds one like a POST — not the deleted abstract NonBodyEndpoint.
    [InlineData("IGetEndpoint<UserRequest, UserResponse>", "GetEndpoint<global::Fixtures.UserRequest>")]
    [InlineData("IDeleteEndpoint<UserRequest, UserResponse>", "DeleteEndpoint<global::Fixtures.UserRequest>")]
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
    /// The body-less verbs in the corpus, as M4 reshaped them: <c>GetUser : IGetEndpoint&lt;UserResponse&gt;</c>
    /// gets the response-only <c>ResponseEndpoint</c> base plus a <c>BindParameters</c> override,
    /// and the raw <c>DeleteUser : IDeleteEndpoint</c> gets no base class at all — only an explicit
    /// <c>IParameterBinder</c> for its <c>[RouteParam]</c>.
    /// </summary>
    /// <remarks>
    /// Rewritten deliberately for M4. It used to assert that GET and DELETE received the
    /// <c>GetEndpoint&lt;TRequest&gt;</c>/<c>DeleteEndpoint&lt;TRequest&gt;</c> bases with an abstract
    /// <c>BindRequestAsync</c> the user had to write; that rung no longer exists (PRD R2.14,
    /// R2.14b). The failure it now catches is an arity-1 GET or DELETE being routed back to a
    /// request-typed base, which would make the consumer's <c>HandleAsync(CancellationToken)</c>
    /// override a CS0115.
    /// </remarks>
    [Fact]
    public void BodylessEndpoints_GetTheResponseOnlyBaseOrARawBinder()
    {
        var generated = Generated("Fixtures", FixtureSources.Corpus);

        Assert.Contains("partial class GetUser : global::MintPlayer.AspNetCore.Endpoints.ResponseEndpoint", generated);
        Assert.Contains("partial class DeleteUser : global::MintPlayer.AspNetCore.Endpoints.IParameterBinder", generated);
        Assert.DoesNotContain("Endpoints.GetEndpoint<", generated);
        Assert.DoesNotContain("Endpoints.DeleteEndpoint<", generated);
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

        var created = Assert.Single(routes, route =>
            route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));
        var produces = Assert.Single(created.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>());
        Assert.Equal(201, produces.StatusCode);

        // The default is 200, and it must survive the presence of an override elsewhere.
        var patched = Assert.Single(routes, route =>
            route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("PATCH"));
        Assert.Equal(200, Assert.Single(patched.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()).StatusCode);

        // A raw endpoint declares no response type, so it gets no Produces metadata at all.
        var health = Assert.Single(routes, route => route.RoutePattern.RawText == "/health");
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
    /// The descriptor's <c>Path</c> is the fully resolved route, group prefixes composed in.
    /// </summary>
    /// <remarks>
    /// <c>Describe&lt;T&gt;</c> used to pass <c>TEndpoint.Path</c> straight through, so an endpoint in
    /// <c>/api/users</c> was described as <c>"/{id}"</c>. The descriptor list is public API and its
    /// obvious use is a diagnostics or discovery page, for which every grouped entry was useless. The
    /// generator is the only component that knows the chain, so it bakes the chain into the call and
    /// the prefixes themselves are read at runtime from each group's <c>Prefix</c>.
    /// </remarks>
    [Fact]
    public void DescriptorPath_IsTheResolvedRoute()
    {
        const string assemblyName = "Fixtures.DescriptorPaths";
        var generated = EndpointGeneratorHarness.RunAndLoad(assemblyName, FixtureSources.Corpus);

        var descriptors = GeneratedEndpointHost.Descriptors(generated, assemblyName);

        Assert.Equal("/api/users/{id}", Assert.Single(descriptors, d => d.Name == "GetUser").Path);
        Assert.Equal("/api/users/", Assert.Single(descriptors, d => d.Name == "ListUsers").Path);
        Assert.Equal("/api/products/", Assert.Single(descriptors, d => d.Name == "ListProducts").Path);

        // An ungrouped endpoint's path was always right, which is what hid this.
        Assert.Equal("/health", Assert.Single(descriptors, d => d.Name == "HealthCheck").Path);
    }

    /// <summary>
    /// Every descriptor path matches a route the endpoint is actually registered at.
    /// </summary>
    /// <remarks>
    /// The prefix chain in the descriptor and the <c>MapGroup</c> nesting in the mapping method come
    /// from the same plan; this is what stops the two drifting apart again.
    /// </remarks>
    [Fact]
    public void DescriptorPaths_MatchTheRegisteredRoutes()
    {
        const string assemblyName = "Fixtures.DescriptorRoutes";
        var generated = EndpointGeneratorHarness.RunAndLoad(assemblyName, FixtureSources.Corpus);

        var routePaths = GeneratedEndpointHost
            .MapAndCollectRoutes(generated, assemblyName)
            .Select(route => route.RoutePattern.RawText!)
            .ToHashSet(StringComparer.Ordinal);

        var descriptors = GeneratedEndpointHost.Descriptors(generated, assemblyName);

        Assert.All(descriptors, descriptor => Assert.Contains(descriptor.Path, routePaths));
    }

    /// <summary>
    /// <c>[EndpointDescriptorName]</c> names the descriptor; without it the class name is used.
    /// </summary>
    /// <remarks>
    /// The attribute shipped in the Abstractions package with exactly one plausible purpose — to
    /// supply this name — and nothing read it, so it was dead weight that read like a feature. It is
    /// also renamed: its old short name, <c>EndpointName</c>, is taken by
    /// <c>Microsoft.AspNetCore.Routing.EndpointNameAttribute</c>, whose namespace is an implicit
    /// global using in every Web SDK project, so writing <c>[EndpointName("x")]</c> alongside
    /// <c>using MintPlayer.AspNetCore.Endpoints;</c> was a CS0104 ambiguity error.
    /// </remarks>
    [Fact]
    public void EndpointDescriptorNameAttribute_NamesTheDescriptor()
    {
        var source = $$"""
            {{Preamble}}

            [EndpointDescriptorName("Health")]
            public class HealthCheckEndpoint : IGetEndpoint
            {
                public static string Path => "/health";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }

            public class UnnamedEndpoint : IGetEndpoint
            {
                public static string Path => "/unnamed";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        var generated = Generated("Fixtures", source);

        Assert.Contains("""Describe<global::Fixtures.HealthCheckEndpoint>("Health", "")""", generated);
        Assert.Contains("""Describe<global::Fixtures.UnnamedEndpoint>("UnnamedEndpoint", "")""", generated);
    }

    /// <summary>
    /// The attribute's short name is usable next to the library's own namespace import — which is the
    /// point of the rename.
    /// </summary>
    /// <remarks>
    /// The fixture is written the way a consumer writes it: <c>using MintPlayer.AspNetCore.Endpoints;</c>
    /// plus the Web SDK's global usings, both supplied by the harness. Under the old name that
    /// combination did not compile.
    /// </remarks>
    [Fact]
    public void EndpointDescriptorNameAttribute_ShortNameIsNotAmbiguous()
    {
        var source = $$"""
            {{Preamble}}

            [EndpointDescriptorName("Health")]
            public class HealthCheckEndpoint : IGetEndpoint
            {
                public static string Path => "/health";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        var diagnostics = EndpointGeneratorHarness.RunAndCompile("Fixtures", source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Each endpoint's factory field index is its position in emission order, and the descriptor list
    /// uses the same order.
    /// </summary>
    /// <remarks>
    /// The lookup behind that used to be <c>valid.IndexOf(ep)</c> — a linear scan per endpoint,
    /// quadratic in the number of endpoints, on every keystroke in the IDE. It is a dictionary now,
    /// and this is the observable contract the replacement has to keep: field <c>_f{i}</c> belongs to
    /// the i-th endpoint in emission order, and emission order is the ordinal sort.
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

        // Ordinally, Endpoint10 and Endpoint11 sort between Endpoint1 and Endpoint2 — so this is a
        // real assertion about the order, not a restatement of the source order.
        var order = Enumerable.Range(0, 12)
            .Select(i => $"Endpoint{i}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        for (var i = 0; i < order.Length; i++)
        {
            Assert.Contains($"ObjectFactory<global::Fixtures.{order[i]}> _f{i} =", generated);
            Assert.Contains($"Map<global::Fixtures.{order[i]}>(app, _f{i});", generated);
        }

        var describePositions = order
            .Select(name => generated.IndexOf($"Describe<global::Fixtures.{name}>", StringComparison.Ordinal))
            .ToArray();

        Assert.All(describePositions, position => Assert.True(position >= 0));
        Assert.Equal(describePositions.OrderBy(position => position), describePositions);
    }
}
