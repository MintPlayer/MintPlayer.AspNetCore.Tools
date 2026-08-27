using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.LoggerProviders;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Logging;

public class FileLoggerTests
{
    private static FileLogger CreateLogger(string fileName) =>
        new(Options.Create(new FileLoggerOptions { FileName = fileName }));

    /// <summary>
    /// Writes through <see cref="ILogger"/> the way real callers do, so the framework's default
    /// message formatter is the one under test rather than a formatter the test invented.
    /// </summary>
    private static void LogInformation(ILogger logger, string message) => logger.LogInformation("{Message}", message);

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
    /// Each entry is the message line followed by a blank separator line.
    /// </summary>
    /// <remarks>
    /// Asserted through <see cref="File.ReadAllLines(string)"/> rather than against a literal
    /// string, because the logger uses <c>StreamWriter.WriteLine</c> and the byte-level line
    /// terminator therefore differs between the Windows dev box and the Linux runner.
    /// </remarks>
    [Fact]
    public void Log_WritesMessageLineThenBlankLine()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        LogInformation(logger, "hello");

        Assert.Equal(new[] { "hello", "" }, File.ReadAllLines(path));
    }

    [Fact]
    public void Log_CalledTwice_AppendsRatherThanTruncates()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        LogInformation(logger, "first");
        LogInformation(logger, "second");

        Assert.Equal(new[] { "first", "", "second", "" }, File.ReadAllLines(path));
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
        Assert.Contains("appended", lines);
    }

    [Fact]
    public void Log_MessageWithTemplateArguments_WritesTheFormattedResult()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        ((ILogger)logger).LogInformation("user {UserId} did {Action}", 42, "login");

        Assert.Equal("user 42 did login", File.ReadAllLines(path)[0]);
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
    /// The log file is held open exclusively for the duration of a single <c>Log</c> call, so a
    /// reader that already holds a write lock on the file makes the call fail.
    /// </summary>
    /// <remarks>
    /// This is the benign, deterministic half of D-M14: it documents the exclusive-open design
    /// without racing two threads (see <c>Log_ConcurrentCallsFromMultipleThreads_AllLinesWritten</c>).
    /// </remarks>
    [Fact]
    public void Log_FileLockedByAnotherWriter_ThrowsIOException_KnownBug()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        using var holder = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);

        Assert.Throws<IOException>(() => LogInformation(logger, "hello"));
    }

    #endregion

    #region Filename resolution and validation

    [Theory]
    [InlineData("Log.txt")]
    [InlineData("Log.log")]
    [InlineData("some.name.with.dots.log")]
    public void Log_AllowedExtension_Succeeds(string fileName)
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath(fileName);
        var logger = CreateLogger(path);

        LogInformation(logger, "hello");

        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData("Log.xml")]
    [InlineData("Log.json")]
    [InlineData("Log")]
    [InlineData("Log.")]
    public void Log_DisallowedExtension_ThrowsInvalidOperationException(string fileName)
    {
        using var temp = new TempDirectoryFixture();
        var logger = CreateLogger(temp.GetPath(fileName));

        var ex = Assert.Throws<InvalidOperationException>(() => LogInformation(logger, "hello"));

        Assert.Contains(".txt", ex.Message);
        Assert.Contains(".log", ex.Message);
    }

    /// <summary>
    /// Pins D-M11: the extension allowlist is a case-sensitive <c>Contains</c> over
    /// <c>[".txt", ".log"]</c>, so an upper- or mixed-case extension is rejected even though it
    /// names the same file on Windows and is an ordinary filename on Linux.
    /// </summary>
    [Theory]
    [InlineData("Log.TXT")]
    [InlineData("Log.Txt")]
    [InlineData("Log.LOG")]
    public void Log_UpperCaseAllowedExtension_Throws_KnownBug(string fileName)
    {
        using var temp = new TempDirectoryFixture();
        var logger = CreateLogger(temp.GetPath(fileName));

        Assert.Throws<InvalidOperationException>(() => LogInformation(logger, "hello"));
    }

    /// <summary>
    /// Pins D-M15: the filename is resolved and revalidated on every single <c>Log</c> call
    /// rather than once at construction, so mutating the options object mid-flight changes where
    /// (and whether) subsequent entries are written.
    /// </summary>
    [Fact]
    public void Log_RevalidatesFilenameOnEveryCall_KnownGap()
    {
        using var temp = new TempDirectoryFixture();
        var options = new FileLoggerOptions { FileName = temp.GetPath("Log.txt") };
        var logger = new FileLogger(Options.Create(options));

        LogInformation(logger, "first");
        options.FileName = temp.GetPath("Log.xml");

        Assert.Throws<InvalidOperationException>(() => LogInformation(logger, "second"));
    }

    /// <summary>
    /// With no options at all — or an options instance whose <c>FileName</c> was never set — the
    /// logger silently falls back to a relative <c>Log.txt</c> in the process's working directory.
    /// </summary>
    /// <remarks>
    /// Deliberately a single test rather than a theory: it is the only test in this folder that
    /// writes outside a temp directory, and keeping it to one case keeps two of these from racing
    /// each other over the same relative path under xunit's parallelism.
    /// </remarks>
    [Fact]
    public void Log_NoFileNameConfigured_FallsBackToLogTxtInWorkingDirectory()
    {
        var fallback = Path.Combine(Directory.GetCurrentDirectory(), "Log.txt");
        File.Delete(fallback);
        try
        {
            var logger = new FileLogger(Options.Create<FileLoggerOptions>(null!));

            LogInformation(logger, "fallback");

            Assert.True(File.Exists(fallback));
            Assert.Contains("fallback", File.ReadAllText(fallback));
        }
        finally
        {
            File.Delete(fallback);
        }
    }

    /// <summary>
    /// A null <see cref="IOptions{T}"/> is tolerated by the same <c>?.</c> chain that produces the
    /// <c>Log.txt</c> fallback, so this asserts only that it does not throw a
    /// <see cref="NullReferenceException"/> — the write itself is covered by the test above.
    /// </summary>
    [Fact]
    public void Log_NullOptions_DoesNotThrowNullReference()
    {
        var logger = new FileLogger(null!);

        // Validation of the fallback name happens before any file is touched, so an allowed
        // extension is all this needs to reach; the exception type below would be a
        // NullReferenceException if the guard chain regressed.
        var exception = Record.Exception(() =>
        {
            var fallback = Path.Combine(Directory.GetCurrentDirectory(), "Log.txt");
            try
            {
                LogInformation(logger, "hello");
            }
            finally
            {
                File.Delete(fallback);
            }
        });

        Assert.Null(exception);
    }

    #endregion

    #region Known gaps in what gets written

    /// <summary>
    /// Pins D-M7: exceptions are never written to the log file. <c>Log</c> writes only
    /// <c>formatter(state, exception)</c>, and the framework's default message formatter ignores
    /// its exception argument — so <c>LogError(ex, "boom")</c> records "boom" and loses the type,
    /// the message and the entire stack trace.
    /// </summary>
    [Fact]
    public void Log_WithException_DoesNotWriteTheException_KnownBug()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);
        var exception = new InvalidOperationException("the-real-cause");

        ((ILogger)logger).LogError(exception, "boom");

        var content = File.ReadAllText(path);
        Assert.Contains("boom", content);
        Assert.DoesNotContain("the-real-cause", content);
        Assert.DoesNotContain(nameof(InvalidOperationException), content);
    }

    /// <summary>
    /// Pins D-M7 for the inner exception too: a wrapped cause is just as invisible, which is the
    /// case where losing the stack trace hurts most.
    /// </summary>
    [Fact]
    public void Log_WithNestedException_WritesNothingOfEither_KnownBug()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);
        var exception = new InvalidOperationException("outer", new ArgumentException("inner"));

        ((ILogger)logger).LogError(exception, "boom");

        Assert.Equal(new[] { "boom", "" }, File.ReadAllLines(path));
    }

    /// <summary>
    /// Pins D-M8: no level, category, timestamp or EventId is written. The entry is the bare
    /// formatted message, so two entries logged a day apart at different levels from different
    /// categories are indistinguishable in the file.
    /// </summary>
    [Fact]
    public void Log_WritesNoLevelCategoryTimestampOrEventId_KnownGap()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        ((ILogger)logger).Log(LogLevel.Critical, new EventId(4711, "TheEventName"), "the-state", null,
            (state, _) => state);

        var lines = File.ReadAllLines(path);
        Assert.Equal("the-state", lines[0]);
        Assert.DoesNotContain("4711", lines[0]);
        Assert.DoesNotContain("TheEventName", lines[0]);
        Assert.DoesNotContain(nameof(LogLevel.Critical), lines[0]);
        // A timestamp of any plausible shape would need at least one digit somewhere on the line.
        Assert.DoesNotContain(lines[0], (char c) => char.IsDigit(c));
    }

    /// <summary>
    /// The log level reaches <c>Log</c> and is discarded, so entries at every level are written
    /// identically — including <see cref="LogLevel.None"/>, which is not a real level at all.
    /// </summary>
    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    [InlineData(LogLevel.None)]
    public void Log_EveryLevelIncludingNone_IsWrittenIdentically_KnownGap(LogLevel level)
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        ((ILogger)logger).Log(level, default, "message", null, (state, _) => state);

        Assert.Equal(new[] { "message", "" }, File.ReadAllLines(path));
    }

    #endregion

    #region ILogger contract

    /// <summary>
    /// Pins D-M12: <c>IsEnabled</c> is a hard <c>return true</c>, so it also claims to be enabled
    /// for <see cref="LogLevel.None"/>, whose documented meaning is "no logging at all".
    /// </summary>
    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    [InlineData(LogLevel.None)]
    public void IsEnabled_AlwaysReturnsTrue_KnownBug(LogLevel level)
    {
        var logger = CreateLogger("Log.txt");

        Assert.True(logger.IsEnabled(level));
    }

    /// <summary>
    /// Pins D-M12 at its worst: an out-of-range level is reported as enabled too, so nothing in
    /// this logger can ever be filtered by the level it was handed.
    /// </summary>
    [Fact]
    public void IsEnabled_UndefinedLevel_StillReturnsTrue_KnownBug()
    {
        var logger = CreateLogger("Log.txt");

        Assert.True(logger.IsEnabled((LogLevel)999));
    }

    /// <summary>
    /// Pins D-M13: <c>BeginScope</c> returns <c>default!</c> — i.e. <c>null</c> — from a method
    /// whose declared return type is <c>IDisposable?</c>. Every caller that writes the idiomatic
    /// <c>using (logger.BeginScope(...))</c> is fine (C# tolerates a null using-resource), but
    /// anything that dereferences the result gets a NullReferenceException, and the scope state is
    /// silently discarded either way.
    /// </summary>
    [Fact]
    public void BeginScope_ReturnsNull_KnownBug()
    {
        var logger = CreateLogger("Log.txt");

        Assert.Null(logger.BeginScope("scope-state"));
    }

    /// <summary>Scope state is dropped, so a scope cannot influence what is written.</summary>
    [Fact]
    public void BeginScope_DoesNotAffectWrittenEntries_KnownGap()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);

        using (logger.BeginScope("RequestId:abc"))
        {
            LogInformation(logger, "inside");
        }

        Assert.Equal(new[] { "inside", "" }, File.ReadAllLines(path));
    }

    #endregion

    #region Concurrency (D-M14)

    /// <summary>
    /// Reproduces D-M14: <c>Log</c> opens a fresh <c>FileStream(path, Append, Write)</c> per call
    /// with the default <c>FileShare.Read</c>, so two threads logging at once race and the loser
    /// gets an <see cref="IOException"/> instead of a log line.
    /// </summary>
    /// <remarks>
    /// Skipped on purpose until D-M14 is fixed: it is a bug reproduction, not a stable regression
    /// test. It would be flaky-red, and an IOException racing on a thread-pool thread inside the
    /// test host is not worth the noise. M9 fixes the defect and un-skips this.
    /// </remarks>
    [Fact(Skip = "Bug reproduction for D-M14 (FileLogger.Log is not thread-safe). Un-skip once M9 serialises the writes.")]
    public void Log_ConcurrentCallsFromMultipleThreads_AllLinesWritten()
    {
        using var temp = new TempDirectoryFixture();
        var path = temp.GetPath("Log.txt");
        var logger = CreateLogger(path);
        const int iterations = 200;

        Parallel.For(0, iterations, i => LogInformation(logger, $"line-{i}"));

        var written = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        Assert.Equal(iterations, written.Length);
    }

    #endregion
}
