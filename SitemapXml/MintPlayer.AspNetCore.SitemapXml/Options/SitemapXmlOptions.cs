namespace MintPlayer.AspNetCore.SitemapXml.Options;

/// <summary>Configuration for <c>AddSitemapXml</c>.</summary>
public class SitemapXmlOptions
{
    /// <summary>
    /// Absolute path at which an XSL stylesheet for the sitemap is hosted, so a human opening the
    /// sitemap in a browser sees a rendered table rather than raw XML. The same value is used
    /// twice: as the route pattern registered by <c>MapDefaultSitemapXmlStylesheet</c>, and as the
    /// <c>href</c> of the <c>xml-stylesheet</c> processing instruction the output formatter writes
    /// into every sitemap response.
    /// </summary>
    /// <remarks>
    /// Must start with <c>'/'</c> and must not contain a double quote; both are validated at
    /// startup. <see langword="null"/> or empty means no stylesheet is referenced: no processing
    /// instruction is written, and <c>MapDefaultSitemapXmlStylesheet</c> falls back to serving the
    /// built-in stylesheet at <c>/sitemap.xsl</c> if it is called at all.
    /// </remarks>
    public string? StylesheetUrl { get; set; }
}
