using Microsoft.Extensions.Options;

namespace MintPlayer.AspNetCore.LoggerProviders;

internal class FileLogger : ILogger
{
    private readonly IOptions<FileLoggerOptions> fileLoggerOptions;
    public FileLogger(IOptions<FileLoggerOptions> fileLoggerOptions)
    {
        this.fileLoggerOptions = fileLoggerOptions;
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
        var path = fileLoggerOptions?.Value?.FileName ?? "Log.txt";
        ValidateLogFilename(path);

        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write);
        using var writer = new StreamWriter(fs);

        writer.WriteLine(formatter(state, exception));
        writer.WriteLine(string.Empty);
    }

    private void ValidateLogFilename(string filename)
    {
        var extension = Path.GetExtension(filename);
        string[] allowedExts = [".txt", ".log"];
        if (!allowedExts.Contains(extension))
        {
            throw new InvalidOperationException($"Logger file extension {extension} is not allowed. Only {string.Join(", ", allowedExts)} are allowed");
        }
    }
}