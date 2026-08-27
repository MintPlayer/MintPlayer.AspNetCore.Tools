namespace MintPlayer.AspNetCore.LoggerProviders;

/// <summary>Configures the file logger provider.</summary>
public class FileLoggerOptions
{
    /// <summary>
    /// Path of the log file. Must be set — there is no default, and no fallback into the
    /// process's current directory (see the README).
    /// </summary>
    /// <remarks>
    /// A relative path is resolved once, against <see cref="Directory.GetCurrentDirectory"/>, when
    /// the provider is created; the log file therefore never moves if the process later changes
    /// its working directory. Only <c>.txt</c> and <c>.log</c> extensions are accepted, compared
    /// case-insensitively.
    /// </remarks>
    public string? FileName { get; set; }

    /// <summary>
    /// Lowest level this provider writes, independently of any <c>ILoggingBuilder</c> filter.
    /// Defaults to <see cref="LogLevel.Trace"/> (write everything the factory hands over); set it
    /// to <see cref="LogLevel.None"/> to disable the provider entirely.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    /// <summary>
    /// Whether active <c>BeginScope</c> state is written as part of each entry. Defaults to
    /// <see langword="true"/>: in a file — read long after the fact — the scope is usually the
    /// only thing tying an entry to the request that produced it.
    /// </summary>
    public bool IncludeScopes { get; set; } = true;
}
