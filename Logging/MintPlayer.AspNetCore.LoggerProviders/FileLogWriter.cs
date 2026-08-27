using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Options;

namespace MintPlayer.AspNetCore.LoggerProviders;

/// <summary>
/// The single destination shared by every <see cref="FileLogger"/> of one provider: it resolves and
/// validates the configured path once, and appends already-formatted entries to it atomically.
/// </summary>
/// <remarks>
/// Nothing is buffered and no handle is kept: each entry is one <c>open, append, close</c> under a
/// lock that is shared by every writer targeting the same resolved path (process-wide, so a second
/// provider on the same file cannot race the first). That costs a syscall pair per entry, and buys
/// two things worth more than the syscalls at a log file's write volume: ordinary readers
/// (<c>File.ReadAllText</c>, editors, <c>tail</c>) are never locked out, and there is no window in
/// which a crash or a missed <c>Dispose</c> loses entries the caller believes were written. That is
/// also why the provider's <c>Dispose</c> owes no flush.
/// </remarks>
internal sealed class FileLogWriter
{
    /// <summary>No BOM: entries are appended, and a BOM mid-file is corruption.</summary>
    private static readonly Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// One lock per resolved path. Keyed ordinally: the key is already normalised by
    /// <see cref="Path.GetFullPath(string)"/>, and case-folding it would be wrong on Linux.
    /// </summary>
    private static readonly ConcurrentDictionary<string, object> gates = new(StringComparer.Ordinal);

    internal static readonly string[] AllowedExtensions = [".txt", ".log"];

    private readonly object gate;

    public FileLogWriter(IOptions<FileLoggerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value ?? throw new InvalidOperationException(
            $"No {nameof(FileLoggerOptions)} were configured. Call AddFileProvider(options => options.FileName = ...).");

        FilePath = ResolveFilePath(value.FileName);
        MinimumLevel = value.MinimumLevel;
        IncludeScopes = value.IncludeScopes;
        gate = gates.GetOrAdd(FilePath, static _ => new object());
    }

    /// <summary>Absolute path of the log file, resolved once at construction.</summary>
    public string FilePath { get; }

    public LogLevel MinimumLevel { get; }

    public bool IncludeScopes { get; }

    /// <summary>Appends one fully formatted entry, serialised against every other writer on this file.</summary>
    public void Write(string entry)
    {
        var bytes = encoding.GetBytes(entry);

        lock (gate)
        {
            // A single Write of the whole entry keeps an entry from being interleaved with
            // another process's append.
            using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    /// <summary>
    /// Validates <paramref name="fileName"/> and turns it into an absolute path. Shared with
    /// <see cref="FileLoggerOptionsValidator"/> so the same rule applies whether the options come
    /// from DI or from a directly constructed logger.
    /// </summary>
    internal static string ResolveFilePath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException(
                $"{nameof(FileLoggerOptions)}.{nameof(FileLoggerOptions.FileName)} is not configured. " +
                "Set it explicitly, e.g. AddFileProvider(options => options.FileName = Path.Combine(env.ContentRootPath, \"Log.txt\")). " +
                $"Allowed extensions: {string.Join(", ", AllowedExtensions)}.");
        }

        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Logger file extension {extension} is not allowed. Only {string.Join(", ", AllowedExtensions)} are allowed");
        }

        return Path.GetFullPath(fileName);
    }
}
