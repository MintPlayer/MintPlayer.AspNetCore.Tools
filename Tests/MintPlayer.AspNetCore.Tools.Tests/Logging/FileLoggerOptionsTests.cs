using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.LoggerProviders;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Logging;

public class FileLoggerOptionsTests
{
    [Fact]
    public void FileName_RoundTrips()
    {
        var options = new FileLoggerOptions { FileName = "Log.txt" };

        Assert.Equal("Log.txt", options.FileName);
    }

    /// <summary>
    /// D-M46: <c>FileName</c> has no default, and the signature now says so — it is annotated
    /// <c>string?</c> rather than declaring a non-nullable <c>string</c> that is in fact null.
    /// </summary>
    [Fact]
    public void FileName_IsAnnotatedNullable_AndDefaultsToNull()
    {
        var property = typeof(FileLoggerOptions).GetProperty(nameof(FileLoggerOptions.FileName))!;
        var nullability = new NullabilityInfoContext().Create(property);

        Assert.Equal(NullabilityState.Nullable, nullability.ReadState);
        Assert.Equal(NullabilityState.Nullable, nullability.WriteState);
        Assert.Null(new FileLoggerOptions().FileName);
    }

    /// <summary>
    /// Defaults: write everything the factory hands over, and include scopes — in a file, read long
    /// after the fact, the scope is usually the only thing tying an entry to its request.
    /// </summary>
    [Fact]
    public void Defaults_AreTraceAndScopesIncluded()
    {
        var options = new FileLoggerOptions();

        Assert.Equal(LogLevel.Trace, options.MinimumLevel);
        Assert.True(options.IncludeScopes);
    }

    /// <summary>
    /// The options class is public and settable, which is the whole reason it is separate from the
    /// internal logger. Guarded by reflection because narrowing any of them would break consumers
    /// silently at compile time only for them, not here.
    /// </summary>
    [Theory]
    [InlineData(nameof(FileLoggerOptions.FileName), typeof(string))]
    [InlineData(nameof(FileLoggerOptions.MinimumLevel), typeof(LogLevel))]
    [InlineData(nameof(FileLoggerOptions.IncludeScopes), typeof(bool))]
    public void Options_ExposePublicSettableProperties(string propertyName, Type propertyType)
    {
        var property = typeof(FileLoggerOptions).GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.True(property!.GetMethod!.IsPublic);
        Assert.True(property.SetMethod!.IsPublic);
        Assert.Equal(propertyType, property.PropertyType);
    }

    /// <summary>
    /// The options type must have a public parameterless constructor, or the options system cannot
    /// materialise it at all.
    /// </summary>
    [Fact]
    public void Options_HaveAPublicParameterlessConstructor()
    {
        Assert.NotNull(typeof(FileLoggerOptions).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void Configure_IsObservedThroughIOptions()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<FileLoggerOptions>(o => o.FileName = "Configured.log");
        using var sp = services.BuildServiceProvider();

        Assert.Equal("Configured.log", sp.GetRequiredService<IOptions<FileLoggerOptions>>().Value.FileName);
    }

    /// <summary>
    /// Binding from configuration is the realistic way a host sets the filename, and the property
    /// name is therefore part of the package's wire contract: renaming it silently stops
    /// <c>"Logging:File:FileName"</c> style configuration from taking effect.
    /// </summary>
    [Fact]
    public void Options_BindFromConfigurationByPropertyName()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileLogger:FileName"] = "FromConfig.log",
                ["FileLogger:MinimumLevel"] = "Warning",
                ["FileLogger:IncludeScopes"] = "false",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddOptions<FileLoggerOptions>().Bind(configuration.GetSection("FileLogger"));
        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<FileLoggerOptions>>().Value;
        Assert.Equal("FromConfig.log", options.FileName);
        Assert.Equal(LogLevel.Warning, options.MinimumLevel);
        Assert.False(options.IncludeScopes);
    }

    /// <summary>
    /// Multiple <c>Configure</c> callbacks run in registration order, so the last one wins. The
    /// resolved value is what the provider reads — once, when it is created (D-M15).
    /// </summary>
    [Fact]
    public void Configure_LastRegistrationWins()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<FileLoggerOptions>(o => o.FileName = "First.log");
        services.Configure<FileLoggerOptions>(o => o.FileName = "Second.log");
        using var sp = services.BuildServiceProvider();

        Assert.Equal("Second.log", sp.GetRequiredService<IOptions<FileLoggerOptions>>().Value.FileName);
    }

    #region Validation

    /// <summary>
    /// D-M15: the filename is validated by the options system, so a misconfiguration is reported
    /// once when the options are first read rather than on every write.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Log.json")]
    [InlineData("Log")]
    public void Validation_RejectsAMisconfiguredFileName(string? fileName)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFileProvider(o => o.FileName = fileName));
        using var sp = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<FileLoggerOptions>>().Value);
    }

    [Theory]
    [InlineData("Log.txt")]
    [InlineData("Log.LOG")]
    [InlineData("sub/dir/Log.log")]
    public void Validation_AcceptsAnAllowedFileName(string fileName)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFileProvider(o => o.FileName = fileName));
        using var sp = services.BuildServiceProvider();

        Assert.Equal(fileName, sp.GetRequiredService<IOptions<FileLoggerOptions>>().Value.FileName);
    }

    #endregion
}
