using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.LoggerProviders;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Logging;

public class LoggerFileProviderTests
{
    /// <summary>
    /// The provider resolves its <see cref="FileLogger"/> through <c>ActivatorUtilities</c>, so it
    /// needs a container that can supply <see cref="IOptions{T}"/> — not a stub
    /// <see cref="IServiceProvider"/>.
    /// </summary>
    private static ServiceProvider BuildContainer(string? fileName = null)
    {
        var services = new ServiceCollection();
        services.AddOptions<FileLoggerOptions>().Configure(o => o.FileName = fileName!);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void CreateLogger_ReturnsAFileLogger()
    {
        using var sp = BuildContainer();
        var provider = new LoggerFileProvider(sp);

        var logger = provider.CreateLogger("Some.Category");

        Assert.IsType<FileLogger>(logger);
    }

    [Fact]
    public void CreateLogger_ReturnedLoggerUsesTheConfiguredFileName()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildContainer(path);
        var provider = new LoggerFileProvider(sp);

        provider.CreateLogger("Some.Category").LogInformation("{Message}", "hello");

        Assert.Contains("hello", File.ReadAllText(path));
    }

    /// <summary>
    /// Pins the caching half of D-M17: every <c>CreateLogger</c> call builds a brand-new
    /// <see cref="FileLogger"/> rather than returning a cached instance per category, so a host
    /// with many categories allocates one logger per category per provider for no benefit.
    /// </summary>
    [Fact]
    public void CreateLogger_SameCategoryTwice_ReturnsDistinctInstances_KnownGap()
    {
        using var sp = BuildContainer();
        var provider = new LoggerFileProvider(sp);

        var first = provider.CreateLogger("Some.Category");
        var second = provider.CreateLogger("Some.Category");

        Assert.NotSame(first, second);
    }

    /// <summary>
    /// Pins the category half of D-M17: <c>categoryName</c> is accepted and discarded. Two loggers
    /// created for completely different categories are indistinguishable, and — combined with
    /// D-M8 — nothing in the log file records which category an entry came from.
    /// </summary>
    [Fact]
    public void CreateLogger_IgnoresCategoryName_KnownGap()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildContainer(path);
        var provider = new LoggerFileProvider(sp);

        provider.CreateLogger("Category.One").LogInformation("{Message}", "from-one");
        provider.CreateLogger("Category.Two").LogInformation("{Message}", "from-two");

        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        Assert.Equal(new[] { "from-one", "from-two" }, lines);
        Assert.DoesNotContain("Category.One", File.ReadAllText(path));
    }

    /// <summary>An empty or whitespace category is accepted, since the value is never used.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateLogger_EmptyCategory_StillReturnsALogger(string categoryName)
    {
        using var sp = BuildContainer();
        var provider = new LoggerFileProvider(sp);

        Assert.NotNull(provider.CreateLogger(categoryName));
    }

    /// <summary>
    /// A null category is not guarded against either — worth pinning because the guard is exactly
    /// what would be added if D-M17 were fixed by keying a cache on the category.
    /// </summary>
    [Fact]
    public void CreateLogger_NullCategory_DoesNotThrow_KnownGap()
    {
        using var sp = BuildContainer();
        var provider = new LoggerFileProvider(sp);

        Assert.NotNull(provider.CreateLogger(null!));
    }

    [Fact]
    public void Dispose_IsANoOpAndIsIdempotent()
    {
        using var sp = BuildContainer();
        var provider = new LoggerFileProvider(sp);

        provider.Dispose();
        provider.Dispose();

        // Disposal releases nothing, so the provider keeps working afterwards. Pinned rather than
        // asserted as desirable: FileLogger holds no stream between calls, so there is nothing to
        // release — but that also means Dispose gives no flush guarantee to a caller expecting one.
        Assert.IsType<FileLogger>(provider.CreateLogger("Some.Category"));
    }

    /// <summary>
    /// The provider is constructed by <c>ActivatorUtilities</c> from the container in
    /// <c>AddFileProvider</c>, which means the generated <c>[Inject]</c> constructor must take a
    /// resolvable <see cref="IServiceProvider"/> and nothing else.
    /// </summary>
    [Fact]
    public void Provider_IsConstructableByActivatorUtilities()
    {
        using var sp = BuildContainer();

        var provider = ActivatorUtilities.CreateInstance<LoggerFileProvider>(sp);

        Assert.NotNull(provider);
    }
}
