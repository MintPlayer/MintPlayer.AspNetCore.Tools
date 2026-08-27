using MintPlayer.AspNetCore.SitemapXml.Extensions;

namespace MintPlayer.AspNetCore.SitemapXml.Options;

/// <summary>
/// Resolves <see cref="SitemapXmlOptions.StylesheetUrl"/> to the single form both of its consumers
/// need: a validated absolute path, or <see langword="null"/> when no stylesheet is configured.
/// </summary>
/// <remarks>
/// <para>
/// The configured value is used simultaneously as a route pattern (by
/// <c>MapDefaultSitemapXmlStylesheet</c>) and as an <c>href</c> inside an <c>xml-stylesheet</c>
/// processing instruction (by the output formatter), so both requirements are enforced in one
/// place. It has to be rooted, or the route and the href disagree — the href would resolve
/// against the depth of the requesting path. And it must not contain the double quote that
/// delimits the href: <see cref="System.Xml.XmlWriter"/> escapes nothing inside a processing
/// instruction, so a quote closes the pseudo-attribute and injects whatever follows.
/// </para>
/// <para>
/// Deliberately a plain string test and NOT <c>Uri.TryCreate</c>: for a leading-slash string
/// <c>Uri.TryCreate</c> answers <see langword="false"/> on Windows and <see langword="true"/> on
/// Linux (resolving it as <c>file:///…</c>), which makes any validation built on it pass locally
/// and fail in CI.
/// </para>
/// </remarks>
internal static class StylesheetUrl
{
    public static string? Resolve(string? stylesheetUrl)
    {
        if (stylesheetUrl.NullIfEmpty() is null)
            return null;

        if (stylesheetUrl!.Contains('"'))
            throw new InvalidOperationException(
                $"{nameof(SitemapXmlOptions)}.{nameof(SitemapXmlOptions.StylesheetUrl)} must not contain a double quote, because it is written as the href of an xml-stylesheet processing instruction. Configured value: '{stylesheetUrl}'.");

        if (!stylesheetUrl.StartsWith('/'))
            throw new InvalidOperationException(
                $"{nameof(SitemapXmlOptions)}.{nameof(SitemapXmlOptions.StylesheetUrl)} must be an absolute path starting with '/', because it is used both as a route pattern and as an href. Configured value: '{stylesheetUrl}'.");

        return stylesheetUrl;
    }
}
