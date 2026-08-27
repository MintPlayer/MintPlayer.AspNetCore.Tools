using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.OpenSearch;
using MintPlayer.AspNetCore.OpenSearch.Abstractions;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

public class AddOpenSearchTests
{
    private static MvcOptions ResolveMvcOptions(IServiceCollection services)
        => services.BuildServiceProvider().GetRequiredService<IOptions<MvcOptions>>().Value;

    [Fact]
    public void AddOpenSearch_RegistersServiceAsScoped()
    {
        var services = new ServiceCollection();

        services.AddOpenSearch<FakeOpenSearchService>();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IOpenSearchService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(FakeOpenSearchService), descriptor.ImplementationType);
    }

    [Fact]
    public void AddOpenSearch_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var returned = services.AddOpenSearch<FakeOpenSearchService>();

        Assert.Same(services, returned);
    }

    [Fact]
    public void AddOpenSearch_SetsRespectBrowserAcceptHeader()
    {
        var services = new ServiceCollection();

        services.AddOpenSearch<FakeOpenSearchService>();

        Assert.True(ResolveMvcOptions(services).RespectBrowserAcceptHeader);
    }

    /// <summary>
    /// The formatter must sit at index 0 so it wins negotiation for the OSDX media type ahead of
    /// the framework's own XML formatters, which would otherwise serialize the description with
    /// different namespace handling.
    /// </summary>
    [Fact]
    public void AddOpenSearch_InsertsFormatterAtIndexZero()
    {
        var services = new ServiceCollection();

        services.AddOpenSearch<FakeOpenSearchService>();

        var formatters = ResolveMvcOptions(services).OutputFormatters;
        Assert.IsType<MintPlayer.AspNetCore.OpenSearch.Formatters.XmlSerializerOutputFormatter>(formatters[0]);
    }

    [Fact]
    public void AddOpenSearch_CalledTwice_RegistersFormatterOnlyOnce()
    {
        var services = new ServiceCollection();

        services.AddOpenSearch<FakeOpenSearchService>();
        services.AddOpenSearch<FakeOpenSearchService>();

        var formatters = ResolveMvcOptions(services).OutputFormatters;
        Assert.Single(formatters.OfType<MintPlayer.AspNetCore.OpenSearch.Formatters.XmlSerializerOutputFormatter>());
    }

    /// <summary>
    /// Two different service implementations both register against <c>IOpenSearchService</c>; the
    /// last one wins for <c>GetRequiredService</c>, which is how the test host overrides it.
    /// </summary>
    [Fact]
    public void AddOpenSearch_CalledTwiceWithDifferentServices_KeepsBothDescriptors()
    {
        var services = new ServiceCollection();

        services.AddOpenSearch<FakeOpenSearchService>();
        services.AddOpenSearch<SecondService>();

        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IOpenSearchService)));
        Assert.IsType<SecondService>(services.BuildServiceProvider().GetRequiredService<IOpenSearchService>());
    }

    private sealed class SecondService : IOpenSearchService
    {
        public Task<IEnumerable<string>> ProvideSuggestions(string? searchTerms) => Task.FromResult(Enumerable.Empty<string>());
        public Task<OpenSearchRedirect> PerformSearch(string? searchTerms) => Task.FromResult(new OpenSearchRedirect("/"));
    }

    [Fact]
    public void AddOpenSearch_RegistersMvcInfrastructure()
    {
        var services = new ServiceCollection();

        services.AddOpenSearch<FakeOpenSearchService>();

        // The OSDX endpoint writes through ObjectResult, so this executor must be resolvable.
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<Microsoft.AspNetCore.Mvc.Infrastructure.IActionResultExecutor<ObjectResult>>());
    }

    [Fact]
    public void AddOpenSearch_WithOptions_AppliesTheConfiguration()
    {
        var services = new ServiceCollection();

        services.AddOpenSearch<FakeOpenSearchService>(options =>
        {
            options.OsdxEndpoint = "/osdx.xml";
            options.ShortName = "Test";
            options.Contact = "a@b.c";
        });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<OpenSearchOptions>>().Value;
        Assert.Equal("/osdx.xml", options.OsdxEndpoint);
        Assert.Equal("Test", options.ShortName);
        Assert.Equal("a@b.c", options.Contact);
    }

    [Fact]
    public void AddOpenSearch_WithOptions_AlsoRegistersServiceAndFormatter()
    {
        var services = new ServiceCollection();

        services.AddOpenSearch<FakeOpenSearchService>(_ => { });

        Assert.Contains(services, d => d.ServiceType == typeof(IOpenSearchService));
        Assert.IsType<MintPlayer.AspNetCore.OpenSearch.Formatters.XmlSerializerOutputFormatter>(
            ResolveMvcOptions(services).OutputFormatters[0]);
    }

    /// <summary>
    /// D-S26 fixed: the formatter type used to be held in a <c>static Lazy&lt;Type&gt;</c> whose
    /// factory was a <c>typeof</c> expression — a compile-time constant. The laziness bought nothing
    /// and the <c>Lazy</c> added a lock and an allocation to a value the JIT already has.
    /// </summary>
    [Fact]
    public void OpenSearchExtensions_HoldsNoLazyOverACompileTimeConstant()
    {
        var lazyFields = typeof(OpenSearchExtensions)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(Lazy<>))
            .ToArray();

        Assert.Empty(lazyFields);
        Assert.Null(typeof(OpenSearchExtensions).GetField("formatterType", BindingFlags.NonPublic | BindingFlags.Static));
    }

    /// <summary>
    /// D-S16 fixed: <c>AddOpenSearch</c> registers a marker service, which is the only thing that
    /// can prove it ran — <c>IOptions&lt;OpenSearchOptions&gt;</c> resolves to a default instance
    /// whether or not anybody configured it.
    /// </summary>
    [Fact]
    public void AddOpenSearch_RegistersAMarkerService()
    {
        var services = new ServiceCollection();

        services.AddOpenSearch<FakeOpenSearchService>();

        var markerType = typeof(OpenSearchExtensions).Assembly.GetType("MintPlayer.AspNetCore.OpenSearch.OpenSearchMarker");
        Assert.NotNull(markerType);
        Assert.NotNull(services.BuildServiceProvider().GetService(markerType));
    }

    [Fact]
    public void AddOpenSearch_CalledTwice_RegistersTheMarkerOnlyOnce()
    {
        var services = new ServiceCollection();

        services.AddOpenSearch<FakeOpenSearchService>();
        services.AddOpenSearch<FakeOpenSearchService>();

        var markerType = typeof(OpenSearchExtensions).Assembly.GetType("MintPlayer.AspNetCore.OpenSearch.OpenSearchMarker")!;
        Assert.Single(services, d => d.ServiceType == markerType);
    }
}
