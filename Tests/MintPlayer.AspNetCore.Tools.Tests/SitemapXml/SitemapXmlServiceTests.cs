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
    /// Zero items is zero pages. Previously <c>(0 - 1) / perPage</c> truncated toward zero and
    /// answered <b>one</b> — so a caller looping <c>1..PageCount</c> rendered an empty sitemap and
    /// a sitemap index pointing at it (PRD defect D-S6).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(1000)]
    public void PageCount_ZeroTotal_IsZeroPages(int perPage)
    {
        Assert.Equal(0, CreateService().PageCount(0, perPage));
    }

    /// <summary>
    /// The answer for zero items no longer depends on the page size. The old expression yielded
    /// <c>0</c> at <c>perPage == 1</c> and <c>1</c> everywhere else, because <c>-1 / 1</c> is
    /// <c>-1</c> while <c>-1 / n</c> truncates to <c>0</c> — the inconsistency half of D-S6.
    /// </summary>
    [Fact]
    public void PageCount_ZeroTotal_IsIndependentOfThePageSize()
    {
        var service = CreateService();

        Assert.Equal(service.PageCount(0, 1), service.PageCount(0, 10));
    }

    /// <summary>
    /// PRD defect D-S7: a misconfigured page size used to take the request down with a
    /// <see cref="DivideByZeroException"/>, which names neither the argument nor the caller.
    /// </summary>
    [Fact]
    public void PageCount_ZeroPerPage_ThrowsArgumentOutOfRange()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateService().PageCount(100, 0));

        Assert.Equal("perPage", exception.ParamName);
    }

    /// <summary>Second half of D-S7: negative inputs used to return nonsense instead of an error.</summary>
    [Theory]
    [InlineData(10, -3, "perPage")]
    [InlineData(-5, 3, "total")]
    [InlineData(-5, -5, "total")]
    public void PageCount_NegativeInputs_ThrowArgumentOutOfRange(int total, int perPage, string expectedParamName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateService().PageCount(total, perPage));

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    // ── GetSitemapIndex ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetSitemapIndex_EmptySource_ReturnsNoSitemaps()
    {
        var result = CreateService().GetSitemapIndex(Array.Empty<TimestampedItem>(), 10, (perPage, page) => $"/s/{page}");

        Assert.Empty(result);
    }

    /// <summary>
    /// The empty case needs no special-casing any more: <c>PageCount(0, n)</c> is <c>0</c> since
    /// D-S6, so the page loop simply does not run. The old <c>items.Any()</c> guard existed only to
    /// mask that arithmetic.
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
            result.Select(s => s.Loc!).ToArray());
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
    /// The result is fully computed before the method returns. It used to be a lazy <c>Select</c>
    /// over <c>Enumerable.Range</c> layered on top of two eager probes — half-lazy, so the caller
    /// could not predict when the source would be touched, and a lambda that threw did so at some
    /// unrelated <c>foreach</c>.
    /// </summary>
    [Fact]
    public void GetSitemapIndex_IsFullyEvaluatedBeforeItReturns()
    {
        var source = new CountingEnumerable<TimestampedItem>(TimestampedItem.Sequence(5));
        var calls = 0;

        var result = CreateService().GetSitemapIndex(source, 2, (pp, page) => { calls++; return "/s"; });

        Assert.Equal(3, calls);

        result.ToList();

        Assert.Equal(3, calls);
    }

    /// <summary>
    /// PRD defect D-S8: the source is now materialised once, whatever the page count. It used to be
    /// walked <c>2 + pageCount</c> times — <c>Any()</c>, <c>Count()</c>, and one
    /// <c>Skip/Take/Max</c> per page — which on an <c>IQueryable</c> is N+2 database round trips
    /// per sitemap index.
    /// </summary>
    [Theory]
    [InlineData(5, 2)]
    [InlineData(100, 10)]
    [InlineData(1, 10)]
    [InlineData(0, 10)]
    public void GetSitemapIndex_EnumeratesTheSourceExactlyOnce(int itemCount, int perPage)
    {
        var source = new CountingEnumerable<TimestampedItem>(TimestampedItem.Sequence(itemCount));

        CreateService().GetSitemapIndex(source, perPage, (pp, page) => "/s").ToList();

        Assert.Equal(1, source.EnumerationCount);
    }

    /// <summary>
    /// Second half of D-S8: the returned sequence is a materialised snapshot, so re-enumerating it
    /// costs nothing. It used to be lazy, and every <c>foreach</c> the caller wrote paid the
    /// per-page cost again.
    /// </summary>
    [Fact]
    public void GetSitemapIndex_ReEnumeratingTheResult_DoesNotTouchTheSourceAgain()
    {
        var source = new CountingEnumerable<TimestampedItem>(TimestampedItem.Sequence(5));

        var result = CreateService().GetSitemapIndex(source, 2, (pp, page) => "/s");
        var first = result.ToList();
        var second = result.ToList();

        Assert.Equal(1, source.EnumerationCount);
        Assert.Equal(first.Select(sitemap => sitemap.LastMod), second.Select(sitemap => sitemap.LastMod));
    }

    /// <summary>
    /// Third part of D-S8, and the one that was a hard failure rather than a cost: a source that
    /// can only be walked once — a stream, a data reader, an already-consumed iterator — used to
    /// throw before the method even returned. It is now a supported input.
    /// </summary>
    [Fact]
    public void GetSitemapIndex_SingleUseSource_IsSupported()
    {
        var source = new SingleUseEnumerable<TimestampedItem>(TimestampedItem.Sequence(5));

        var result = CreateService().GetSitemapIndex(source, 2, (pp, page) => $"/s/{page}").ToList();

        Assert.Equal(["/s/1", "/s/2", "/s/3"], result.Select(sitemap => sitemap.Loc!).ToArray());
        Assert.Equal(new DateTime(2024, 1, 5), result[2].LastMod);
    }

    /// <summary>D-S7 reaching the caller through <c>GetSitemapIndex</c>.</summary>
    [Fact]
    public void GetSitemapIndex_ZeroPerPage_ThrowsArgumentOutOfRange()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateService().GetSitemapIndex(TimestampedItem.Sequence(5), 0, (pp, page) => "/s"));

        Assert.Equal("perPage", exception.ParamName);
    }

    [Fact]
    public void GetSitemapIndex_NullItems_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => CreateService().GetSitemapIndex<TimestampedItem>(null!, 10, (pp, page) => "/s"));
    }

    /// <summary>
    /// A null <c>urlFunc</c> is reported at the call that was wrong, and as an
    /// <see cref="ArgumentNullException"/> naming the parameter. It used to surface as a
    /// <see cref="NullReferenceException"/> at the caller's <c>foreach</c>, arbitrarily far away.
    /// </summary>
    [Fact]
    public void GetSitemapIndex_NullUrlFunc_ThrowsAtCallTime()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => CreateService().GetSitemapIndex(TimestampedItem.Sequence(5), 2, null!));

        Assert.Equal("urlFunc", exception.ParamName);
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
