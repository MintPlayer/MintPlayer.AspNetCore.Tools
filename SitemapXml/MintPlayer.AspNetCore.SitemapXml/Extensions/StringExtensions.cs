namespace MintPlayer.AspNetCore.SitemapXml.Extensions;

public static class StringExtensions
{
    public static string? NullIfEmpty(this string? value)
        => value == string.Empty ? null : value;
}
