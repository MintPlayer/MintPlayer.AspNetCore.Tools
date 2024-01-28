namespace MintPlayer.AspNetCore.LoggerProviders;

internal class FileLogger : ILogger
{
    public FileLogger()
    {
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return default!;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        using var fs = new FileStream("Log.txt", FileMode.Append, FileAccess.Write);
        using var writer = new StreamWriter(fs);

        writer.WriteLine(formatter(state, exception));
        writer.WriteLine(string.Empty);
    }
}