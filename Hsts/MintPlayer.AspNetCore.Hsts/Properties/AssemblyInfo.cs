using System.Runtime.CompilerServices;

// ImprovedHstsMiddleware is internal; without this its constructor guards, the
// excluded-host matching and the header-value assembly are only reachable through
// UseImprovedHsts(), which forces a host for what are otherwise fast unit tests.
[assembly: InternalsVisibleTo("MintPlayer.AspNetCore.Tools.Tests")]
