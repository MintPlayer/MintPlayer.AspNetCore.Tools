using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MintPlayer.AspNetCore.LoggerProviders;

/// <summary>Registration helpers for this package's logger providers.</summary>
/// <remarks>
/// Not named <c>LoggerExtensions</c>: <c>Microsoft.Extensions.Logging</c> already publishes a type
/// by that name and is an implicit global using in a Web SDK project, so the short name would be an
/// ambiguity error for any consumer that also imports this namespace.
/// </remarks>
public static class LoggerProviderExtensions
{
    /// <summary>
    /// Registers <typeparamref name="T"/> as an <see cref="ILoggerProvider"/>, at most once.
    /// </summary>
    /// <remarks>
    /// A logger provider registered twice makes the factory write every entry twice, with nothing
    /// to explain it — and registering twice is easy, because a library's own <c>AddX()</c> and the
    /// app's explicit call cannot see each other. So this deduplicates on <typeparamref name="T"/>
    /// (the name says so, and it no longer shadows the framework's own
    /// <c>ILoggingBuilder.AddProvider(ILoggerProvider)</c>).
    /// </remarks>
    public static ILoggingBuilder TryAddProvider<T>(this ILoggingBuilder builder, Func<IServiceProvider, T> factory)
        where T : class, ILoggerProvider
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, T>(factory));
        return builder;
    }

    /// <summary>Adds the file logger provider. The filename must be configured (see the overload below).</summary>
    public static ILoggingBuilder AddFileProvider(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FileLoggerOptions>, FileLoggerOptionsValidator>());

        return builder.TryAddProvider(static provider => ActivatorUtilities.CreateInstance<LoggerFileProvider>(provider));
    }

    /// <summary>Adds the file logger provider and configures it, the way every other <c>Add*</c> does.</summary>
    public static ILoggingBuilder AddFileProvider(this ILoggingBuilder builder, Action<FileLoggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.AddFileProvider();
        builder.Services.Configure(configure);
        return builder;
    }
}
