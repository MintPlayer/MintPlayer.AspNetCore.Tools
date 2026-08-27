using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MintPlayer.AspNetCore.LoggerProviders;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Logging;

/// <summary>
/// Exercises the package the way a host does: <c>AddLogging(b =&gt; b.AddFileProvider())</c>, a
/// configured filename, and an injected <see cref="ILogger{T}"/>.
/// </summary>
/// <remarks>
/// This library is the sink, so there is no fake logger to substitute — these tests write real
/// files into a per-test temp directory and read them back.
/// </remarks>
public class FileLoggerEndToEndTests
{
    private static ServiceProvider BuildHost(string path, Action<ILoggingBuilder>? configureLogging = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddFileProvider();
            configureLogging?.Invoke(b);
        });
        services.Configure<FileLoggerOptions>(o => o.FileName = path);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void InjectedLogger_WritesToTheConfiguredFile()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path);

        sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>().LogInformation("{Message}", "hello");

        Assert.Equal(new[] { "hello", "" }, File.ReadAllLines(path));
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

        Assert.Equal(new[] { "first", "second" }, File.ReadAllLines(path).Where(l => l.Length > 0));
    }

    /// <summary>
    /// The category is what an injected <see cref="ILogger{T}"/> contributes over a bare
    /// <see cref="ILogger"/>, and it never reaches the file — the end-to-end consequence of D-M8
    /// and D-M17 together.
    /// </summary>
    [Fact]
    public void InjectedLogger_CategoryNeverReachesTheFile_KnownGap()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path);

        sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>().LogInformation("{Message}", "hello");

        Assert.DoesNotContain(nameof(FileLoggerEndToEndTests), File.ReadAllText(path));
    }

    /// <summary>
    /// Level filtering still works, because <c>ILoggerFactory</c> applies its own filters before
    /// ever reaching the provider — which is the only reason D-M12's always-true <c>IsEnabled</c>
    /// is not catastrophic in a real host.
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
    /// Pins the user-visible consequence of D-M10: because <c>AddProvider</c> uses
    /// <c>AddSingleton</c> instead of <c>TryAddEnumerable</c>, calling <c>AddFileProvider()</c>
    /// twice registers two providers and the factory fans every log call out to both — so every
    /// line lands in the file twice, with no error to explain it.
    /// </summary>
    [Fact]
    public void AddFileProviderTwice_WritesEveryLineTwice_KnownBug()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path, b => b.AddFileProvider());

        sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>().LogInformation("{Message}", "once");

        Assert.Equal(new[] { "once", "once" }, File.ReadAllLines(path).Where(l => l.Length > 0));
    }

    /// <summary>
    /// Pins D-M7 end to end: the stack trace a host most needs is exactly what is lost.
    /// </summary>
    [Fact]
    public void InjectedLogger_LogErrorWithException_LosesTheStackTrace_KnownBug()
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
        Assert.DoesNotContain("the-real-cause", content);
        Assert.DoesNotContain(nameof(InjectedLogger_LogErrorWithException_LosesTheStackTrace_KnownBug), content);
    }

    /// <summary>
    /// A disallowed extension surfaces out of the caller's own <c>LogInformation</c> call — i.e. a
    /// logging misconfiguration takes down application code, which is the ergonomic cost of
    /// validating on the write path (D-M15) rather than at registration.
    /// </summary>
    /// <remarks>
    /// The factory wraps provider failures in an <see cref="AggregateException"/> ("An error
    /// occurred while writing to logger(s)"), so the real cause is one level down. Asserted on the
    /// inner exception because that wrapping is the factory's contract, not this package's.
    /// </remarks>
    [Fact]
    public void MisconfiguredFileName_ThrowsFromTheCallersLogCall_KnownGap()
    {
        using var temp = new TempDirectoryFixture();
        using var sp = BuildHost(temp.GetPath("Log.json"));
        var logger = sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>();

        var aggregate = Assert.Throws<AggregateException>(() => logger.LogInformation("{Message}", "hello"));

        var inner = Assert.IsType<InvalidOperationException>(Assert.Single(aggregate.InnerExceptions));
        Assert.Contains(".json", inner.Message);
    }

    /// <summary>
    /// Scopes are enabled by default on the factory, so the host really does create them — and
    /// the provider drops them (D-M13).
    /// </summary>
    [Fact]
    public void LoggerScopes_AreCreatedByTheHostAndDroppedByTheProvider_KnownGap()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildHost(path);
        var logger = sp.GetRequiredService<ILogger<FileLoggerEndToEndTests>>();

        using (logger.BeginScope("RequestId:{RequestId}", "abc"))
        {
            logger.LogInformation("{Message}", "inside");
        }

        Assert.DoesNotContain("abc", File.ReadAllText(path));
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
}
