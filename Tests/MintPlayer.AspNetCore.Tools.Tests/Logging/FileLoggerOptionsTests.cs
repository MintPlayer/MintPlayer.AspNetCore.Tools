using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    /// <c>FileName</c> is declared as non-nullable <c>string</c> under <c>Nullable=enable</c> with
    /// no initialiser, so a freshly constructed instance holds <c>null</c> and the signature lies
    /// to consumers. <see cref="FileLogger"/> defends against it with <c>?.</c> and a
    /// <c>"Log.txt"</c> fallback, which is what makes this survivable rather than a crash.
    /// </summary>
    /// <remarks>
    /// Not in the PRD register — the equivalent nullability defect there (D-S27) covers only the
    /// OpenSearch and SitemapXml DTOs. Reported as a new finding rather than named as a known bug.
    /// </remarks>
    [Fact]
    public void FileName_DefaultsToNull_DespiteBeingDeclaredNonNullable()
    {
        var options = new FileLoggerOptions();

        Assert.Null(options.FileName);
    }

    /// <summary>
    /// The options class is public and settable, which is the whole reason it is separate from the
    /// internal logger. Guarded by reflection because narrowing either would break consumers
    /// silently at compile time only for them, not here.
    /// </summary>
    [Fact]
    public void Options_ExposeAPublicSettableFileName()
    {
        var property = typeof(FileLoggerOptions).GetProperty(nameof(FileLoggerOptions.FileName));

        Assert.NotNull(property);
        Assert.True(property!.GetMethod!.IsPublic);
        Assert.True(property.SetMethod!.IsPublic);
        Assert.Equal(typeof(string), property.PropertyType);
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
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FileLogger:FileName"] = "FromConfig.log" })
            .Build();
        var services = new ServiceCollection();
        services.AddOptions<FileLoggerOptions>().Bind(configuration.GetSection("FileLogger"));
        using var sp = services.BuildServiceProvider();

        Assert.Equal("FromConfig.log", sp.GetRequiredService<IOptions<FileLoggerOptions>>().Value.FileName);
    }

    /// <summary>
    /// Multiple <c>Configure</c> callbacks run in registration order, so the last one wins. Pinned
    /// because the logger re-reads <c>Options.Value</c> on every write (D-M15), which makes the
    /// resolved value — not just the registration — observable at runtime.
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
}
