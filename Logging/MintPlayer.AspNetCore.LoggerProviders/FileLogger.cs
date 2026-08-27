using System.Globalization;
using System.Text;

namespace MintPlayer.AspNetCore.LoggerProviders;

/// <summary>
/// Writes one category's entries to the provider's log file.
/// </summary>
/// <remarks>
/// <para>The entry format is one header line, optionally followed by the exception's own lines, then
/// a blank separator line (the blank line is what delimits multi-line entries):</para>
/// <code>
/// &lt;timestamp&gt; [&lt;Level&gt;] &lt;Category&gt;[&lt;EventId&gt;[:&lt;EventName&gt;]] [=&gt; &lt;scope&gt;]* &lt;message&gt;
/// &lt;exception.ToString()&gt;
/// </code>
/// <para>for example</para>
/// <code>
/// 2026-08-27T09:15:42.1234567Z [Error] MintPlayer.Sample.Worker[4711:JobFailed] =&gt; RequestId:abc request failed
/// System.InvalidOperationException: the real cause
///    at MintPlayer.Sample.Worker.Run()
/// </code>
/// <para>The timestamp is UTC in round-trip ("O") form and every field is formatted with
/// <see cref="CultureInfo.InvariantCulture"/>, so the file reads identically whatever the host's
/// culture and time zone. The category and the event id are omitted when empty (respectively
/// zero and unnamed), and the scopes when <see cref="FileLoggerOptions.IncludeScopes"/> is off.</para>
/// </remarks>
internal sealed class FileLogger : ILogger
{
    private readonly string categoryName;
    private readonly FileLogWriter writer;
    private readonly TimeProvider timeProvider;

    public FileLogger(string categoryName, FileLogWriter writer, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(writer);

        this.categoryName = categoryName ?? string.Empty;
        this.writer = writer;
        this.timeProvider = timeProvider ?? TimeProvider.System;

        // Its own provider by default, so BeginScope is functional even for a logger built outside
        // a host; a hosted provider replaces this with the factory's shared one.
        ScopeProvider = new LoggerExternalScopeProvider();
    }

    /// <summary>Set by <see cref="LoggerFileProvider"/> from <c>ISupportExternalScope</c>.</summary>
    public IExternalScopeProvider ScopeProvider { get; set; }

    /// <summary>
    /// Never returns <see langword="null"/>: a caller that dereferences the result must not have to
    /// know whether this provider cares about scopes. When it does not, the state is not even
    /// captured and a shared do-nothing scope is handed back.
    /// </summary>
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => writer.IncludeScopes ? ScopeProvider.Push(state) : NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel)
        // Only the six real levels can be enabled: LogLevel.None means "no logging at all", and an
        // out-of-range value is not a level. A MinimumLevel of None therefore disables everything.
        => logLevel >= writer.MinimumLevel && logLevel <= LogLevel.Critical;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        ArgumentNullException.ThrowIfNull(formatter);

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null) return;

        writer.Write(FormatEntry(logLevel, eventId, message, exception));
    }

    private string FormatEntry(LogLevel logLevel, EventId eventId, string message, Exception? exception)
    {
        var builder = new StringBuilder();

        builder.Append(timeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        builder.Append(" [").Append(logLevel.ToString()).Append(']');

        if (categoryName.Length > 0)
        {
            builder.Append(' ').Append(categoryName);
        }

        if (eventId.Id != 0 || eventId.Name is not null)
        {
            if (categoryName.Length == 0) builder.Append(' ');
            builder.Append('[').Append(eventId.Id.ToString(CultureInfo.InvariantCulture));
            if (eventId.Name is not null) builder.Append(':').Append(eventId.Name);
            builder.Append(']');
        }

        if (writer.IncludeScopes)
        {
            ScopeProvider.ForEachScope(
                static (scope, state) => state.Append(" => ").Append(Convert.ToString(scope, CultureInfo.InvariantCulture)),
                builder);
        }

        if (message.Length > 0)
        {
            builder.Append(' ').Append(message);
        }

        builder.AppendLine();

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        builder.AppendLine();

        return builder.ToString();
    }
}
