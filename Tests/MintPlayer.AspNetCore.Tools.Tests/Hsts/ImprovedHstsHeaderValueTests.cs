using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MintPlayer.AspNetCore.Hsts;
using MintPlayer.AspNetCore.Tools.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Hsts;

/// <summary>
/// Covers the <c>Strict-Transport-Security</c> value, which is assembled once in the constructor.
/// </summary>
public class ImprovedHstsHeaderValueTests
{
    /// <summary>Runs a request through the middleware and returns the header it wrote.</summary>
    private static async Task<string?> HeaderFor(HstsOptions options)
    {
        var middleware = new ImprovedHstsMiddleware(
            _ => Task.CompletedTask,
            Options.Create(options),
            NullLoggerFactory.Instance);

        var (context, response) = RecordingResponseFeature.CreateContext();
        context.Request.Scheme = "https";

        await middleware.Invoke(context);
        await response.FireOnStartingAsync();

        return response.Headers.StrictTransportSecurity;
    }

    private static HstsOptions Options_(TimeSpan maxAge, bool includeSubDomains = false, bool preload = false)
        => new() { MaxAge = maxAge, IncludeSubDomains = includeSubDomains, Preload = preload, ExcludedHosts = { } };

    [Fact]
    public async Task MaxAge30Days_ProducesMaxAge2592000()
        => Assert.Equal("max-age=2592000", await HeaderFor(Options_(TimeSpan.FromDays(30))));

    [Fact]
    public async Task MaxAgeOneYear_ProducesMaxAge31536000()
        => Assert.Equal("max-age=31536000", await HeaderFor(Options_(TimeSpan.FromDays(365))));

    [Fact]
    public async Task MaxAgeZero_ProducesMaxAge0()
        => Assert.Equal("max-age=0", await HeaderFor(Options_(TimeSpan.Zero)));

    /// <summary>Fractional seconds are floored, not rounded.</summary>
    [Fact]
    public async Task MaxAgeWithFractionalSeconds_IsFloored()
        => Assert.Equal("max-age=1", await HeaderFor(Options_(TimeSpan.FromSeconds(1.9))));

    [Fact]
    public async Task IncludeSubDomains_AppendsDirective()
        => Assert.Equal(
            "max-age=2592000; includeSubDomains",
            await HeaderFor(Options_(TimeSpan.FromDays(30), includeSubDomains: true)));

    [Fact]
    public async Task Preload_AppendsDirectiveWithoutIncludeSubDomains()
        => Assert.Equal(
            "max-age=2592000; preload",
            await HeaderFor(Options_(TimeSpan.FromDays(30), preload: true)));

    /// <summary>Directive order is fixed: max-age, then includeSubDomains, then preload.</summary>
    [Fact]
    public async Task IncludeSubDomainsAndPreload_AppendInFixedOrder()
        => Assert.Equal(
            "max-age=31536000; includeSubDomains; preload",
            await HeaderFor(Options_(TimeSpan.FromDays(365), includeSubDomains: true, preload: true)));

    /// <summary>
    /// The numeric part must be invariant regardless of the ambient culture.
    /// </summary>
    /// <remarks>
    /// CI runs on ubuntu-latest where ICU supplies culture data, while development here is under
    /// nl-BE. The library passes <see cref="CultureInfo.InvariantCulture"/> explicitly; this test
    /// is what catches its removal, and it must set the culture itself rather than relying on
    /// whatever the host happens to default to.
    /// </remarks>
    [Theory]
    [InlineData("nl-BE")]
    [InlineData("fa-IR")]
    [InlineData("ar-SA")]
    public async Task MaxAge_UsesInvariantCulture_UnderAnyAmbientCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.Equal("max-age=2592000", await HeaderFor(Options_(TimeSpan.FromDays(30))));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
