using System.Collections;
using MintPlayer.Timestamps;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

/// <summary>
/// The minimum a caller of <c>GetSitemapIndex</c> has to satisfy: <c>T : IUpdateTimestamp</c>.
/// </summary>
internal sealed class TimestampedItem : IUpdateTimestamp
{
    public TimestampedItem(DateTime dateUpdate) => DateUpdate = dateUpdate;

    public DateTime DateUpdate { get; set; }

    /// <summary>
    /// Items dated one day apart from 2024-01-01, so a page's <c>Max</c> is predictable from the
    /// index of its last item.
    /// </summary>
    public static TimestampedItem[] Sequence(int count) =>
        [.. Enumerable.Range(0, count).Select(offset => new TimestampedItem(new DateTime(2024, 1, 1).AddDays(offset)))];
}

/// <summary>
/// Counts how many times the source was enumerated from the top. This is the instrument for PRD
/// defect D-S8 — on an <c>IQueryable</c> each of those enumerations is a database round trip.
/// </summary>
/// <remarks>
/// Deliberately NOT an <see cref="ICollection{T}"/>: <c>Enumerable.Count()</c> short-circuits on
/// one, which would hide half the cost being measured.
/// </remarks>
internal sealed class CountingEnumerable<T>(IEnumerable<T> source) : IEnumerable<T>
{
    public int EnumerationCount { get; private set; }

    public IEnumerator<T> GetEnumerator()
    {
        EnumerationCount++;
        return source.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A source that can only be walked once, like a network stream or a
/// <see cref="System.Data.IDataReader"/>-backed sequence. Anything enumerating it twice fails.
/// </summary>
internal sealed class SingleUseEnumerable<T>(IEnumerable<T> source) : IEnumerable<T>
{
    private bool consumed;

    public IEnumerator<T> GetEnumerator()
    {
        if (consumed)
            throw new InvalidOperationException("This sequence can only be enumerated once.");

        consumed = true;
        return source.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
