using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.LoggerProviders;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Logging;

public class LoggerFileProviderTests
{
    /// <summary>
    /// The provider resolves its options and its clock from the container, so tests need a real
    /// container rather than a stub <see cref="IServiceProvider"/>.
    /// </summary>
    private static ServiceProvider BuildContainer(string? fileName, TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddOptions<FileLoggerOptions>().Configure(o => o.FileName = fileName);
        if (clock is not null) services.AddSingleton(clock);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void CreateLogger_ReturnsAFileLogger()
    {
        using var temp = new TempDirectoryFixture();
        using var sp = BuildContainer(temp.GetPath("Log.txt"));
        using var provider = new LoggerFileProvider(sp);

        var logger = provider.CreateLogger("Some.Category");

        Assert.IsType<FileLogger>(logger);
    }

    [Fact]
    public void CreateLogger_ReturnedLoggerUsesTheConfiguredFileName()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildContainer(path);
        using var provider = new LoggerFileProvider(sp);

        provider.CreateLogger("Some.Category").LogInformation("{Message}", "hello");

        Assert.Contains("hello", File.ReadAllText(path));
    }

    /// <summary>
    /// D-M17, the caching half: one logger per category, so a host with hundreds of categories does
    /// not allocate a logger per <c>CreateLogger</c> call.
    /// </summary>
    [Fact]
    public void CreateLogger_SameCategoryTwice_ReturnsTheCachedInstance()
    {
        using var temp = new TempDirectoryFixture();
        using var sp = BuildContainer(temp.GetPath("Log.txt"));
        using var provider = new LoggerFileProvider(sp);

        var first = provider.CreateLogger("Some.Category");
        var second = provider.CreateLogger("Some.Category");

        Assert.Same(first, second);
    }

    [Fact]
    public void CreateLogger_DifferentCategories_ReturnDifferentInstances()
    {
        using var temp = new TempDirectoryFixture();
        using var sp = BuildContainer(temp.GetPath("Log.txt"));
        using var provider = new LoggerFileProvider(sp);

        Assert.NotSame(provider.CreateLogger("Category.One"), provider.CreateLogger("Category.Two"));
    }

    /// <summary>
    /// D-M17, the category half: <c>categoryName</c> reaches the logger and is written, so an entry
    /// says which category produced it.
    /// </summary>
    [Fact]
    public void CreateLogger_CategoryNameIsWrittenToTheFile()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildContainer(path, new FixedTimeProvider());
        using var provider = new LoggerFileProvider(sp);

        provider.CreateLogger("Category.One").LogInformation("{Message}", "from-one");
        provider.CreateLogger("Category.Two").LogInformation("{Message}", "from-two");

        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        Assert.Equal(
            new[]
            {
                $"{FixedTimeProvider.DefaultNowUtcRoundTrip} [Information] Category.One from-one",
                $"{FixedTimeProvider.DefaultNowUtcRoundTrip} [Information] Category.Two from-two",
            },
            lines);
    }

    /// <summary>
    /// The clock is taken from the container when one is registered, which is what makes the
    /// timestamp in the entry format assertable at all.
    /// </summary>
    [Fact]
    public void CreateLogger_UsesTheRegisteredTimeProvider()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildContainer(path, new FixedTimeProvider());
        using var provider = new LoggerFileProvider(sp);

        provider.CreateLogger("Some.Category").LogInformation("{Message}", "hello");

        Assert.StartsWith(FixedTimeProvider.DefaultNowUtcRoundTrip, File.ReadAllLines(path)[0]);
    }

    /// <summary>No registered clock means the wall clock, not a crash.</summary>
    [Fact]
    public void CreateLogger_WithoutARegisteredTimeProvider_UsesTheWallClock()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildContainer(path);
        using var provider = new LoggerFileProvider(sp);
        var before = DateTime.UtcNow.AddSeconds(-5);

        provider.CreateLogger("Some.Category").LogInformation("{Message}", "hello");

        var timestamp = DateTime.Parse(File.ReadAllLines(path)[0].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.InRange(timestamp.ToUniversalTime(), before, DateTime.UtcNow.AddSeconds(5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateLogger_EmptyCategory_StillReturnsALogger(string categoryName)
    {
        using var temp = new TempDirectoryFixture();
        using var sp = BuildContainer(temp.GetPath("Log.txt"));
        using var provider = new LoggerFileProvider(sp);

        Assert.NotNull(provider.CreateLogger(categoryName));
    }

    /// <summary>
    /// A null category is normalised to the empty one rather than rejected: the cache is keyed on
    /// the category now, and a caller passing null should not be punished for it.
    /// </summary>
    [Fact]
    public void CreateLogger_NullCategory_IsTreatedAsTheEmptyCategory()
    {
        using var temp = new TempDirectoryFixture();
        using var sp = BuildContainer(temp.GetPath("Log.txt"));
        using var provider = new LoggerFileProvider(sp);

        Assert.Same(provider.CreateLogger(null!), provider.CreateLogger(string.Empty));
    }

    /// <summary>
    /// D-M46/D-M47: the provider refuses to exist without a configured filename, and says so while
    /// logging is being wired up rather than writing somewhere unpredictable.
    /// </summary>
    /// <remarks>
    /// This container deliberately skips the <c>IValidateOptions</c> registration that
    /// <c>AddFileProvider</c> adds, so the guard being exercised here is the writer's own — the one
    /// that also covers a directly constructed logger. The registered-validator path is asserted in
    /// <see cref="FileLoggerEndToEndTests"/>.
    /// </remarks>
    [Fact]
    public void Constructor_WithoutAConfiguredFileName_Throws()
    {
        using var sp = BuildContainer(fileName: null);

        var ex = Assert.Throws<InvalidOperationException>(() => new LoggerFileProvider(sp));

        Assert.Contains(nameof(FileLoggerOptions.FileName), ex.Message);
    }

    [Fact]
    public void Constructor_NullServiceProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LoggerFileProvider(null!));
    }

    #region Scopes

    /// <summary>
    /// The provider implements <see cref="ISupportExternalScope"/>, which is how the logger factory
    /// shares one scope stack across every provider — the idiomatic answer to D-M13.
    /// </summary>
    [Fact]
    public void Provider_SupportsExternalScope()
    {
        using var temp = new TempDirectoryFixture();
        using var sp = BuildContainer(temp.GetPath("Log.txt"));
        using var provider = new LoggerFileProvider(sp);

        Assert.IsAssignableFrom<ISupportExternalScope>(provider);
    }

    /// <summary>
    /// A scope provider handed over after loggers were already created must reach those loggers
    /// too — the factory calls <c>SetScopeProvider</c> whenever it likes.
    /// </summary>
    [Fact]
    public void SetScopeProvider_AppliesToAlreadyCreatedLoggers()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildContainer(path);
        using var provider = new LoggerFileProvider(sp);
        var logger = provider.CreateLogger("Some.Category");
        var scopes = new LoggerExternalScopeProvider();

        provider.SetScopeProvider(scopes);
        using (scopes.Push("RequestId:abc"))
        {
            logger.LogInformation("{Message}", "inside");
        }

        Assert.Contains("=> RequestId:abc", File.ReadAllText(path));
    }

    [Fact]
    public void SetScopeProvider_Null_ThrowsArgumentNullException()
    {
        using var temp = new TempDirectoryFixture();
        using var sp = BuildContainer(temp.GetPath("Log.txt"));
        using var provider = new LoggerFileProvider(sp);

        Assert.Throws<ArgumentNullException>(() => provider.SetScopeProvider(null!));
    }

    #endregion

    /// <summary>
    /// D-M48: disposal has nothing to flush, and that is a property of the design rather than an
    /// oversight — the writer buffers nothing and holds no handle, so an entry is on disk before
    /// <c>Log</c> returns. Disposal releases the cached loggers, is idempotent, and cannot lose a
    /// line.
    /// </summary>
    [Fact]
    public void Dispose_IsIdempotentAndCannotLoseWrittenLines()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        using var sp = BuildContainer(path);
        var provider = new LoggerFileProvider(sp);
        provider.CreateLogger("Some.Category").LogInformation("{Message}", "before-dispose");

        provider.Dispose();
        provider.Dispose();

        Assert.Contains("before-dispose", File.ReadAllText(path));
        // The provider stays usable, because the logger factory may hold on to it during shutdown.
        Assert.IsType<FileLogger>(provider.CreateLogger("Some.Category"));
    }

    /// <summary>
    /// The provider is constructed by <c>ActivatorUtilities</c> in <c>AddFileProvider</c>, so it
    /// must have exactly one constructor and it must be satisfiable from the container alone.
    /// </summary>
    [Fact]
    public void Provider_IsConstructableByActivatorUtilities()
    {
        using var temp = new TempDirectoryFixture();
        using var sp = BuildContainer(temp.GetPath("Log.txt"));

        using var provider = ActivatorUtilities.CreateInstance<LoggerFileProvider>(sp);

        Assert.NotNull(provider);
        Assert.Single(typeof(LoggerFileProvider).GetConstructors());
    }
}
