namespace MintPlayer.AspNetCore.OpenSearch.Extensions;

/// <summary>
/// Internal on purpose: SitemapXml exports a <c>public</c> extension of the same name on the same
/// receiver type, so a public one here makes every call ambiguous in an app that installs both
/// packages with implicit usings on.
/// </summary>
internal static class StringExtensions
{
    /// <summary>Treats a blank value as absent, so a whitespace-only option falls back to its default.</summary>
    internal static string? NullIfEmpty(this string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
