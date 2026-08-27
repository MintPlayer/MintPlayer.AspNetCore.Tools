namespace MintPlayer.AspNetCore.SitemapXml.Extensions;

/// <remarks>
/// <c>internal</c> on purpose. <c>MintPlayer.AspNetCore.OpenSearch</c> exports an identically
/// named extension on the same receiver type, so two <c>public</c> copies made
/// <c>value.NullIfEmpty()</c> an ambiguous call in any app that installed both packages with
/// <c>ImplicitUsings</c> on.
/// </remarks>
internal static class StringExtensions
{
    /// <summary>Treats a blank string as "not configured".</summary>
    internal static string? NullIfEmpty(this string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
