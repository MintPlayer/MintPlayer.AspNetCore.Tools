using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.LoggerProviders;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Logging;

/// <summary>
/// Exercises the package the way a host does: <c>AddLogging(b =&gt; b.AddFileProvider(...))</c> and an
/// injected <see cref="ILogger{T}"/>.
/// </summary>
/// <remarks>
/// This library is the sink, so there is no fake logger to substitute — these tests write real
/// files into a per-test temp directory and read them back.
/// </remarks>
public class FileLoggerEndToEndTests
{
    /// <summary>The category an injected <c>ILogger&lt;FileLoggerEndToEndTests&gt;</c> logs under.</summary>
    private static readonly string Category = typeof(FileLoggerEndToEndTests).FullName!;

    private static ServiceProvider BuildHost(string path, Action<ILoggingBuilder>? configureLogging = null, TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddFileProvider(o => o.FileName = path);
            configureLogging?.Invoke(b);
        });
        if (clock is not null) services.AddSingleton(clock);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void InjectedLogger_WritesToTheConfiguredFile()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path, clock: new FixedTimeProvider());

        sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>().LogInformation("{Message}", "hello");

        Assert.Equal(
            new[] { $"{FixedTimeProvider.DefaultNowUtcRoundTrip} [Information] {Category} hello", "" },
            File.ReadAllLines(path));
    }

    [Fact]
    public void InjectedLogger_MultipleEntries_AreAppendedInOrder()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.log");
        using var sp = BuildHost(path);
        var logger = sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>();

        logger.LogWarning("{Message}", "first");
        logger.LogError("{Message}", "second");

        var entries = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Contains("[Warning]", entries[0]);
        Assert.EndsWith("first", entries[0]);
        Assert.Contains("[Error]", entries[1]);
        Assert.EndsWith("second", entries[1]);
    }

    /// <summary>
    /// The category is what an injected <see cref="ILogger{T}"/> contributes over a bare
    /// <see cref="ILogger"/>, and it now reaches the file — D-M8 and D-M17 together, end to end.
    /// </summary>
    [Fact]
    public void InjectedLogger_CategoryReachesTheFile()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path);

        sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>().LogInformation("{Message}", "hello");

        Assert.Contains(Category, File.ReadAllText(path));
    }

    /// <summary>
    /// Level filtering above the provider still works: <c>ILoggerFactory</c> applies its own filters
    /// before the provider is ever reached.
    /// </summary>
    [Fact]
    public void MinimumLevelFilter_IsAppliedByTheFactoryBeforeTheProviderIsReached()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path, b => b.SetMinimumLevel(LogLevel.Warning));
        var logger = sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>();

        logger.LogInformation("{Message}", "suppressed");
        logger.LogWarning("{Message}", "kept");

        var content = File.ReadAllText(path);
        Assert.DoesNotContain("suppressed", content);
        Assert.Contains("kept", content);
    }

    /// <summary>
    /// A provider-scoped filter also works, again because it is enforced above the provider.
    /// </summary>
    [Fact]
    public void ProviderSpecificFilter_SuppressesEntriesForThisProviderOnly()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path, b => b.AddFilter<LoggerFileProvider>(null, LogLevel.Error));
        var logger = sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>();

        logger.LogWarning("{Message}", "suppressed");
        logger.LogError("{Message}", "kept");

        var content = File.ReadAllText(path);
        Assert.DoesNotContain("suppressed", content);
        Assert.Contains("kept", content);
    }

    /// <summary>
    /// D-M12 end to end: the provider filters on its own too, so <c>MinimumLevel</c> works in a host
    /// that configures no <c>ILoggingBuilder</c> filter at all.
    /// </summary>
    [Fact]
    public void ProviderMinimumLevel_FiltersWithoutAnyLoggingBuilderFilter()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFileProvider(o =>
        {
            o.FileName = path;
            o.MinimumLevel = LogLevel.Error;
        }));
        using var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>();

        logger.LogWarning("{Message}", "suppressed");
        logger.LogError("{Message}", "kept");

        var content = File.ReadAllText(path);
        Assert.DoesNotContain("suppressed", content);
        Assert.Contains("kept", content);
    }

    /// <summary>
    /// D-M10: <c>AddFileProvider()</c> twice registers one provider, so a line is written once. It
    /// used to be written twice, with no error to explain it.
    /// </summary>
    [Fact]
    public void AddFileProviderTwice_WritesEveryLineOnce()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path, b => b.AddFileProvider());

        sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>().LogInformation("{Message}", "once");

        Assert.Single(File.ReadAllLines(path), l => l.Length > 0);
    }

    /// <summary>
    /// D-M7 end to end: the stack trace a host most needs is exactly what a file logger must keep.
    /// </summary>
    [Fact]
    public void InjectedLogger_LogErrorWithException_WritesTheStackTrace()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path);

        Exception captured;
        try
        {
            throw new InvalidOperationException("the-real-cause");
        }
        catch (InvalidOperationException ex)
        {
            captured = ex;
        }

        sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>().LogError(captured, "request failed");

        var content = File.ReadAllText(path);
        Assert.Contains("request failed", content);
        Assert.Contains("the-real-cause", content);
        Assert.Contains(nameof(InjectedLogger_LogErrorWithException_WritesTheStackTrace), content);
    }

    /// <summary>
    /// D-M15/D-M47: a disallowed extension is reported by the options system while logging is being
    /// wired up — i.e. when the first logger is resolved — instead of surfacing out of some
    /// unrelated application code's <c>LogInformation</c> call as an
    /// <see cref="AggregateException"/> much later.
    /// </summary>
    [Fact]
    public void MisconfiguredFileName_ThrowsWhenLoggingIsResolved()
    {
        using var temp = new TempDirectoryFixture();
        using var sp = BuildHost(temp.GetPath("Log.json"));

        var ex = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>());

        Assert.Contains(".json", ex.Message);
    }

    /// <summary>
    /// D-M46/D-M47: a host that forgets to configure a filename is told so, rather than getting a
    /// <c>Log.txt</c> in whatever the process's current directory happens to be.
    /// </summary>
    [Fact]
    public void UnconfiguredFileName_ThrowsWhenLoggingIsResolved()
    {
        var fallbackThatMustNotAppear = Path.Combine(Directory.GetCurrentDirectory(), "Log.txt");
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFileProvider());
        using var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>());

        Assert.Contains(nameof(FileLoggerOptions.FileName), ex.Message);
        Assert.False(File.Exists(fallbackThatMustNotAppear));
    }

    /// <summary>
    /// D-M13 end to end: the host creates scopes, and the provider now records them — through
    /// <c>ISupportExternalScope</c>, so it is the factory's shared scope stack that is written.
    /// </summary>
    [Fact]
    public void LoggerScopes_AreWrittenToTheFile()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path);
        var logger = sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>();

        using (logger.BeginScope("RequestId:{RequestId}", "abc"))
        {
            logger.LogInformation("{Message}", "inside");
        }
        logger.LogInformation("{Message}", "outside");

        var entries = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        Assert.Contains("=> RequestId:abc", entries[0]);
        Assert.DoesNotContain("RequestId:abc", entries[1]);
    }

    /// <summary>
    /// A scope created on a logger of one category is visible to a logger of another, because the
    /// factory's scope stack is shared — the point of implementing <c>ISupportExternalScope</c>
    /// rather than keeping a per-logger stack.
    /// </summary>
    [Fact]
    public void LoggerScopes_AreSharedAcrossCategories()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path);
        var factory = sp.GetRequiredService<ILoggerFactory>();

        using (factory.CreateLogger("Category.One").BeginScope("RequestId:abc"))
        {
            factory.CreateLogger("Category.Two").LogInformation("{Message}", "inside");
        }

        Assert.Contains("Category.Two => RequestId:abc inside", File.ReadAllText(path));
    }

    [Fact]
    public void ScopesDisabled_LeavesScopesOutOfTheFile()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFileProvider(o =>
        {
            o.FileName = path;
            o.IncludeScopes = false;
        }));
        using var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>();

        using (logger.BeginScope("RequestId:abc"))
        {
            logger.LogInformation("{Message}", "inside");
        }

        var content = File.ReadAllText(path);
        Assert.Contains("inside", content);
        Assert.DoesNotContain("RequestId:abc", content);
    }

    [Fact]
    public void Dispose_OfTheHost_DisposesTheProviderWithoutThrowing()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var sp = BuildHost(path);
        sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>().LogInformation("{Message}", "hello");

        sp.Dispose();

        Assert.Contains("hello", File.ReadAllText(path));
    }

    /// <summary>
    /// D-M14 through the real host: many threads logging through injected loggers of different
    /// categories all land in the file.
    /// </summary>
    [Fact]
    public void ConcurrentLoggingThroughTheHost_WritesEveryLine()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path);
        var factory = sp.GetRequiredService<ILoggerFactory>();
        const int iterations = 200;

        Parallel.For(0, iterations, i => factory.CreateLogger($"Category.{i % 8}").LogInformation("{Message}", $"line-{i}"));

        Assert.Equal(iterations, File.ReadAllLines(path).Count(l => l.Length > 0));
    }
}
