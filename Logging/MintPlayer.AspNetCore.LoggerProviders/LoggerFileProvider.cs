namespace MintPlayer.AspNetCore.LoggerProviders;

internal class LoggerFileProvider : ILoggerProvider
{
    private readonly IServiceProvider serviceProvider;
    public LoggerFileProvider(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public ILogger CreateLogger(string categoryName)
    {
        var logger = ActivatorUtilities.CreateInstance<FileLogger>(serviceProvider);
        return logger;
    }

    public void Dispose()
    {
    }
}
