using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MintPlayer.AspNetCore.LoggerProviders;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Logging;

public class LoggerExtensionsTests
{
    /// <summary>
    /// A minimal <see cref="ILoggerProvider"/> so the generic <c>AddProvider&lt;T&gt;</c> overload can be
    /// tested without dragging in <see cref="LoggerFileProvider"/>'s file writing.
    /// </summary>
    private sealed class FakeLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose() { }
    }

    /// <summary>
    /// <see cref="ILoggingBuilder"/> has no public implementation, so the only way to get one is to
    /// let <c>AddLogging</c> hand it over. The callback runs synchronously inside <c>AddLogging</c>.
    /// </summary>
    private static (ServiceCollection Services, ILoggingBuilder Builder) CreateBuilder()
    {
        var services = new ServiceCollection();
        ILoggingBuilder? captured = null;
        services.AddLogging(b => captured = b);
        return (services, captured!);
    }

    private static int LoggerProviderRegistrations(IServiceCollection services) =>
        services.Count(d => d.ServiceType == typeof(ILoggerProvider));

    #region AddProvider

    [Fact]
    public void AddProvider_RegistersTheProviderAsILoggerProvider()
    {
        var (services, builder) = CreateBuilder();
        var before = LoggerProviderRegistrations(services);

        builder.AddProvider(_ => new FakeLoggerProvider());

        Assert.Equal(before + 1, LoggerProviderRegistrations(services));
    }

    [Fact]
    public void AddProvider_RegistersAsSingleton()
    {
        var (services, builder) = CreateBuilder();

        builder.AddProvider(_ => new FakeLoggerProvider());

        var descriptor = services.Last(d => d.ServiceType == typeof(ILoggerProvider));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void AddProvider_ReturnsTheSameBuilderForChaining()
    {
        var (_, builder) = CreateBuilder();

        var returned = builder.AddProvider(_ => new FakeLoggerProvider());

        Assert.Same(builder, returned);
    }

    /// <summary>
    /// The factory is the caller's hook for constructing a provider that DI cannot build on its
    /// own, so it must be the thing actually invoked — and only when the provider is resolved.
    /// </summary>
    [Fact]
    public void AddProvider_InvokesTheFactoryLazilyAndExactlyOnce()
    {
        var (services, builder) = CreateBuilder();
        var expected = new FakeLoggerProvider();
        var invocations = 0;

        builder.AddProvider(_ =>
        {
            invocations++;
            return expected;
        });

        Assert.Equal(0, invocations);

        using var sp = services.BuildServiceProvider();
        var resolvedTwice = sp.GetServices<ILoggerProvider>().OfType<FakeLoggerProvider>().ToArray();
        _ = sp.GetServices<ILoggerProvider>().OfType<FakeLoggerProvider>().ToArray();

        Assert.Same(expected, Assert.Single(resolvedTwice));
        Assert.Equal(1, invocations);
    }

    [Fact]
    public void AddProvider_FactoryReceivesTheApplicationServiceProvider()
    {
        var (services, builder) = CreateBuilder();
        var marker = new FakeLoggerProvider();
        services.AddSingleton(marker);
        IServiceProvider? handed = null;

        builder.AddProvider(sp =>
        {
            handed = sp;
            return new FakeLoggerProvider();
        });

        using var root = services.BuildServiceProvider();
        _ = root.GetServices<ILoggerProvider>().ToArray();

        Assert.NotNull(handed);
        Assert.Same(marker, handed!.GetRequiredService<FakeLoggerProvider>());
    }

    /// <summary>
    /// Pins D-M10: <c>AddProvider</c> uses <c>AddSingleton</c>, not
    /// <c>TryAddEnumerable</c>. Registering the same provider type twice — trivially easy across a
    /// library's own <c>AddX()</c> and an app's explicit call — yields two providers, and the
    /// logger factory then writes every log line twice.
    /// </summary>
    [Fact]
    public void AddProvider_CalledTwiceForTheSameType_RegistersItTwice_KnownBug()
    {
        var (services, builder) = CreateBuilder();
        var before = LoggerProviderRegistrations(services);

        builder.AddProvider(_ => new FakeLoggerProvider());
        builder.AddProvider(_ => new FakeLoggerProvider());

        Assert.Equal(before + 2, LoggerProviderRegistrations(services));

        using var sp = services.BuildServiceProvider();
        Assert.Equal(2, sp.GetServices<ILoggerProvider>().OfType<FakeLoggerProvider>().Count());
    }

    #endregion

    #region AddFileProvider

    [Fact]
    public void AddFileProvider_RegistersALoggerFileProvider()
    {
        var (services, builder) = CreateBuilder();

        builder.AddFileProvider();

        using var sp = services.BuildServiceProvider();
        Assert.Single(sp.GetServices<ILoggerProvider>().OfType<LoggerFileProvider>());
    }

    [Fact]
    public void AddFileProvider_ReturnsTheSameBuilderForChaining()
    {
        var (_, builder) = CreateBuilder();

        Assert.Same(builder, builder.AddFileProvider());
    }

    /// <summary>
    /// The provider is built with <c>ActivatorUtilities</c>, which resolves its
    /// <see cref="IServiceProvider"/> parameter from the container — so registering the extension
    /// must be sufficient, with no extra registration required from the caller.
    /// </summary>
    [Fact]
    public void AddFileProvider_NeedsNoAdditionalRegistrationsToResolve()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFileProvider());

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<ILoggerFactory>().CreateLogger("Some.Category"));
    }

    [Fact]
    public void AddFileProvider_RegistersAsSingletonSoTheProviderIsSharedAcrossCategories()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFileProvider());
        using var sp = services.BuildServiceProvider();

        var first = sp.GetServices<ILoggerProvider>().OfType<LoggerFileProvider>().Single();
        var second = sp.GetServices<ILoggerProvider>().OfType<LoggerFileProvider>().Single();

        Assert.Same(first, second);
    }

    /// <summary>
    /// Pins D-M10 on the extension the package actually documents: two <c>AddFileProvider()</c>
    /// calls give two providers. The visible consequence — duplicated lines in the log file — is
    /// asserted in <see cref="FileLoggerEndToEndTests"/>.
    /// </summary>
    [Fact]
    public void AddFileProvider_CalledTwice_RegistersTwoProviders_KnownBug()
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddFileProvider();
            b.AddFileProvider();
        });

        using var sp = services.BuildServiceProvider();

        Assert.Equal(2, sp.GetServices<ILoggerProvider>().OfType<LoggerFileProvider>().Count());
    }

    #endregion
}
