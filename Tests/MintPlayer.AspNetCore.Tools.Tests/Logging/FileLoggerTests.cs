using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.LoggerProviders;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Logging;

public class FileLoggerTests
{
    private const string Category = "Tests.Category";

    /// <summary>
    /// The written header line: a round-trip UTC timestamp, the level in brackets, then the
    /// category. Anchored at the start so a test can assert the rest of the line literally.
    /// </summary>
    private static readonly Regex Header = new(
        @"^(?<timestamp>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z) \[(?<level>\w+)\] (?<rest>.*)$",
        RegexOptions.Compiled);

    private static FileLogWriter CreateWriter(string fileName, Action<FileLoggerOptions>? configure = null)
    {
        var options = new FileLoggerOptions { FileName = fileName };
        configure?.Invoke(options);
        return new FileLogWriter(Options.Create(options));
    }

    private static FileLogger CreateLogger(string fileName, Action<FileLoggerOptions>? configure = null, TimeProvider? clock = null)
        => new(Category, CreateWriter(fileName, configure), clock);

    /// <summary>
    /// Writes through <see cref="ILogger"/> the way real callers do, so the framework's default
    /// message formatter is the one under test rather than a formatter the test invented.
    /// </summary>
    private static void LogInformation(ILogger logger, string message) => logger.LogInformation("{Message}", message);

    /// <summary>Splits one written entry's header line into its parts, failing if it is malformed.</summary>
    private static Match AssertHeader(string line, string expectedLevel)
    {
        var match = Header.Match(line);
        Assert.True(match.Success, $"Not a well-formed log header: '{line}'");
        Assert.Equal(expectedLevel, match.Groups["level"].Value);
        return match;
    }

    #region Writing

    [Fact]
    public void Log_CreatesFileAndWritesFormattedMessage()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        LogInformation(logger, "hello");

        Assert.True(File.Exists(path));
        Assert.Contains("hello", File.ReadAllText(path));
    }

    /// <summary>
    /// The entry format (D-M8): <c>&lt;timestamp&gt; [&lt;Level&gt;] &lt;Category&gt; &lt;message&gt;</c>
    /// on one line, followed by a blank separator line.
    /// </summary>
    /// <remarks>
    /// Asserted through <see cref="File.ReadAllLines(string)"/> rather than against a literal
    /// string, because the logger writes <c>Environment.NewLine</c> and the byte-level line
    /// terminator therefore differs between the Windows dev box and the Linux runner.
    /// </remarks>
    [Fact]
    public void Log_WritesTimestampLevelCategoryAndMessage_ThenBlankLine()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        LogInformation(logger, "hello");

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Equal($"{Category} hello", AssertHeader(lines[0], nameof(LogLevel.Information)).Groups["rest"].Value);
        Assert.Equal(string.Empty, lines[1]);
    }

    /// <summary>
    /// The timestamp comes from an injectable <see cref="TimeProvider"/> and is written as UTC in
    /// round-trip ("O") form with the invariant culture, so the file reads identically on the
    /// Windows dev box and the UTC Linux runner.
    /// </summary>
    [Fact]
    public void Log_TimestampComesFromTheTimeProvider_AndIsUtcRoundTrip()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path, clock: new FixedTimeProvider());

        LogInformation(logger, "hello");

        Assert.Equal(
            $"{FixedTimeProvider.DefaultNowUtcRoundTrip} [Information] {Category} hello",
            File.ReadAllLines(path)[0]);
    }

    [Fact]
    public void Log_CalledTwice_AppendsRatherThanTruncates()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        LogInformation(logger, "first");
        LogInformation(logger, "second");

        var lines = File.ReadAllLines(path);
        Assert.Equal(4, lines.Length);
        Assert.EndsWith("first", lines[0]);
        Assert.Equal(string.Empty, lines[1]);
        Assert.EndsWith("second", lines[2]);
        Assert.Equal(string.Empty, lines[3]);
    }

    [Fact]
    public void Log_ExistingFile_PreservesEarlierContent()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        File.WriteAllLines(path, new[] { "pre-existing" });
        var logger = CreateLogger(path);

        LogInformation(logger, "appended");

        var lines = File.ReadAllLines(path);
        Assert.Equal("pre-existing", lines[0]);
        Assert.EndsWith("appended", lines[1]);
    }

    [Fact]
    public void Log_MessageWithTemplateArguments_WritesTheFormattedResult()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        ((ILogger)logger).LogInformation("user {UserId} did {Action}", 42, "login");

        Assert.EndsWith("user 42 did login", File.ReadAllLines(path)[0]);
    }

    /// <summary>
    /// A message that formats to nothing and carries no exception writes no entry at all — there is
    /// nothing to diagnose from a bare timestamp.
    /// </summary>
    [Fact]
    public void Log_EmptyMessageWithoutException_WritesNothing()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        ((ILogger)logger).Log(LogLevel.Information, default, string.Empty, null, static (state, _) => state);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Log_DirectoryDoesNotExist_ThrowsDirectoryNotFound()
    {
        using var temp = new TempDirectoryFixture();
        var path = Path.Combine(temp.Path, "no-such-subfolder", "Log.txt");
        var logger = CreateLogger(path);

        Assert.Throws<DirectoryNotFoundException>(() => LogInformation(logger, "hello"));
    }

    /// <summary>
    /// The writer opens the file with <c>FileShare.ReadWrite</c>, so it never locks readers out —
    /// but a foreign writer holding the file with a share mode that excludes writers still wins,
    /// and the resulting <see cref="IOException"/> surfaces to the caller rather than being
    /// swallowed.
    /// </summary>
    /// <remarks>
    /// This is the deterministic counterpart to
    /// <c>Log_ConcurrentCallsFromMultipleThreads_AllLinesWritten</c>: the fix for D-M14 serialises
    /// <i>this</i> process's writes, and cannot do anything about another process's exclusive lock.
    /// </remarks>
    [Fact]
    public void Log_FileLockedAgainstWritersByAnotherProcess_ThrowsIOException()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        using var holder = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);

        Assert.Throws<IOException>(() => LogInformation(logger, "hello"));
    }

    /// <summary>
    /// Reading the log file while the logger is in use must work with a plain
    /// <see cref="File.ReadAllText(string)"/> — i.e. with the default <c>FileShare.Read</c>, which
    /// a held-open write handle would reject on Windows. Every other test here depends on this, so
    /// it is asserted directly.
    /// </summary>
    [Fact]
    public void Log_LeavesNoHandleOpen_SoOrdinaryReadersAreNeverLockedOut()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        LogInformation(logger, "first");
        var readBetweenWrites = File.ReadAllText(path);
        LogInformation(logger, "second");

        Assert.Contains("first", readBetweenWrites);
        Assert.DoesNotContain("second", readBetweenWrites);
        Assert.Contains("second", File.ReadAllText(path));
    }

    #endregion

    #region Filename resolution and validation

    [Theory]
    [InlineData("Log.txt")]
    [InlineData("Log.log")]
    [InlineData("some.name.with.dots.log")]
    public void AllowedExtension_Succeeds(string fileName)
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath(fileName);
        var logger = CreateLogger(path);

        LogInformation(logger, "hello");

        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// D-M11: the extension allowlist is compared with
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>, so <c>Log.TXT</c> — the same file as
    /// <c>Log.txt</c> on Windows and an ordinary filename on Linux — is accepted.
    /// </summary>
    [Theory]
    [InlineData("Log.TXT")]
    [InlineData("Log.Txt")]
    [InlineData("Log.LOG")]
    public void AllowedExtension_IsCaseInsensitive(string fileName)
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath(fileName);
        var logger = CreateLogger(path);

        LogInformation(logger, "hello");

        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// D-M15: the filename is validated once, when the destination is created, not on every write.
    /// A disallowed extension therefore fails at wiring-up time instead of out of some unrelated
    /// caller's <c>LogInformation</c>.
    /// </summary>
    [Theory]
    [InlineData("Log.xml")]
    [InlineData("Log.json")]
    [InlineData("Log")]
    [InlineData("Log.")]
    public void DisallowedExtension_ThrowsWhenTheDestinationIsCreated(string fileName)
    {
        using var temp = new TempDirectoryFixture();

        var ex = Assert.Throws<InvalidOperationException>(() => CreateWriter(temp.GetPath(fileName)));

        Assert.Contains(".txt", ex.Message);
        Assert.Contains(".log", ex.Message);
    }

    /// <summary>
    /// D-M15, the other half: because validation and path resolution happen once, mutating the
    /// options object afterwards cannot move — or break — subsequent writes.
    /// </summary>
    [Fact]
    public void FileName_IsResolvedOnce_SoLaterOptionMutationIsIgnored()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var options = new FileLoggerOptions { FileName = path };
        var logger = new FileLogger(Category, new FileLogWriter(Options.Create(options)));

        LogInformation(logger, "first");
        options.FileName = temp.GetPath("Log.xml");
        LogInformation(logger, "second");

        Assert.Contains("second", File.ReadAllText(path));
        Assert.False(File.Exists(temp.GetPath("Log.xml")));
    }

    /// <summary>
    /// A relative filename is resolved against the working directory once, so the log file cannot
    /// move if the process later changes its current directory.
    /// </summary>
    [Fact]
    public void RelativeFileName_IsResolvedToAnAbsolutePathOnce()
    {
        var writer = CreateWriter(Path.Combine("logs", "Log.txt"));

        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "logs", "Log.txt"), writer.FilePath);
        Assert.True(Path.IsPathFullyQualified(writer.FilePath));
    }

    /// <summary>
    /// D-M46 and D-M47: an unconfigured <c>FileName</c> is refused loudly instead of silently
    /// producing a relative <c>Log.txt</c> in whatever <see cref="Directory.GetCurrentDirectory"/>
    /// happens to be — which under IIS or a Windows Service is neither the content root nor
    /// necessarily writable.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoFileNameConfigured_ThrowsInsteadOfWritingToTheWorkingDirectory(string? fileName)
    {
        var fallbackThatMustNotAppear = Path.Combine(Directory.GetCurrentDirectory(), "Log.txt");

        var ex = Assert.Throws<InvalidOperationException>(
            () => new FileLogWriter(Options.Create(new FileLoggerOptions { FileName = fileName })));

        Assert.Contains(nameof(FileLoggerOptions.FileName), ex.Message);
        Assert.False(File.Exists(fallbackThatMustNotAppear));
    }

    [Fact]
    public void NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FileLogWriter(null!));
    }

    [Fact]
    public void NullOptionsValue_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => new FileLogWriter(Options.Create<FileLoggerOptions>(null!)));
    }

    [Fact]
    public void NullWriter_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FileLogger(Category, null!));
    }

    #endregion

    #region What gets written

    /// <summary>
    /// D-M7: the exception is written after the message. The framework's default message formatter
    /// ignores its exception argument, so a logger that writes only <c>formatter(state, exception)</c>
    /// silently drops the one thing a log file exists for.
    /// </summary>
    [Fact]
    public void Log_WithException_WritesTheExceptionAfterTheMessage()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);
        var exception = new InvalidOperationException("the-real-cause");

        ((ILogger)logger).LogError(exception, "boom");

        var lines = File.ReadAllLines(path);
        Assert.EndsWith("boom", lines[0]);
        Assert.Contains(nameof(InvalidOperationException), lines[1]);
        Assert.Contains("the-real-cause", lines[1]);
    }

    /// <summary>The stack trace of a thrown exception survives, not just its message.</summary>
    [Fact]
    public void Log_WithThrownException_WritesTheStackTrace()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        try
        {
            throw new InvalidOperationException("the-real-cause");
        }
        catch (InvalidOperationException ex)
        {
            ((ILogger)logger).LogError(ex, "boom");
        }

        var content = File.ReadAllText(path);
        Assert.Contains("the-real-cause", content);
        Assert.Contains(nameof(Log_WithThrownException_WritesTheStackTrace), content);
    }

    /// <summary>D-M7 for the wrapped cause too — the case where losing it hurts most.</summary>
    [Fact]
    public void Log_WithNestedException_WritesBothOuterAndInner()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);
        var exception = new InvalidOperationException("outer", new ArgumentException("inner"));

        ((ILogger)logger).LogError(exception, "boom");

        var content = File.ReadAllText(path);
        Assert.Contains("outer", content);
        Assert.Contains("inner", content);
        Assert.Contains(nameof(ArgumentException), content);
    }

    /// <summary>
    /// D-M8: level, category, timestamp and a non-zero <see cref="EventId"/> all reach the file, so
    /// two entries logged a day apart at different levels from different categories are
    /// distinguishable.
    /// </summary>
    [Fact]
    public void Log_WritesLevelCategoryTimestampAndEventId()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path, clock: new FixedTimeProvider());

        ((ILogger)logger).Log(LogLevel.Critical, new EventId(4711, "TheEventName"), "the-state", null,
            static (state, _) => state);

        Assert.Equal(
            $"{FixedTimeProvider.DefaultNowUtcRoundTrip} [Critical] {Category}[4711:TheEventName] the-state",
            File.ReadAllLines(path)[0]);
    }

    /// <summary>An unnamed, non-zero event id is written as the bare number.</summary>
    [Fact]
    public void Log_UnnamedEventId_WritesTheIdOnly()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        ((ILogger)logger).Log(LogLevel.Warning, new EventId(42), "message", null, static (state, _) => state);

        Assert.Equal($"{Category}[42] message", AssertHeader(File.ReadAllLines(path)[0], nameof(LogLevel.Warning)).Groups["rest"].Value);
    }

    /// <summary>
    /// A zero, unnamed event id carries no information and is left out rather than written as
    /// <c>[0]</c> on every single line.
    /// </summary>
    [Fact]
    public void Log_ZeroEventId_IsOmitted()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        LogInformation(logger, "hello");

        Assert.DoesNotContain("[0]", File.ReadAllText(path));
    }

    /// <summary>An empty category — the factory's own <c>CreateLogger("")</c> — is simply absent.</summary>
    [Fact]
    public void Log_EmptyCategory_WritesNoCategoryToken()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = new FileLogger(string.Empty, CreateWriter(path), new FixedTimeProvider());

        LogInformation(logger, "hello");

        Assert.Equal($"{FixedTimeProvider.DefaultNowUtcRoundTrip} [Information] hello", File.ReadAllLines(path)[0]);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void Log_EveryRealLevel_IsWrittenWithItsOwnName(LogLevel level)
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        ((ILogger)logger).Log(level, default, "message", null, static (state, _) => state);

        Assert.Equal($"{Category} message", AssertHeader(File.ReadAllLines(path)[0], level.ToString()).Groups["rest"].Value);
    }

    /// <summary>
    /// D-M12: <see cref="LogLevel.None"/> means "no logging at all", so an entry handed over at
    /// that level is dropped rather than written like any other.
    /// </summary>
    [Fact]
    public void Log_LogLevelNone_WritesNothing()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        ((ILogger)logger).Log(LogLevel.None, default, "message", null, static (state, _) => state);

        Assert.False(File.Exists(path));
    }

    /// <summary>An entry below the configured minimum is dropped by the provider itself.</summary>
    [Fact]
    public void Log_BelowConfiguredMinimumLevel_WritesNothing()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path, o => o.MinimumLevel = LogLevel.Warning);

        LogInformation(logger, "suppressed");
        ((ILogger)logger).LogWarning("{Message}", "kept");

        var content = File.ReadAllText(path);
        Assert.DoesNotContain("suppressed", content);
        Assert.Contains("kept", content);
    }

    #endregion

    #region ILogger contract

    /// <summary>
    /// D-M12: the six real levels are enabled by default, and <see cref="LogLevel.None"/> — whose
    /// documented meaning is "no logging at all" — never is.
    /// </summary>
    [Theory]
    [InlineData(LogLevel.Trace, true)]
    [InlineData(LogLevel.Debug, true)]
    [InlineData(LogLevel.Information, true)]
    [InlineData(LogLevel.Warning, true)]
    [InlineData(LogLevel.Error, true)]
    [InlineData(LogLevel.Critical, true)]
    [InlineData(LogLevel.None, false)]
    public void IsEnabled_IsTrueForRealLevelsAndFalseForNone(LogLevel level, bool expected)
    {
        var logger = CreateLogger("Log.txt");

        Assert.Equal(expected, logger.IsEnabled(level));
    }

    /// <summary>An out-of-range value is not a level, so it is not enabled either.</summary>
    [Fact]
    public void IsEnabled_UndefinedLevel_ReturnsFalse()
    {
        var logger = CreateLogger("Log.txt");

        Assert.False(logger.IsEnabled((LogLevel)999));
    }

    /// <summary>
    /// D-M12's real point: the provider is filterable on its own, without depending on an
    /// <c>ILoggingBuilder</c> filter above it.
    /// </summary>
    [Theory]
    [InlineData(LogLevel.Trace, false)]
    [InlineData(LogLevel.Information, false)]
    [InlineData(LogLevel.Warning, true)]
    [InlineData(LogLevel.Critical, true)]
    [InlineData(LogLevel.None, false)]
    public void IsEnabled_RespectsTheConfiguredMinimumLevel(LogLevel level, bool expected)
    {
        var logger = CreateLogger("Log.txt", o => o.MinimumLevel = LogLevel.Warning);

        Assert.Equal(expected, logger.IsEnabled(level));
    }

    /// <summary>A <c>MinimumLevel</c> of <see cref="LogLevel.None"/> disables the provider entirely.</summary>
    [Fact]
    public void IsEnabled_MinimumLevelNone_DisablesEveryLevel()
    {
        var logger = CreateLogger("Log.txt", o => o.MinimumLevel = LogLevel.None);

        Assert.All(
            new[] { LogLevel.Trace, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical, LogLevel.None },
            level => Assert.False(logger.IsEnabled(level)));
    }

    /// <summary>
    /// D-M13: <c>BeginScope</c> honours its <c>IDisposable</c> contract — a caller that
    /// dereferences the result no longer gets a <see cref="NullReferenceException"/>.
    /// </summary>
    [Fact]
    public void BeginScope_ReturnsANonNullDisposable()
    {
        var logger = CreateLogger("Log.txt");

        var scope = logger.BeginScope("scope-state");

        Assert.NotNull(scope);
        scope.Dispose();
    }

    /// <summary>Even with scopes switched off the result is a real disposable, just an inert one.</summary>
    [Fact]
    public void BeginScope_WithScopesDisabled_StillReturnsANonNullDisposable()
    {
        var logger = CreateLogger("Log.txt", o => o.IncludeScopes = false);

        var scope = logger.BeginScope("scope-state");

        Assert.NotNull(scope);
        scope.Dispose();
    }

    /// <summary>
    /// D-M13's second half: now that entries are structured, the scope is part of them — in a file
    /// read long after the fact, the scope is often the only thing tying an entry to its request.
    /// </summary>
    [Fact]
    public void BeginScope_ScopeStateAppearsInWrittenEntries()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path, clock: new FixedTimeProvider());

        using (logger.BeginScope("RequestId:abc"))
        {
            LogInformation(logger, "inside");
        }
        LogInformation(logger, "outside");

        var lines = File.ReadAllLines(path);
        Assert.Equal(
            $"{FixedTimeProvider.DefaultNowUtcRoundTrip} [Information] {Category} => RequestId:abc inside",
            lines[0]);
        Assert.DoesNotContain("RequestId:abc", lines[2]);
    }

    /// <summary>Nested scopes are written outermost first, in the order they were pushed.</summary>
    [Fact]
    public void BeginScope_NestedScopes_AreWrittenOutermostFirst()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        using (logger.BeginScope("outer"))
        using (logger.BeginScope("inner"))
        {
            LogInformation(logger, "message");
        }

        Assert.Equal($"{Category} => outer => inner message",
            AssertHeader(File.ReadAllLines(path)[0], nameof(LogLevel.Information)).Groups["rest"].Value);
    }

    [Fact]
    public void BeginScope_WithScopesDisabled_ScopeIsNotWritten()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path, o => o.IncludeScopes = false);

        using (logger.BeginScope("RequestId:abc"))
        {
            LogInformation(logger, "inside");
        }

        var content = File.ReadAllText(path);
        Assert.Contains("inside", content);
        Assert.DoesNotContain("RequestId:abc", content);
    }

    #endregion

    #region Concurrency (D-M14)

    /// <summary>
    /// D-M14: concurrent <c>Log</c> calls all land in the file. Writes are serialised on a lock
    /// shared by every writer targeting the same resolved path, and each entry is one
    /// open-append-close, so neither thread can lose its line to an <see cref="IOException"/>.
    /// </summary>
    /// <remarks>
    /// Deterministic rather than probabilistic: the assertion is on the exact line count, and no
    /// outcome other than "all 200" is reachable — a lost write would have to surface as a thrown
    /// <see cref="IOException"/> out of <c>Parallel.For</c>, which fails the test just as loudly.
    /// </remarks>
    [Fact]
    public void Log_ConcurrentCallsFromMultipleThreads_AllLinesWritten()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);
        const int iterations = 200;

        Parallel.For(0, iterations, i => LogInformation(logger, $"line-{i}"));

        var written = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        Assert.Equal(iterations, written.Length);
        Assert.All(written, line => AssertHeader(line, nameof(LogLevel.Information)));
        Assert.Equal(iterations, Enumerable.Range(0, iterations).Count(i => written.Any(l => l.EndsWith($"line-{i}", StringComparison.Ordinal))));
    }

    /// <summary>
    /// Two separate destinations pointed at the same file must not race each other either — the
    /// lock is keyed by resolved path, not per writer instance.
    /// </summary>
    [Fact]
    public void Log_ConcurrentCallsThroughTwoWritersOnTheSameFile_AllLinesWritten()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var first = CreateLogger(path);
        var second = CreateLogger(path);
        const int iterations = 200;

        Parallel.For(0, iterations, i => LogInformation(i % 2 == 0 ? first : second, $"line-{i}"));

        Assert.Equal(iterations, File.ReadAllLines(path).Count(l => l.Length > 0));
    }

    /// <summary>
    /// A multi-line entry (message plus exception) is never interleaved with another thread's
    /// entry: every line in the file belongs to a well-formed entry.
    /// </summary>
    [Fact]
    public void Log_ConcurrentEntriesWithExceptions_AreNotInterleaved()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);
        const int iterations = 100;

        Parallel.For(0, iterations, i =>
            ((ILogger)logger).LogError(new InvalidOperationException($"cause-{i}"), "{Message}", $"line-{i}"));

        var lines = File.ReadAllLines(path);
        var headers = lines.Where(l => Header.IsMatch(l)).ToArray();
        var causes = lines.Where(l => l.Contains("cause-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(iterations, headers.Length);
        Assert.Equal(iterations, causes.Length);
        // header, exception, blank — in that order, for every entry.
        Assert.Equal(iterations * 3, lines.Length);
        for (var i = 0; i < iterations; i++)
        {
            Assert.True(Header.IsMatch(lines[i * 3]), $"Line {i * 3} is not a header: '{lines[i * 3]}'");
            Assert.Contains("cause-", lines[(i * 3) + 1]);
            Assert.Equal(string.Empty, lines[(i * 3) + 2]);
        }
    }

    #endregion
}
