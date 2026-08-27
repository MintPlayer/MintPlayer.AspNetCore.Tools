using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace MintPlayer.AspNetCore.LoggerProviders;

/// <summary>
/// Provides <see cref="FileLogger"/>s writing to one log file, one cached logger per category.
/// </summary>
/// <remarks>
/// The configured filename is resolved and validated here, in the constructor, so a bad filename
/// fails while logging is being wired up rather than on a later write.
/// </remarks>
internal sealed class LoggerFileProvider : ILoggerProvider, ISupportExternalScope
{
    /// <summary>
    /// A logger per category. The category is part of every written entry, so it cannot be
    /// discarded; caching keeps a host with hundreds of categories from allocating a logger per
    /// <c>CreateLogger</c> call.
    /// </summary>
    private readonly ConcurrentDictionary<string, FileLogger> loggers = new(StringComparer.Ordinal);

    private readonly FileLogWriter writer;
    private readonly TimeProvider timeProvider;
    private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();

    public LoggerFileProvider(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        writer = new FileLogWriter(serviceProvider.GetRequiredService<IOptions<FileLoggerOptions>>());
        // Optional: a host that registers a TimeProvider (a test, most often) controls the
        // timestamps; everyone else gets the wall clock.
        timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
    }

    /// <summary>A null or empty category is written as no category at all rather than rejected.</summary>
    public ILogger CreateLogger(string categoryName)
        => loggers.GetOrAdd(categoryName ?? string.Empty, name => new FileLogger(name, writer, timeProvider) { ScopeProvider = scopeProvider });

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeProvider);

        this.scopeProvider = scopeProvider;
        foreach (var logger in loggers.Values)
        {
            logger.ScopeProvider = scopeProvider;
        }
    }

    /// <summary>
    /// Releases the cached loggers. There is deliberately nothing to flush: <see cref="FileLogWriter"/>
    /// buffers nothing and holds no handle, so an entry is on disk before <c>Log</c> returns and no
    /// entry can be lost by skipping disposal. Idempotent.
    /// </summary>
    public void Dispose() => loggers.Clear();
}
