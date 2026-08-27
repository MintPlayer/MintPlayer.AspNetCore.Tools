using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.SitemapXml;
using MintPlayer.AspNetCore.SitemapXml.Abstractions;
using MintPlayer.AspNetCore.SitemapXml.Options;
using Xunit;
using SitemapFormatter = MintPlayer.AspNetCore.SitemapXml.Formatters.XmlSerializerOutputFormatter;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

public class AddSitemapXmlTests
{
    private static MvcOptions ResolveMvcOptions(IServiceCollection services)
        => services.BuildServiceProvider().GetRequiredService<IOptions<MvcOptions>>().Value;

    [Fact]
    public void AddSitemapXml_RegistersTheSitemapXmlServiceAsScoped()
    {
        var services = new ServiceCollection();

        services.AddSitemapXml();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ISitemapXml));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AddSitemapXml_ResolvesTheSitemapXmlService()
    {
        var provider = new ServiceCollection().AddSitemapXml().BuildServiceProvider();

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISitemapXml>());
    }

    [Fact]
    public void AddSitemapXml_ReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddSitemapXml());
    }

    /// <summary>
    /// The formatter is inserted at index 0 rather than appended, which is what lets it win the
    /// <c>application/xml</c> negotiation against the built-in XML formatters.
    /// </summary>
    [Fact]
    public void AddSitemapXml_InsertsTheFormatterAtTheFrontOfTheOutputFormatters()
    {
        var options = ResolveMvcOptions(new ServiceCollection().AddSitemapXml());

        Assert.IsType<SitemapFormatter>(options.OutputFormatters[0]);
    }

    /// <summary>
    /// Without this, MVC ignores the browser's <c>Accept</c> header and a sitemap request would be
    /// answered with whatever formatter came first overall.
    /// </summary>
    [Fact]
    public void AddSitemapXml_TurnsOnRespectBrowserAcceptHeader()
    {
        Assert.True(ResolveMvcOptions(new ServiceCollection().AddSitemapXml()).RespectBrowserAcceptHeader);
    }

    /// <summary>
    /// Pins PRD defect D-S10: there is no idempotence guard, so a second <c>AddSitemapXml()</c> —
    /// two libraries each doing their own setup, say — inserts a second formatter instance. Both
    /// then run the same negotiation and the response is written by whichever landed at index 0.
    /// </summary>
    [Fact]
    public void AddSitemapXml_CalledTwice_InsertsTheFormatterTwice_KnownBug()
    {
        var services = new ServiceCollection();

        services.AddSitemapXml();
        services.AddSitemapXml();

        var options = ResolveMvcOptions(services);
        Assert.Equal(2, options.OutputFormatters.OfType<SitemapFormatter>().Count());
    }

    /// <summary>
    /// The service registration half of D-S10. The generated registration is additive too, so the
    /// last one wins on resolve while both stay in the collection.
    /// </summary>
    [Fact]
    public void AddSitemapXml_CalledTwice_RegistersTheServiceTwice_KnownBug()
    {
        var services = new ServiceCollection();

        services.AddSitemapXml();
        services.AddSitemapXml();

        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(ISitemapXml)));
    }

    // ── the configuring overload ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddSitemapXml_WithOptions_ConfiguresTheStylesheetUrl()
    {
        var provider = new ServiceCollection()
            .AddSitemapXml(options => options.StylesheetUrl = "/custom.xsl")
            .BuildServiceProvider();

        Assert.Equal("/custom.xsl", provider.GetRequiredService<IOptions<SitemapXmlOptions>>().Value.StylesheetUrl);
    }

    /// <summary>The configuring overload must do everything the bare one does.</summary>
    [Fact]
    public void AddSitemapXml_WithOptions_AlsoRegistersTheServiceAndTheFormatter()
    {
        var services = new ServiceCollection().AddSitemapXml(options => options.StylesheetUrl = "/custom.xsl");

        Assert.Contains(services, d => d.ServiceType == typeof(ISitemapXml));
        Assert.IsType<SitemapFormatter>(ResolveMvcOptions(services).OutputFormatters[0]);
    }

    [Fact]
    public void AddSitemapXml_WithoutOptions_LeavesTheStylesheetUrlNull()
    {
        var provider = new ServiceCollection().AddSitemapXml().BuildServiceProvider();

        Assert.Null(provider.GetRequiredService<IOptions<SitemapXmlOptions>>().Value.StylesheetUrl);
    }

    [Fact]
    public void AddSitemapXml_WithANullOptionsAction_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddSitemapXml(null!));
    }

    /// <summary>
    /// The options action runs lazily, at first resolve of <c>IOptions{SitemapXmlOptions}</c> —
    /// so a configuration mistake surfaces on the first sitemap request, not at startup.
    /// </summary>
    [Fact]
    public void AddSitemapXml_OptionsActionIsNotInvokedDuringRegistration()
    {
        var invoked = false;

        var services = new ServiceCollection().AddSitemapXml(options => invoked = true);

        Assert.False(invoked);

        // Later still than it looks: resolving IOptions<T> is not enough, the action runs on the
        // first read of .Value.
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<SitemapXmlOptions>>();
        Assert.False(invoked);

        _ = options.Value;
        Assert.True(invoked);
    }

    /// <summary>
    /// <c>AddSitemapXml</c> calls <c>AddControllersWithViews()</c> itself, so an app that never
    /// registers MVC still gets a working formatter pipeline — this is the "pull complexity
    /// downwards" behaviour consumers depend on, and dropping the call would break them silently.
    /// </summary>
    [Fact]
    public void AddSitemapXml_BringsInMvcOnItsOwn()
    {
        var services = new ServiceCollection().AddSitemapXml();

        Assert.Contains(services, d => d.ServiceType.FullName == "Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider");
    }
}
