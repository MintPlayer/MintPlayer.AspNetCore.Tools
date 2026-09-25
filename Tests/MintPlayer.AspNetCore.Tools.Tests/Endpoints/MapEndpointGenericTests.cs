using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.Endpoints;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// The manual <c>MapEndpoint&lt;T&gt;()</c> path for closed generic endpoints (issue #34, PRD D4, D5,
/// acceptance 4): names follow the generator's <c>{Name}_{TypeArguments}</c> rule, so two closings
/// of one endpoint never collide, and a disabled group maps nothing.
/// </summary>
public class MapEndpointGenericTests
{
    public sealed class Echo<T> : IGetEndpoint
    {
        /// <summary>One route per closing: two closings on one route would be an ambiguous match.</summary>
        public static string Path => "/echo/" + (typeof(T).FullName!.GetHashCode() & 0x7FFFFFFF);

        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(typeof(T).Name));
    }

    [EndpointDescriptorName("renamed")]
    public sealed class Renamed<T> : IGetEndpoint
    {
        public static string Path => "/renamed/" + (typeof(T).FullName!.GetHashCode() & 0x7FFFFFFF);

        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    public sealed class Outer<T>
    {
        public sealed class Inner : IGetEndpoint
        {
            public static string Path => "/inner";

            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }
    }

    public sealed class DisabledGroup : IEndpointGroup
    {
        public static string Prefix => "/off";

        static bool IEndpointGroup.IsEnabled(IServiceProvider services) => false;
    }

    [MemberOf<DisabledGroup>]
    public sealed class InDisabledGroup : IGetEndpoint
    {
        public static string Path => "/x";

        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private static string? NameOf<TEndpoint>() where TEndpoint : class, IEndpoint
    {
        var app = WebApplication.CreateBuilder([]).Build();
        app.MapEndpoint<TEndpoint>();

        return Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints))
            .Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
    }

    [Fact]
    public void ClosedGenericEndpoint_IsNamedAfterItsTypeArguments()
    {
        Assert.Equal("Echo_String", NameOf<Echo<string>>());
        Assert.Equal("Echo_Int32", NameOf<Echo<int>>());
        Assert.Equal("Echo_List_Int32", NameOf<Echo<List<int>>>());
        Assert.Equal("Echo_Int32Array", NameOf<Echo<int[]>>());
    }

    [Fact]
    public void ClosedGenericEndpoint_WithADescriptorName_KeepsTheTypeArgumentSuffix()
        => Assert.Equal("renamed_String", NameOf<Renamed<string>>());

    [Fact]
    public void EndpointNestedInAClosedGenericType_IsNamedAfterTheContainersTypeArguments()
        => Assert.Equal("Inner_String", NameOf<Outer<string>.Inner>());

    /// <summary>
    /// Two closings in one app: both answer. With the old <c>Type.Name</c> rule both were named
    /// <c>Echo`1</c>, and ASP.NET Core threw <c>Duplicate endpoint name</c> on the first request — and
    /// on the OpenAPI document, whose <c>operationId</c> is the endpoint name.
    /// </summary>
    /// <remarks>
    /// The document itself is not built here: calling <c>AddOpenApi()</c> in this project switches on
    /// the OpenAPI package's XML-comment interceptors (CS9137). Unique names are unique operationIds;
    /// the TestApp's document test covers closed endpoints end to end.
    /// </remarks>
    [Fact]
    public async Task TwoClosings_AnswerOnTheirRoutes_WithDistinctNames()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapEndpoint<Echo<string>>();
        app.MapEndpoint<Echo<int>>();
        await app.StartAsync();
        var client = app.GetTestClient();

        Assert.Equal("\"String\"", await client.GetStringAsync(Echo<string>.Path));
        Assert.Equal("\"Int32\"", await client.GetStringAsync(Echo<int>.Path));

        var names = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "(unnamed)")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Echo_Int32", "Echo_String"], names);
    }

    /// <summary>PRD D5: <c>IsEnabled</c> is honoured on the manual path too.</summary>
    [Fact]
    public void EndpointInADisabledGroup_IsNotMapped()
    {
        var app = WebApplication.CreateBuilder([]).Build();

        Assert.Same(app, app.MapEndpoint<InDisabledGroup>());
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));
    }
}
