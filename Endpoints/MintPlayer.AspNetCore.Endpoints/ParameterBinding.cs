using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>Where a bound endpoint property takes its value from.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum ParameterSource
{
    /// <summary>A route value — <c>{id}</c> in the endpoint's <c>Path</c>.</summary>
    Route,

    /// <summary>A query-string value — <c>?page=2</c>.</summary>
    Query,
}

/// <summary>
/// The conversions generated code uses to assign <c>[RouteParam]</c> and <c>[QueryParam]</c>
/// properties. Not intended to be called by hand.
/// </summary>
/// <remarks>
/// <b>Why so many methods with near-identical names.</b> Generic constraints are not part of a C#
/// method signature, so <c>Parsable&lt;T&gt;() where T : IParsable&lt;T&gt;</c> and
/// <c>Enum&lt;T&gt;() where T : struct, Enum</c> cannot be overloads of one name — they are
/// <c>CS0111</c>. The generator classifies the property's type at compile time and picks the
/// method by name instead.
/// <para>
/// Every failure is an <see cref="EndpointBindingException"/> carrying a 400 and a message naming
/// the parameter, the offending value and the expected type, so it takes the same
/// <c>OnBindFailedAsync</c> route as a malformed body rather than escaping as a 500.
/// </para>
/// <para>
/// Parsing uses <see cref="CultureInfo.InvariantCulture"/>: a URL is not localised, and
/// <c>2026-09-24</c> must mean the same thing on every server. Enums parse case-insensitively and
/// reject undefined values — <c>Enum.TryParse("7")</c> alone succeeds for an enum with three
/// members, and would hand the handler a value no <c>switch</c> expects.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ParameterBinding
{
    // ---- strings -------------------------------------------------------------------------

    /// <summary>A required string. Absent is a 400.</summary>
    public static string String(HttpContext context, ParameterSource source, string key) =>
        Raw(context, source, key) ?? throw Missing(source, key);

    /// <summary>An optional string. Absent is <see langword="null"/>.</summary>
    public static string? OptionalString(HttpContext context, ParameterSource source, string key) =>
        Raw(context, source, key);

    /// <summary>A string with a declared default: assigns only when a value was supplied.</summary>
    public static bool TryString(HttpContext context, ParameterSource source, string key, out string value)
    {
        var raw = Raw(context, source, key);
        value = raw!;
        return raw is not null;
    }

    // ---- IParsable<T> ----------------------------------------------------------------------

    /// <summary>A required parsable value. Absent or malformed is a 400.</summary>
    public static T Parsable<T>(HttpContext context, ParameterSource source, string key)
        where T : IParsable<T>
    {
        var raw = Present(context, source, key) ?? throw Missing(source, key);
        return Parse<T>(raw, source, key);
    }

    /// <summary>An optional parsable value type. Absent is <see langword="null"/>; malformed is a 400.</summary>
    public static T? OptionalParsable<T>(HttpContext context, ParameterSource source, string key)
        where T : struct, IParsable<T>
    {
        var raw = Present(context, source, key);
        return raw is null ? null : Parse<T>(raw, source, key);
    }

    /// <summary>
    /// An optional parsable <i>reference</i> type. Absent is <see langword="null"/>; malformed is a 400.
    /// </summary>
    /// <remarks>
    /// A separate method because <see cref="OptionalParsable{T}"/> needs <c>where T : struct</c> to
    /// return <c>T?</c>. Without this one, a <c>Foo?</c> property where <c>Foo</c> is a class
    /// implementing <see cref="IParsable{TSelf}"/> would have to be treated as required, and an
    /// absent optional value would become a 400.
    /// </remarks>
    public static T? OptionalParsableReference<T>(HttpContext context, ParameterSource source, string key)
        where T : class, IParsable<T>
    {
        var raw = Present(context, source, key);
        return raw is null ? null : Parse<T>(raw, source, key);
    }

    /// <summary>A parsable value with a declared default: assigns only when a value was supplied.</summary>
    public static bool TryParsable<T>(HttpContext context, ParameterSource source, string key, out T value)
        where T : IParsable<T>
    {
        var raw = Present(context, source, key);
        if (raw is null)
        {
            value = default!;
            return false;
        }

        value = Parse<T>(raw, source, key);
        return true;
    }

    // ---- enums ------------------------------------------------------------------------------

    /// <summary>A required enum. Absent, unrecognised or undefined is a 400.</summary>
    public static T Enum<T>(HttpContext context, ParameterSource source, string key)
        where T : struct, System.Enum
    {
        var raw = Present(context, source, key) ?? throw Missing(source, key);
        return ParseEnum<T>(raw, source, key);
    }

    /// <summary>An optional enum. Absent is <see langword="null"/>; unrecognised is a 400.</summary>
    public static T? OptionalEnum<T>(HttpContext context, ParameterSource source, string key)
        where T : struct, System.Enum
    {
        var raw = Present(context, source, key);
        return raw is null ? null : ParseEnum<T>(raw, source, key);
    }

    /// <summary>An enum with a declared default: assigns only when a value was supplied.</summary>
    public static bool TryEnum<T>(HttpContext context, ParameterSource source, string key, out T value)
        where T : struct, System.Enum
    {
        var raw = Present(context, source, key);
        if (raw is null)
        {
            value = default;
            return false;
        }

        value = ParseEnum<T>(raw, source, key);
        return true;
    }

    // ---- failure --------------------------------------------------------------------------

    /// <summary>
    /// The response for a binding failure when the endpoint has no <c>OnBindFailedAsync</c> of its
    /// own to decide — that is, a raw <c>IEndpoint</c>.
    /// </summary>
    public static IResult Failure(EndpointBindingException failure) =>
        Results.Problem(statusCode: failure.StatusCode, detail: failure.Message);

    // ---- plumbing -------------------------------------------------------------------------

    /// <summary>
    /// The raw value, or <see langword="null"/> when none was supplied.
    /// </summary>
    /// <remarks>
    /// Three distinct route shapes all mean "not supplied" and all collapse to null here: an
    /// unmatched optional <c>{id?}</c> leaves the key <i>absent</i>, an unmatched catch-all
    /// <c>{*rest}</c> leaves it <i>present with a null value</i>, and a route default
    /// <c>{id=5}</c> leaves it <i>present with the default</i> — which is a supplied value.
    /// <para>
    /// <see cref="RouteValueDictionary"/> is typed <see cref="object"/>. Every value observed in
    /// practice is a <see cref="string"/>, but a custom constraint or parameter transformer could
    /// place something else, so this reads with <c>as</c>/<c>ToString</c> rather than a cast.
    /// </para>
    /// </remarks>
    private static string? Raw(HttpContext context, ParameterSource source, string key)
    {
        if (source == ParameterSource.Route)
        {
            return context.Request.RouteValues.TryGetValue(key, out var value)
                ? value as string ?? value?.ToString()
                : null;
        }

        var values = context.Request.Query[key];
        return values.Count == 0 ? null : values[0];
    }

    /// <summary>
    /// Like <see cref="Raw"/>, but an empty string also counts as absent. Used for every non-string
    /// type: <c>?page=</c> is a client that did not choose a page, not one that chose an unparsable
    /// one. A <c>string</c> property keeps <c>""</c>, because for text the difference is real.
    /// </summary>
    private static string? Present(HttpContext context, ParameterSource source, string key)
    {
        var raw = Raw(context, source, key);
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    private static T Parse<T>(string raw, ParameterSource source, string key) where T : IParsable<T> =>
        T.TryParse(raw, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new EndpointBindingException(
                StatusCodes.Status400BadRequest,
                $"The {Describe(source)} '{key}' must be a valid {typeof(T).Name}; '{raw}' is not.");

    private static T ParseEnum<T>(string raw, ParameterSource source, string key) where T : struct, System.Enum =>
        System.Enum.TryParse<T>(raw, ignoreCase: true, out var value) && System.Enum.IsDefined(value)
            ? value
            : throw new EndpointBindingException(
                StatusCodes.Status400BadRequest,
                $"The {Describe(source)} '{key}' must be one of: {string.Join(", ", System.Enum.GetNames<T>())}; '{raw}' is not.");

    private static EndpointBindingException Missing(ParameterSource source, string key) =>
        new(StatusCodes.Status400BadRequest, $"The {Describe(source)} '{key}' is required.");

    private static string Describe(ParameterSource source) =>
        source == ParameterSource.Route ? "route parameter" : "query parameter";
}
