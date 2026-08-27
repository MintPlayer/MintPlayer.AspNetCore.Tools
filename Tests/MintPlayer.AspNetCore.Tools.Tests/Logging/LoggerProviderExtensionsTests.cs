using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.LoggerProviders;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Logging;

public class LoggerProviderExtensionsTests
{
    /// <summary>
    /// A minimal <see cref="ILoggerProvider"/> so the generic <c>TryAddProvider&lt;T&gt;</c> overload can
    /// be tested without dragging in <see cref="LoggerFileProvider"/>'s file writing.
    /// </summary>
    private sealed class FakeLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose() { }
    }

    /// <summary>A second type, to show the deduplication is per provider type and not global.</summary>
    private sealed class OtherFakeLoggerProvider : ILoggerProvider
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

    #region TryAddProvider

    [Fact]
    public void TryAddProvider_RegistersTheProviderAsILoggerProvider()
    {
        var (services, builder) = CreateBuilder();
        var before = LoggerProviderRegistrations(services);

        builder.TryAddProvider(_ => new FakeLoggerProvider());

        Assert.Equal(before + 1, LoggerProviderRegistrations(services));
    }

    [Fact]
    public void TryAddProvider_RegistersAsSingleton()
    {
        var (services, builder) = CreateBuilder();

        builder.TryAddProvider(_ => new FakeLoggerProvider());

        var descriptor = services.Last(d => d.ServiceType == typeof(ILoggerProvider));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void TryAddProvider_ReturnsTheSameBuilderForChaining()
    {
        var (_, builder) = CreateBuilder();

        var returned = builder.TryAddProvider(_ => new FakeLoggerProvider());

        Assert.Same(builder, returned);
    }

    /// <summary>
    /// The factory is the caller's hook for constructing a provider that DI cannot build on its
    /// own, so it must be the thing actually invoked — and only when the provider is resolved.
    /// </summary>
    [Fact]
    public void TryAddProvider_InvokesTheFactoryLazilyAndExactlyOnce()
    {
        var (services, builder) = CreateBuilder();
        var expected = new FakeLoggerProvider();
        var invocations = 0;

        builder.TryAddProvider(_ =>
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
    public void TryAddProvider_FactoryReceivesTheApplicationServiceProvider()
    {
        var (services, builder) = CreateBuilder();
        var marker = new FakeLoggerProvider();
        services.AddSingleton(marker);
        IServiceProvider? handed = null;

        builder.TryAddProvider(sp =>
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
    /// D-M10: registration deduplicates on the provider type. Registering the same provider twice —
    /// trivially easy across a library's own <c>AddX()</c> and an app's explicit call — used to
    /// yield two providers, and the factory then wrote every log line twice with nothing to explain
    /// it.
    /// </summary>
    [Fact]
    public void TryAddProvider_CalledTwiceForTheSameType_RegistersItOnce()
    {
        var (services, builder) = CreateBuilder();
        var before = LoggerProviderRegistrations(services);

        builder.TryAddProvider(_ => new FakeLoggerProvider());
        builder.TryAddProvider(_ => new FakeLoggerProvider());

        Assert.Equal(before + 1, LoggerProviderRegistrations(services));

        using var sp = services.BuildServiceProvider();
        Assert.Single(sp.GetServices<ILoggerProvider>().OfType<FakeLoggerProvider>());
    }

    /// <summary>Deduplication is per provider type, so unrelated providers still both register.</summary>
    [Fact]
    public void TryAddProvider_DifferentTypes_BothRegister()
    {
        var (services, builder) = CreateBuilder();

        builder.TryAddProvider(_ => new FakeLoggerProvider());
        builder.TryAddProvider(_ => new OtherFakeLoggerProvider());

        using var sp = services.BuildServiceProvider();
        Assert.Single(sp.GetServices<ILoggerProvider>().OfType<FakeLoggerProvider>());
        Assert.Single(sp.GetServices<ILoggerProvider>().OfType<OtherFakeLoggerProvider>());
    }

    [Fact]
    public void TryAddProvider_NullArguments_ThrowArgumentNullException()
    {
        var (_, builder) = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.TryAddProvider<FakeLoggerProvider>(null!));
        Assert.Throws<ArgumentNullException>(() => ((ILoggingBuilder)null!).TryAddProvider(_ => new FakeLoggerProvider()));
    }

    /// <summary>
    /// D-M10's second half: the old name collided with the framework's own
    /// <c>ILoggingBuilder.AddProvider(ILoggerProvider)</c>. The renamed method must not reintroduce
    /// that, and the framework's own overload must still be reachable.
    /// </summary>
    [Fact]
    public void Package_DoesNotDeclareAnAddProviderExtension()
    {
        var names = typeof(LoggerProviderExtensions).GetMethods().Select(m => m.Name).ToArray();

        Assert.DoesNotContain("AddProvider", names);
        Assert.Contains(nameof(LoggerProviderExtensions.TryAddProvider), names);

        var (services, builder) = CreateBuilder();
        builder.AddProvider(new FakeLoggerProvider()); // the framework's instance overload
        Assert.Single(services, d => d.ServiceType == typeof(ILoggerProvider) && d.ImplementationInstance is FakeLoggerProvider);
    }

    #endregion

    #region AddFileProvider

    [Fact]
    public void AddFileProvider_RegistersALoggerFileProvider()
    {
        using var temp = new TempDirectoryFixture();
        var (services, builder) = CreateBuilder();

        builder.AddFileProvider(o => o.FileName = temp.GetPath("Log.txt"));

        using var sp = services.BuildServiceProvider();
        Assert.Single(sp.GetServices<ILoggerProvider>().OfType<LoggerFileProvider>());
    }

    [Fact]
    public void AddFileProvider_ReturnsTheSameBuilderForChaining()
    {
        var (_, builder) = CreateBuilder();

        Assert.Same(builder, builder.AddFileProvider());
        Assert.Same(builder, builder.AddFileProvider(o => o.FileName = "Log.txt"));
    }

    /// <summary>
    /// The provider is built with <c>ActivatorUtilities</c>, which resolves its
    /// <see cref="IServiceProvider"/> parameter from the container — so registering the extension
    /// must be sufficient, with no extra registration required from the caller beyond the filename.
    /// </summary>
    [Fact]
    public void AddFileProvider_NeedsNoAdditionalRegistrationsToResolve()
    {
        using var temp = new TempDirectoryFixture();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFileProvider(o => o.FileName = temp.GetPath("Log.txt")));

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<ILoggerFactory>().CreateLogger("Some.Category"));
    }

    [Fact]
    public void AddFileProvider_RegistersAsSingletonSoTheProviderIsSharedAcrossCategories()
    {
        using var temp = new TempDirectoryFixture();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFileProvider(o => o.FileName = temp.GetPath("Log.txt")));
        using var sp = services.BuildServiceProvider();

        var first = sp.GetServices<ILoggerProvider>().OfType<LoggerFileProvider>().Single();
        var second = sp.GetServices<ILoggerProvider>().OfType<LoggerFileProvider>().Single();

        Assert.Same(first, second);
    }

    /// <summary>
    /// D-M10 on the extension the package actually documents: two <c>AddFileProvider()</c> calls
    /// give one provider. The visible consequence — lines no longer duplicated in the log file — is
    /// asserted in <see cref="FileLoggerEndToEndTests"/>.
    /// </summary>
    [Fact]
    public void AddFileProvider_CalledTwice_RegistersOneProvider()
    {
        using var temp = new TempDirectoryFixture();
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddFileProvider(o => o.FileName = temp.GetPath("Log.txt"));
            b.AddFileProvider();
        });

        using var sp = services.BuildServiceProvider();

        Assert.Single(sp.GetServices<ILoggerProvider>().OfType<LoggerFileProvider>());
    }

    /// <summary>
    /// D-M16: the <c>Action&lt;FileLoggerOptions&gt;</c> overload, so a consumer configures the
    /// filename where they register the provider — as every other <c>Add*</c> in the ecosystem
    /// allows — rather than reaching for <c>services.Configure</c> separately.
    /// </summary>
    [Fact]
    public void AddFileProvider_WithConfigureCallback_ConfiguresTheOptions()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Configured.log");
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddFileProvider(o =>
        {
            o.FileName = path;
            o.MinimumLevel = LogLevel.Warning;
            o.IncludeScopes = false;
        }));

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<FileLoggerOptions>>().Value;
        Assert.Equal(path, options.FileName);
        Assert.Equal(LogLevel.Warning, options.MinimumLevel);
        Assert.False(options.IncludeScopes);
    }

    /// <summary>The configure overload registers the provider too, not just the options.</summary>
    [Fact]
    public void AddFileProvider_WithConfigureCallback_AlsoRegistersTheProvider()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFileProvider(o => o.FileName = path));

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("Some.Category").LogInformation("{Message}", "hello");

        Assert.Contains("hello", File.ReadAllText(path));
    }

    [Fact]
    public void AddFileProvider_NullArguments_ThrowArgumentNullException()
    {
        var (_, builder) = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => ((ILoggingBuilder)null!).AddFileProvider());
        Assert.Throws<ArgumentNullException>(() => builder.AddFileProvider(null!));
    }

    /// <summary>
    /// The options validator is registered once, however often <c>AddFileProvider</c> is called, so
    /// a bad filename is reported once rather than N times.
    /// </summary>
    [Fact]
    public void AddFileProvider_RegistersTheOptionsValidatorExactlyOnce()
    {
        var (services, builder) = CreateBuilder();

        builder.AddFileProvider();
        builder.AddFileProvider();

        Assert.Single(services, d => d.ServiceType == typeof(IValidateOptions<FileLoggerOptions>));
    }

    #endregion
}
