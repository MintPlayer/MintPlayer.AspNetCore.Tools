using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.OpenSearch;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

public class MapOpenSearchValidationTests
{
    /// <summary>
    /// Minimal <see cref="IEndpointRouteBuilder"/> for the cases where <c>MapOpenSearch</c> is
    /// expected to throw before it ever reaches <c>MapGet</c>.
    /// </summary>
    private sealed class BareEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    /// <summary>Route builder over a fully registered OpenSearch service collection.</summary>
    private static BareEndpointRouteBuilder Routes(Action<OpenSearchOptions> configure)
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddRouting()
            .AddOpenSearch<FakeOpenSearchService>(configure)
            .BuildServiceProvider();

        return new BareEndpointRouteBuilder(provider);
    }

    [Fact]
    public void MapOpenSearch_WithoutAnyOptionsInfrastructure_Throws()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var routes = new BareEndpointRouteBuilder(provider);

        var ex = Assert.Throws<InvalidOperationException>(() => routes.MapOpenSearch());

        Assert.Contains("OpenSearchOptions not initialized", ex.Message);
    }

    /// <summary>
    /// D-S16 fixed. The guard used to be a null check on
    /// <c>IOptions&lt;OpenSearchOptions&gt;</c>, which made it unreachable: once anything has pulled
    /// in the options infrastructure — and <c>AddControllersWithViews</c> always does — the accessor
    /// resolves to a live, default-constructed instance. So a full options setup with no
    /// <c>AddOpenSearch</c> anywhere silently mapped broken routes; now it throws the diagnostic the
    /// message always promised.
    /// </summary>
    [Fact]
    public void MapOpenSearch_WithOptionsInfrastructureButWithoutAddOpenSearch_Throws()
    {
        var provider = new ServiceCollection()
            .AddOptions()
            .AddLogging()
            .AddRouting()
            .Configure<OpenSearchOptions>(o => o.ShortName = "X")
            .BuildServiceProvider();
        var routes = new BareEndpointRouteBuilder(provider);

        var ex = Assert.Throws<InvalidOperationException>(() => routes.MapOpenSearch());

        Assert.Contains("Did you forget to call AddOpenSearch?", ex.Message);
    }

    /// <summary>
    /// The measurement that makes the marker necessary: the options accessor is never null and its
    /// value is never null, so nothing about it distinguishes "configured" from "never heard of".
    /// </summary>
    [Fact]
    public void OptionsAccessor_WithoutConfigure_IsNonNullWithUnsetProperties()
    {
        var provider = new ServiceCollection().AddOptions().BuildServiceProvider();

        var accessor = provider.GetService<IOptions<OpenSearchOptions>>();

        Assert.NotNull(accessor);
        Assert.NotNull(accessor.Value);
        Assert.Null(accessor.Value.OsdxEndpoint);
        Assert.Null(accessor.Value.SearchUrl);
        Assert.Null(accessor.Value.SuggestUrl);
        Assert.Null(accessor.Value.ImageUrl);
        Assert.Null(accessor.Value.ShortName);
        Assert.Null(accessor.Value.Description);
        Assert.Null(accessor.Value.Contact);
    }

    /// <summary>
    /// <c>AddOpenSearch</c> without the options overload is legitimate: every setting has a usable
    /// default, so the three routes are mapped at their default paths.
    /// </summary>
    [Fact]
    public async Task MapOpenSearch_WithoutConfigure_MapsDefaultRoutes()
    {
        using var server = OpenSearchTestHost.Create(configure: null, service: new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var osdx = await client.GetAsync("/opensearch.xml");
        var suggest = await client.GetAsync("/suggest");
        var search = await client.GetAsync("/search");

        Assert.Equal(HttpStatusCode.OK, osdx.StatusCode);
        Assert.Equal(HttpStatusCode.OK, suggest.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, search.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task MapOpenSearch_BlankOsdxEndpoint_FallsBackToDefault(string? osdxEndpoint)
    {
        using var server = OpenSearchTestHost.Create(o => o.OsdxEndpoint = osdxEndpoint!, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        var response = await client.GetAsync("/opensearch.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// D-S17 fixed on both counts: the leading-slash check no longer throws the bare base
    /// <see cref="Exception"/> type, and it now covers every path-valued option rather than
    /// <c>OsdxEndpoint</c> alone.
    /// </summary>
    /// <remarks>
    /// The check is a plain <c>string.StartsWith('/')</c> on purpose.
    /// <c>Uri.TryCreate(value, UriKind.Absolute, out _)</c> answers <b>false</b> on Windows and
    /// <b>true</b> on Linux for a leading-slash string (it parses as <c>file:///x</c>), so a
    /// validation built on it passes locally and fails only in CI.
    /// </remarks>
    [Theory]
    [InlineData(nameof(OpenSearchOptions.OsdxEndpoint), "opensearch.xml")]
    [InlineData(nameof(OpenSearchOptions.SearchUrl), "search")]
    [InlineData(nameof(OpenSearchOptions.SuggestUrl), "suggest")]
    [InlineData(nameof(OpenSearchOptions.ImageUrl), "favicon.png")]
    public void MapOpenSearch_PathOptionWithoutLeadingSlash_ThrowsInvalidOperationException(string optionName, string value)
    {
        var routes = Routes(o => typeof(OpenSearchOptions).GetProperty(optionName)!.SetValue(o, value));

        var ex = Assert.Throws<InvalidOperationException>(() => routes.MapOpenSearch());

        Assert.Contains($"OpenSearchOptions.{optionName}", ex.Message);
        Assert.Contains(@"must start with ""/""", ex.Message);
        Assert.Contains(value, ex.Message);
    }

    [Fact]
    public void MapOpenSearch_LeadingSlashValidation_DoesNotThrowTheBareExceptionType()
    {
        var routes = Routes(o => o.OsdxEndpoint = "opensearch.xml");

        var ex = Record.Exception(() => routes.MapOpenSearch());

        Assert.NotNull(ex);
        Assert.NotEqual(typeof(Exception), ex.GetType());
    }

    /// <summary>A blank path option falls back to its default instead of failing validation.</summary>
    [Fact]
    public void MapOpenSearch_WhitespaceOnlyPathOptions_FallBackRatherThanThrow()
    {
        var routes = Routes(o =>
        {
            o.OsdxEndpoint = "   ";
            o.SearchUrl = "   ";
            o.SuggestUrl = "   ";
            o.ImageUrl = "   ";
        });

        Assert.Same(routes, routes.MapOpenSearch());
    }

    [Fact]
    public async Task MapOpenSearch_CustomEndpoints_AreMappedAtTheConfiguredPaths()
    {
        using var server = OpenSearchTestHost.Create(
            o =>
            {
                o.OsdxEndpoint = "/osdx.xml";
                o.SearchUrl = "/find";
                o.SuggestUrl = "/hints";
            },
            new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/opensearch.xml")).StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, (await client.GetAsync("/osdx.xml")).StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, (await client.GetAsync("/hints")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/find")).StatusCode);
    }

    /// <summary>Only GET is mapped; a POST to any of the three routes is a 405.</summary>
    [Fact]
    public async Task MapOpenSearch_MapsGetOnly()
    {
        using var server = OpenSearchTestHost.Create(_ => { }, new FakeOpenSearchService());
        using var client = server.CreateNonRedirectingClient();

        foreach (var path in new[] { "/opensearch.xml", "/suggest", "/search" })
        {
            var response = await client.PostAsync(path, new StringContent(string.Empty));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }

    [Fact]
    public void MapOpenSearch_ReturnsSameRouteBuilder()
    {
        var routes = Routes(_ => { });

        var returned = routes.MapOpenSearch();

        Assert.Same(routes, returned);
    }

    [Fact]
    public void MapOpenSearch_NullRoutes_ThrowsArgumentNullException()
    {
        IEndpointRouteBuilder? routes = null;

        var ex = Assert.Throws<ArgumentNullException>(() => routes!.MapOpenSearch());

        Assert.Equal("routes", ex.ParamName);
    }
}
