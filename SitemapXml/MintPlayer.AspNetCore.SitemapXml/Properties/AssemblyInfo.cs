using System.Runtime.CompilerServices;

// The SitemapXml service (PageCount / GetSitemapIndex) and the output formatter are
// internal. Reaching them only through AddSitemapXml() + a built provider would make
// every boundary case an integration test.
[assembly: InternalsVisibleTo("MintPlayer.AspNetCore.Tools.Tests")]
