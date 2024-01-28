namespace MintPlayer.AspNetCore.LoggerProviders;

public static class LoggerExtensions
{
    public static ILoggingBuilder AddProvider<T>(this ILoggingBuilder builder, Func<IServiceProvider, T> factory)
        where T : class, ILoggerProvider
    {
        builder.Services.AddSingleton<ILoggerProvider, T>(factory);
        return builder;
    }

    public static ILoggingBuilder AddFileProvider(this ILoggingBuilder builder)
    {
        return builder.AddProvider<LoggerFileProvider>((provider) =>
        {
            var loggerProvider = ActivatorUtilities.CreateInstance<LoggerFileProvider>(provider);
            return loggerProvider;
        });
    }
}
