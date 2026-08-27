using Microsoft.Extensions.Options;

namespace MintPlayer.AspNetCore.LoggerProviders;

/// <summary>
/// Turns a misconfigured filename into an <see cref="OptionsValidationException"/> the first time
/// the options are read — i.e. while the host is wiring logging up — instead of into an exception
/// raised out of some unrelated application code's <c>LogInformation</c> call much later.
/// </summary>
internal sealed class FileLoggerOptionsValidator : IValidateOptions<FileLoggerOptions>
{
    public ValidateOptionsResult Validate(string? name, FileLoggerOptions options)
    {
        try
        {
            FileLogWriter.ResolveFilePath(options?.FileName);
            return ValidateOptionsResult.Success;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // Path.GetFullPath is the ArgumentException/NotSupportedException source; its message is
            // the useful part either way.
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
