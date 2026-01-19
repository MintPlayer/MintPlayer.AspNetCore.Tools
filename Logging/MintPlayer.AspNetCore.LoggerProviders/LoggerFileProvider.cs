using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.AspNetCore.LoggerProviders;

internal partial class LoggerFileProvider : ILoggerProvider
{
    [Inject] private readonly IServiceProvider serviceProvider;

    public ILogger CreateLogger(string categoryName)
    {
        var logger = ActivatorUtilities.CreateInstance<FileLogger>(serviceProvider);
        return logger;
    }

    public void Dispose()
    {
    }
}
