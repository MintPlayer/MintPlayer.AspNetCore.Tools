using MintPlayer.AspNetCore.SitemapXml.Abstractions;
using Xunit;
using SitemapXmlService = MintPlayer.AspNetCore.SitemapXml.DependencyInjection.SitemapXml;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml;

/// <summary>
/// Direct tests of the internal <c>SitemapXml</c> service, reachable via
/// <c>InternalsVisibleTo</c>. Going through <c>AddSitemapXml()</c> + a built provider would turn
/// every arithmetic boundary into an integration test.
/// </summary>
public class SitemapXmlServiceTests
{
    private static ISitemapXml CreateService() => new SitemapXmlService();

    // ── PageCount ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(20, 10, 2)]
    [InlineData(21, 10, 3)]
    [InlineData(9, 3, 3)]
    [InlineData(10, 3, 4)]
    [InlineData(int.MaxValue, int.MaxValue, 1)]
    public void PageCount_RoundsUp(int total, int perPage, int expected)
    {
        Assert.Equal(expected, CreateService().PageCount(total, perPage));
    }

    /// <summary>
    /// Pins PRD defect D-S6: <c>(0 - 1) / perPage</c> truncates toward zero, so zero items claim
    /// one page. A caller looping <c>1..PageCount</c> then renders an empty sitemap and a sitemap
    /// index referencing it.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(1000)]
    public void PageCount_ZeroTotal_ReturnsOne_KnownBug(int perPage)
    {
        Assert.Equal(1, CreateService().PageCount(0, perPage));
    }

    /// <summary>
    /// A wrinkle D-S6 does not mention, found while writing this suite: at <c>perPage == 1</c> the
    /// same expression yields <c>0</c>, because <c>-1 / 1</c> is <c>-1</c> rather than <c>0</c>. So
    /// the zero-item answer is not merely wrong, it is inconsistent — one page for every page size
    /// except the smallest one.
    /// </summary>
    [Fact]
    public void PageCount_ZeroTotalAndPerPageOne_ReturnsZero_KnownBug()
    {
        Assert.Equal(0, CreateService().PageCount(0, 1));
    }

    /// <summary>
    /// Pins PRD defect D-S7: no argument validation at all, so a misconfigured page size takes the
    /// request down with a <see cref="DivideByZeroException"/> rather than an
    /// <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    [Fact]
    public void PageCount_ZeroPerPage_ThrowsDivideByZero_KnownBug()
    {
        Assert.Throws<DivideByZeroException>(() => CreateService().PageCount(100, 0));
    }

    /// <summary>Second half of D-S7: negative inputs produce nonsense instead of an error.</summary>
    [Theory]
    [InlineData(10, -3, -2)]
    [InlineData(-5, 3, -1)]
    [InlineData(-5, -5, 2)]
    public void PageCount_NegativeInputs_ReturnNonsense_KnownBug(int total, int perPage, int expected)
    {
        Assert.Equal(expected, CreateService().PageCount(total, perPage));
    }

    // ── GetSitemapIndex ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetSitemapIndex_EmptySource_ReturnsNoSitemaps()
    {
        var result = CreateService().GetSitemapIndex(Array.Empty<TimestampedItem>(), 10, (perPage, page) => $"/s/{page}");

        Assert.Empty(result);
    }

    /// <summary>
    /// The empty case is the one place the D-S6 <c>PageCount(0, n) == 1</c> bug is masked: the
    /// <c>items.Any()</c> guard short-circuits before <c>PageCount</c> is ever called.
    /// </summary>
    [Fact]
    public void GetSitemapIndex_EmptySource_NeverCallsUrlFunc()
    {
        var calls = 0;

        var result = CreateService()
            .GetSitemapIndex(Array.Empty<TimestampedItem>(), 10, (perPage, page) => { calls++; return "/s"; })
            .ToList();

        Assert.Empty(result);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(5, 2, 3)]
    public void GetSitemapIndex_ProducesOneSitemapPerPage(int itemCount, int perPage, int expectedPages)
    {
        var result = CreateService()
            .GetSitemapIndex(TimestampedItem.Sequence(itemCount), perPage, (pp, page) => $"/s/{page}")
            .ToList();

        Assert.Equal(expectedPages, result.Count);
    }

    [Fact]
    public void GetSitemapIndex_UsesTheUrlFuncResultAsLoc()
    {
        var result = CreateService()
            .GetSitemapIndex(TimestampedItem.Sequence(5), 2, (pp, page) => $"https://example.org/sitemap-{page}.xml")
            .ToList();

        Assert.Equal(
            ["https://example.org/sitemap-1.xml", "https://example.org/sitemap-2.xml", "https://example.org/sitemap-3.xml"],
            result.Select(s => s.Loc).ToArray());
    }

    /// <summary>
    /// The argument order of <c>urlFunc</c> is <c>(perPage, page)</c> — the page number comes
    /// SECOND, even though the parameter is documented as "function to compute the url" and every
    /// caller thinks in pages.
    /// </summary>
    /// <remarks>
    /// Pinned by asserting the captured arguments, not by reading the source, because the order
    /// reads backwards and is the kind of thing a future reader "fixes". Swapping it silently
    /// changes every existing consumer's sitemap URLs, so it must break a test if touched.
    /// </remarks>
    [Fact]
    public void GetSitemapIndex_CallsUrlFuncWithPerPageFirstAndPageSecond()
    {
        var captured = new List<(int First, int Second)>();

        CreateService()
            .GetSitemapIndex(TimestampedItem.Sequence(5), 2, (first, second) =>
            {
                captured.Add((first, second));
                return "/s";
            })
            .ToList();

        Assert.Equal([(2, 1), (2, 2), (2, 3)], captured);
    }

    /// <summary>
    /// Page numbering is one-based: <c>Enumerable.Range(1, pages)</c>, and the slice is
    /// <c>Skip((page - 1) * perPage)</c>.
    /// </summary>
    [Fact]
    public void GetSitemapIndex_PageNumbersAreOneBased()
    {
        var captured = new List<int>();

        CreateService()
            .GetSitemapIndex(TimestampedItem.Sequence(5), 2, (pp, page) => { captured.Add(page); return "/s"; })
            .ToList();

        Assert.Equal([1, 2, 3], captured);
    }

    /// <summary>
    /// <c>LastMod</c> is the newest <c>DateUpdate</c> within the page's own slice, not the whole
    /// source — that is what makes a paged sitemap index useful to a crawler.
    /// </summary>
    [Fact]
    public void GetSitemapIndex_LastModIsTheMaxDateUpdateOfThatPagesSlice()
    {
        var result = CreateService()
            .GetSitemapIndex(TimestampedItem.Sequence(5), 2, (pp, page) => $"/s/{page}")
            .ToList();

        // Sequence(5) is dated 2024-01-01 … 2024-01-05; pages of 2 slice it 1-2, 3-4, 5.
        Assert.Equal(new DateTime(2024, 1, 2), result[0].LastMod);
        Assert.Equal(new DateTime(2024, 1, 4), result[1].LastMod);
        Assert.Equal(new DateTime(2024, 1, 5), result[2].LastMod);
    }

    [Fact]
    public void GetSitemapIndex_LastModIgnoresSourceOrdering()
    {
        var items = new[]
        {
            new TimestampedItem(new DateTime(2024, 1, 3)),
            new TimestampedItem(new DateTime(2024, 1, 9)),
            new TimestampedItem(new DateTime(2024, 1, 1)),
        };

        var result = CreateService().GetSitemapIndex(items, 10, (pp, page) => "/s").ToList();

        Assert.Equal(new DateTime(2024, 1, 9), Assert.Single(result).LastMod);
    }

    /// <summary>
    /// The result is a lazy <c>Select</c> over <c>Enumerable.Range</c>, so nothing per-page happens
    /// until the caller enumerates — but the <c>Any()</c> and <c>Count()</c> probes have ALREADY
    /// run by then. Half-lazy is the worst of both: the caller cannot predict when the source is
    /// touched.
    /// </summary>
    [Fact]
    public void GetSitemapIndex_IsLazyPerPageButEagerOnTheTwoProbes()
    {
        var source = new CountingEnumerable<TimestampedItem>(TimestampedItem.Sequence(5));
        var calls = 0;

        var result = CreateService().GetSitemapIndex(source, 2, (pp, page) => { calls++; return "/s"; });

        Assert.Equal(2, source.EnumerationCount);
        Assert.Equal(0, calls);

        result.ToList();

        Assert.Equal(3, calls);
    }

    /// <summary>
    /// Pins PRD defect D-S8: the source is walked <c>2 + pageCount</c> times — <c>Any()</c>,
    /// <c>Count()</c>, and one <c>Skip/Take/Max</c> per page. On an <c>IQueryable</c> that is
    /// N+2 database round trips per sitemap index.
    /// </summary>
    [Theory]
    [InlineData(5, 2, 5)]
    [InlineData(100, 10, 12)]
    [InlineData(1, 10, 3)]
    public void GetSitemapIndex_EnumeratesTheSourceTwicePlusOncePerPage_KnownBug(int itemCount, int perPage, int expectedEnumerations)
    {
        var source = new CountingEnumerable<TimestampedItem>(TimestampedItem.Sequence(itemCount));

        CreateService().GetSitemapIndex(source, perPage, (pp, page) => "/s").ToList();

        Assert.Equal(expectedEnumerations, source.EnumerationCount);
    }

    /// <summary>
    /// Second half of D-S8: because the returned sequence is lazy, every re-enumeration by the
    /// caller pays the per-page cost again.
    /// </summary>
    [Fact]
    public void GetSitemapIndex_ReEnumeratingTheResult_WalksTheSourceAgain_KnownBug()
    {
        var source = new CountingEnumerable<TimestampedItem>(TimestampedItem.Sequence(5));

        var result = CreateService().GetSitemapIndex(source, 2, (pp, page) => "/s");
        result.ToList();
        result.ToList();

        Assert.Equal(8, source.EnumerationCount);
    }

    /// <summary>
    /// Third half of D-S8, and the one that is a hard failure rather than a cost: a source that can
    /// only be walked once — a stream, a data reader, an already-consumed iterator — throws before
    /// the method even returns.
    /// </summary>
    [Fact]
    public void GetSitemapIndex_SingleUseSource_ThrowsAtCallTime_KnownBug()
    {
        var source = new SingleUseEnumerable<TimestampedItem>(TimestampedItem.Sequence(5));

        Assert.Throws<InvalidOperationException>(() => CreateService().GetSitemapIndex(source, 2, (pp, page) => "/s"));
    }

    /// <summary>D-S7 reaching the caller through <c>GetSitemapIndex</c>.</summary>
    [Fact]
    public void GetSitemapIndex_ZeroPerPage_ThrowsDivideByZero_KnownBug()
    {
        Assert.Throws<DivideByZeroException>(
            () => CreateService().GetSitemapIndex(TimestampedItem.Sequence(5), 0, (pp, page) => "/s"));
    }

    [Fact]
    public void GetSitemapIndex_NullItems_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => CreateService().GetSitemapIndex<TimestampedItem>(null!, 10, (pp, page) => "/s"));
    }

    /// <summary>
    /// A null <c>urlFunc</c> is not guarded either, and because the projection is lazy the
    /// <see cref="NullReferenceException"/> surfaces at the caller's <c>foreach</c> — far from the
    /// call that was wrong.
    /// </summary>
    [Fact]
    public void GetSitemapIndex_NullUrlFunc_ThrowsOnlyWhenEnumerated_KnownBug()
    {
        var result = CreateService().GetSitemapIndex(TimestampedItem.Sequence(5), 2, null!);

        Assert.Throws<NullReferenceException>(() => result.ToList());
    }

    [Fact]
    public void GetSitemapIndex_LastPage_MayBePartial()
    {
        var result = CreateService()
            .GetSitemapIndex(TimestampedItem.Sequence(7), 3, (pp, page) => $"/s/{page}")
            .ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(new DateTime(2024, 1, 7), result[2].LastMod);
    }

    [Fact]
    public void Service_ImplementsThePublicAbstraction()
    {
        Assert.IsAssignableFrom<ISitemapXml>(new SitemapXmlService());
    }
}
