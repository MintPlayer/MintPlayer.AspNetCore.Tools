using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using MintPlayer.AspNetCore.Endpoints;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// The runtime conversion helpers the generated binders call: <c>ParameterBinding</c>'s required,
/// optional and <c>Try</c> families for <c>string</c>, <c>IParsable&lt;T&gt;</c> and enums (PRD R2.5–R2.8).
/// </summary>
/// <remarks>
/// These are the whole of the runtime contract behind <c>[RouteParam]</c>/<c>[QueryParam]</c>; the
/// generator only picks which one to call. Each rule here is one that is easy to get subtly wrong and
/// then never notice: a culture-sensitive parse that works on the build agent, an enum that accepts
/// <c>"7"</c>, a route "no value" shape that reads as a value.
/// </remarks>
public class ParameterBindingTests
{
    private enum Kind { Book, Film }

    private sealed class Slug : IParsable<Slug>
    {
        private Slug(string value) => Value = value;
        public string Value { get; }
        public static Slug Parse(string s, IFormatProvider? provider) => new(s);
        public static bool TryParse(string? s, IFormatProvider? provider, out Slug result)
        {
            result = new Slug(s ?? "");
            return s is not null;
        }
    }

    private static DefaultHttpContext Route(params (string Key, object? Value)[] values)
    {
        var context = new DefaultHttpContext();
        foreach (var (key, value) in values)
            context.Request.RouteValues[key] = value;
        return context;
    }

    private static DefaultHttpContext Query(string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);
        return context;
    }

    // ---- R2.8: the three route "no value" shapes collapse to one "not supplied" ------------

    /// <summary>
    /// <c>{id?}</c> unmatched is <b>key absent</b>; <c>{*rest}</c> unmatched is <b>key present, value
    /// null</b>. Both must read as "not supplied" — optional yields null, Try yields false, required
    /// is a 400 "is required" rather than a <c>NullReferenceException</c> or a parse of <c>""</c>.
    /// </summary>
    [Theory]
    [InlineData(false)]   // {id?}: key absent
    [InlineData(true)]    // {*rest}: key present, value null
    public void RouteNoValue_AbsentKeyAndNullValue_AreBothNotSupplied(bool keyPresentWithNull)
    {
        HttpContext Context() => keyPresentWithNull ? Route(("id", null)) : Route();

        Assert.Null(ParameterBinding.OptionalParsable<int>(Context(), ParameterSource.Route, "id"));
        Assert.Null(ParameterBinding.OptionalString(Context(), ParameterSource.Route, "id"));
        Assert.Null(ParameterBinding.OptionalEnum<Kind>(Context(), ParameterSource.Route, "id"));
        Assert.Null(ParameterBinding.OptionalParsableReference<Slug>(Context(), ParameterSource.Route, "id"));
        Assert.False(ParameterBinding.TryParsable<int>(Context(), ParameterSource.Route, "id", out _));
        Assert.False(ParameterBinding.TryString(Context(), ParameterSource.Route, "id", out _));
        Assert.False(ParameterBinding.TryEnum<Kind>(Context(), ParameterSource.Route, "id", out _));

        var failure = Assert.Throws<EndpointBindingException>(() => ParameterBinding.Parsable<int>(Context(), ParameterSource.Route, "id"));
        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
        Assert.Equal("The route parameter 'id' is required.", failure.Message);
        Assert.Throws<EndpointBindingException>(() => ParameterBinding.String(Context(), ParameterSource.Route, "id"));
        Assert.Throws<EndpointBindingException>(() => ParameterBinding.Enum<Kind>(Context(), ParameterSource.Route, "id"));
    }

    /// <summary>
    /// <c>{id=5}</c> unmatched is <b>key present with the default string</b> — routing has already
    /// substituted the template default, so it binds as a supplied value.
    /// </summary>
    [Fact]
    public void RouteNoValue_TemplateDefault_BindsTheDefault()
    {
        Assert.Equal(5, ParameterBinding.Parsable<int>(Route(("id", "5")), ParameterSource.Route, "id"));
        Assert.True(ParameterBinding.TryParsable<int>(Route(("id", "5")), ParameterSource.Route, "id", out var value));
        Assert.Equal(5, value);
    }

    /// <summary>
    /// A non-string route value (a custom constraint or transformer could place one) is read
    /// defensively rather than hard-cast (R2.8).
    /// </summary>
    [Fact]
    public void RouteValue_NonString_IsReadWithoutAHardCast()
        => Assert.Equal(42, ParameterBinding.Parsable<int>(Route(("id", 42)), ParameterSource.Route, "id"));

    /// <summary>
    /// Route and query keys match case-insensitively, as the framework's own collections do (R2.3).
    /// </summary>
    [Fact]
    public void Keys_MatchCaseInsensitively()
    {
        Assert.Equal(7, ParameterBinding.Parsable<int>(Route(("id", "7")), ParameterSource.Route, "Id"));
        Assert.Equal(3, ParameterBinding.Parsable<int>(Query("?page=3"), ParameterSource.Query, "Page"));
    }

    // ---- R2.6: enums -------------------------------------------------------------------------

    /// <summary>
    /// <c>"7"</c> is rejected for an enum with no member 7.
    /// </summary>
    /// <remarks>
    /// <c>Enum.TryParse("7")</c> succeeds and yields an undefined value, so without
    /// <c>Enum.IsDefined</c> <c>/items/kind/7</c> answered <c>200 {"kind":7}</c>. Every enum family
    /// must check it.
    /// </remarks>
    [Fact]
    public void Enum_UndefinedNumericValue_IsRejected()
    {
        var failure = Assert.Throws<EndpointBindingException>(() => ParameterBinding.Enum<Kind>(Route(("kind", "7")), ParameterSource.Route, "kind"));
        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
        Assert.Equal("The route parameter 'kind' must be one of: Book, Film; '7' is not.", failure.Message);

        Assert.Throws<EndpointBindingException>(() => ParameterBinding.OptionalEnum<Kind>(Route(("kind", "7")), ParameterSource.Route, "kind"));
        Assert.Throws<EndpointBindingException>(() => ParameterBinding.TryEnum<Kind>(Route(("kind", "7")), ParameterSource.Route, "kind", out _));
    }

    /// <summary>
    /// A defined numeric value is still accepted — <c>IsDefined</c> rejects undefined values, not
    /// numbers.
    /// </summary>
    [Fact]
    public void Enum_DefinedNumericValue_IsAccepted()
        => Assert.Equal(Kind.Film, ParameterBinding.Enum<Kind>(Route(("kind", "1")), ParameterSource.Route, "kind"));

    /// <summary>
    /// Enum names parse case-insensitively — a deliberate divergence from the framework's own
    /// case-sensitive enum binding (R2.6).
    /// </summary>
    [Theory]
    [InlineData("film")]
    [InlineData("FILM")]
    [InlineData("Film")]
    public void Enum_ParsesCaseInsensitively(string raw)
    {
        Assert.Equal(Kind.Film, ParameterBinding.Enum<Kind>(Query($"?kind={raw}"), ParameterSource.Query, "kind"));
        Assert.Equal(Kind.Film, ParameterBinding.OptionalEnum<Kind>(Query($"?kind={raw}"), ParameterSource.Query, "kind"));
        Assert.True(ParameterBinding.TryEnum<Kind>(Query($"?kind={raw}"), ParameterSource.Query, "kind", out var value));
        Assert.Equal(Kind.Film, value);
    }

    // ---- R2.7: InvariantCulture ------------------------------------------------------------

    /// <summary>
    /// <c>2026-09-24</c> parses as a <c>DateOnly</c> and <c>24/09/2026</c> does not, whatever the
    /// current culture.
    /// </summary>
    /// <remarks>
    /// Run under nl-BE, where <c>24/09/2026</c> <i>is</i> a valid date: a parse that used the current
    /// culture would accept it here and reject it on an en-US build agent, so a URL's meaning would
    /// depend on the server it hit.
    /// </remarks>
    [Fact]
    public void Parsable_UsesInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("nl-BE");

            Assert.Equal(new DateOnly(2026, 9, 24), ParameterBinding.Parsable<DateOnly>(Query("?since=2026-09-24"), ParameterSource.Query, "since"));

            var failure = Assert.Throws<EndpointBindingException>(() => ParameterBinding.Parsable<DateOnly>(Query("?since=24/09/2026"), ParameterSource.Query, "since"));
            Assert.Equal("The query parameter 'since' must be a valid DateOnly; '24/09/2026' is not.", failure.Message);

            // A decimal separator is '.', not the nl-BE ','.
            Assert.Equal(1.5m, ParameterBinding.Parsable<decimal>(Query("?amount=1.5"), ParameterSource.Query, "amount"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>A malformed value is a 400 naming the parameter, the value and the type (R2.11).</summary>
    [Fact]
    public void Parsable_MalformedValue_Is400NamingParameterValueAndType()
    {
        var failure = Assert.Throws<EndpointBindingException>(() => ParameterBinding.Parsable<int>(Route(("Id", "abc")), ParameterSource.Route, "Id"));

        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
        Assert.Equal("The route parameter 'Id' must be a valid Int32; 'abc' is not.", failure.Message);
    }

    // ---- query specifics --------------------------------------------------------------------

    /// <summary>
    /// <c>?page=</c> is "not supplied" for a non-string type — optional null, Try false, required
    /// "is required" — rather than a failed parse of <c>""</c>. For a <c>string</c> the empty value is
    /// a value, and is kept.
    /// </summary>
    /// <remarks>
    /// An HTML form submits an untouched field as <c>name=</c>, so treating it as a malformed
    /// integer would reject ordinary form GETs. A string, though, can legitimately be empty, and
    /// discarding it would make "search for nothing" indistinguishable from "no search".
    /// </remarks>
    [Fact]
    public void Query_EmptyValue_IsAbsentForNonString_ButKeptForString()
    {
        Assert.Null(ParameterBinding.OptionalParsable<int>(Query("?page="), ParameterSource.Query, "page"));
        Assert.False(ParameterBinding.TryParsable<int>(Query("?page="), ParameterSource.Query, "page", out _));
        Assert.Null(ParameterBinding.OptionalEnum<Kind>(Query("?page="), ParameterSource.Query, "page"));
        Assert.Equal(
            "The query parameter 'page' is required.",
            Assert.Throws<EndpointBindingException>(() => ParameterBinding.Parsable<int>(Query("?page="), ParameterSource.Query, "page")).Message);

        Assert.Equal("", ParameterBinding.String(Query("?term="), ParameterSource.Query, "term"));
        Assert.Equal("", ParameterBinding.OptionalString(Query("?term="), ParameterSource.Query, "term"));
        Assert.True(ParameterBinding.TryString(Query("?term="), ParameterSource.Query, "term", out var term));
        Assert.Equal("", term);
    }

    /// <summary>
    /// A repeated query key binds its <b>first</b> value — not the comma-joined
    /// <c>StringValues.ToString()</c>, which would turn <c>?page=1&amp;page=2</c> into a 400 on
    /// <c>"1,2"</c>.
    /// </summary>
    [Fact]
    public void Query_RepeatedKey_UsesTheFirstValue()
    {
        Assert.Equal(1, ParameterBinding.Parsable<int>(Query("?page=1&page=2"), ParameterSource.Query, "page"));
        Assert.Equal("a", ParameterBinding.String(Query("?term=a&term=b"), ParameterSource.Query, "term"));
    }

    /// <summary>
    /// A nullable reference type implementing <c>IParsable</c> goes through its own helper and
    /// binds when present.
    /// </summary>
    [Fact]
    public void OptionalParsableReference_BindsWhenPresent()
        => Assert.Equal("intro", ParameterBinding.OptionalParsableReference<Slug>(Query("?tag=intro"), ParameterSource.Query, "tag")!.Value);

    /// <summary>
    /// <c>Failure</c> turns a binding exception into the problem-details 400 the raw endpoint's
    /// generated <c>IParameterBinder</c> returns.
    /// </summary>
    [Fact]
    public void Failure_IsAProblemWithTheStatusAndMessage()
    {
        var result = Assert.IsType<ProblemHttpResult>(
            ParameterBinding.Failure(new EndpointBindingException(StatusCodes.Status400BadRequest, "nope")));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("nope", result.ProblemDetails.Detail);
    }
}
