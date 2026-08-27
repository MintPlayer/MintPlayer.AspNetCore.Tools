namespace MintPlayer.AspNetCore.Tools.Tests.Logging;

/// <summary>
/// A <see cref="TimeProvider"/> pinned to one instant, so the timestamp the file logger writes can
/// be asserted literally.
/// </summary>
/// <remarks>
/// Hand-rolled rather than taken from Microsoft.Extensions.TimeProvider.Testing: one overridden
/// member is not worth a package reference, and the fixed instant is deliberately off-UTC
/// (+02:00) so a test would catch the logger writing local time instead of UTC.
/// </remarks>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>2026-08-27T09:15:42.1234567Z, expressed in a +02:00 offset.</summary>
    public static readonly DateTimeOffset DefaultNow =
        new DateTimeOffset(2026, 8, 27, 11, 15, 42, 123, TimeSpan.FromHours(2)).AddTicks(4567);

    /// <summary>The round-trip UTC rendering the logger is expected to write for <see cref="DefaultNow"/>.</summary>
    public const string DefaultNowUtcRoundTrip = "2026-08-27T09:15:42.1234567Z";

    public FixedTimeProvider() : this(DefaultNow) { }

    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
