using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
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

    /// <summary>An endpoint whose route comes from configuration (#37); without the key it maps at <c>Path</c>.</summary>
    public sealed class ConfiguredPath : IGetEndpoint
    {
        public const string Key = "Tests:ConfiguredPath";

        public static string Path => "/hooks/{id}";

        public static string? GetPath(IServiceProvider services) => services.GetRequiredService<IConfiguration>()[Key];

        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(httpContext.Request.RouteValues["id"]));
    }

    /// <summary>An override that returns null: null always means <c>Path</c>, never "unmapped".</summary>
    public sealed class NullPath : IGetEndpoint
    {
        public static string Path => "/null-path";

        static string? IEndpointBase.GetPath(IServiceProvider services) => null;

        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    private static WebApplication AppWith(string? configuredPath)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (configuredPath is not null)
            builder.Configuration[ConfiguredPath.Key] = configuredPath;
        return builder.Build();
    }

    private static string? RouteOf(IEndpointRouteBuilder app)
        => Assert.Single(app.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()).RoutePattern.RawText;

    /// <summary>
    /// AC4 (#37) on the manual path: the configured route answers and the literal <c>Path</c> does not.
    /// </summary>
    [Fact]
    public async Task GetPathFromConfiguration_AnswersOnTheConfiguredRoute_NotOnPath()
    {
        await using var app = AppWith("/custom/{id}");
        app.MapEndpoint<ConfiguredPath>();
        await app.StartAsync();
        var client = app.GetTestClient();

        Assert.Equal("\"5\"", await client.GetStringAsync("/custom/5"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/hooks/5")).StatusCode);
    }

    /// <summary>AC4: unconfigured, an override returning null, and no override at all all map at <c>Path</c>.</summary>
    [Fact]
    public void NullFromGetPath_AndNoOverride_MapAtPath()
    {
        var unconfigured = AppWith(null);
        unconfigured.MapEndpoint<ConfiguredPath>();
        Assert.Equal("/hooks/{id}", RouteOf(unconfigured));

        var nullPath = AppWith(null);
        nullPath.MapEndpoint<NullPath>();
        Assert.Equal("/null-path", RouteOf(nullPath));

        var plain = AppWith(null);
        plain.MapEndpoint<Echo<string>>();
        Assert.Equal(Echo<string>.Path, RouteOf(plain));
    }

    /// <summary>
    /// AC10 (R2.10) on the manual path: a configured route whose parameter names differ from
    /// <c>Path</c>'s fails at map time, naming the endpoint and both patterns, and maps nothing.
    /// </summary>
    [Fact]
    public void GetPathWithOtherParameters_Throws_AndMapsNothing()
    {
        var app = AppWith("/x/{key}");

        var failure = Assert.Throws<InvalidOperationException>(() => app.MapEndpoint<ConfiguredPath>());

        Assert.Contains(typeof(ConfiguredPath).FullName!, failure.Message);
        Assert.Contains("/x/{key}", failure.Message);
        Assert.Contains("/hooks/{id}", failure.Message);
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));
    }

    /// <summary>AC10: literal segments, a constraint and the parameter's case may differ.</summary>
    [Fact]
    public void GetPathWithOtherLiteralsConstraintsOrCase_Maps()
    {
        var app = AppWith("/custom/{ID:int}");

        app.MapEndpoint<ConfiguredPath>();

        Assert.Equal("/custom/{ID:int}", RouteOf(app));
    }

    /// <summary>An endpoint that switches itself off, with hooks that count their calls (PRD R5).</summary>
    public sealed class SwitchedOff : IGetEndpoint
    {
        public static int GetPathCalls;
        public static int ConfigureCalls;

        public static string Path => "/switched-off";

        static bool IEndpointBase.IsEnabled(IServiceProvider services) => false;

        public static string? GetPath(IServiceProvider services)
        {
            GetPathCalls++;
            return null;
        }

        public static void Configure(RouteHandlerBuilder builder, IServiceProvider services) => ConfigureCalls++;

        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    /// <summary>
    /// AC11 (R5.4) on the manual path: a disabled endpoint returns <c>app</c> unmapped, exactly as a
    /// disabled group does, and its <c>GetPath</c> and <c>Configure</c> are never called.
    /// </summary>
    [Fact]
    public void DisabledEndpoint_IsNotMapped_AndItsHooksNeverRun()
    {
        SwitchedOff.GetPathCalls = SwitchedOff.ConfigureCalls = 0;
        var app = WebApplication.CreateBuilder([]).Build();

        Assert.Same(app, app.MapEndpoint<SwitchedOff>());
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));
        Assert.Equal(0, SwitchedOff.GetPathCalls);
        Assert.Equal(0, SwitchedOff.ConfigureCalls);
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
