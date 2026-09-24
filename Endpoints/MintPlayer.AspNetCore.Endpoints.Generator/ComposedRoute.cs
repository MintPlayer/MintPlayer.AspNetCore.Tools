using System.Text;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Composes an endpoint's declared <c>Path</c> with its group chain's prefixes into the route a
/// request actually has to spell, and normalises it for comparison.
/// </summary>
/// <remarks>
/// This is the value <c>EndpointDescriptor.Path</c> has always reported at run time and the
/// generator has never known. Every route-shaped diagnostic needs it: two endpoints only collide
/// on their <i>composed</i> routes, and a group-relative path written absolutely is only visible
/// once the prefix it duplicates is in hand.
/// <para>
/// <b>Composition is all-or-nothing.</b> If the endpoint's own path or any prefix in its chain
/// could not be recovered, the result is <see langword="null"/>. Substituting an empty string for
/// an unknown prefix would silently produce a route that is wrong by exactly that prefix, and a
/// duplicate-route diagnostic built on it would then report confident nonsense.
/// </para>
/// </remarks>
internal static class ComposedRoute
{
    private static readonly char[] ParameterNameTerminators = [':', '=', '?'];

    /// <summary>
    /// Joins the group prefixes (outermost first) with the endpoint's own path.
    /// </summary>
    /// <returns>The composed route, or <see langword="null"/> when any part is unknown.</returns>
    public static string? Compose(IReadOnlyList<string?> groupPrefixes, string? path)
    {
        if (path is null) return null;

        var builder = new StringBuilder();
        foreach (var prefix in groupPrefixes)
        {
            if (prefix is null) return null;
            Join(builder, prefix);
        }

        Join(builder, path);
        return builder.ToString();
    }

    /// <summary>
    /// Appends a part, inserting the <c>/</c> the framework inserts when neither side supplies one:
    /// a group <c>/api</c> with a member path of <c>users</c> maps at <c>/api/users</c>, not
    /// <c>/apiusers</c>.
    /// </summary>
    private static void Join(StringBuilder builder, string part)
    {
        if (builder.Length > 0 && part.Length > 0 &&
            builder[builder.Length - 1] != '/' && part[0] != '/')
            builder.Append('/');

        builder.Append(part);
    }

    /// <summary>
    /// The names of the route parameters in <paramref name="route"/>, in template order, spelled
    /// exactly as the template spells them.
    /// </summary>
    /// <remarks>
    /// The name is what precedes any constraint (<c>:</c>), default (<c>=</c>) or optional marker
    /// (<c>?</c>), with a catch-all's leading <c>*</c> or <c>**</c> removed — the same name
    /// ASP.NET Core puts in <c>RouteValues</c> and in the OpenAPI path key. A doubled brace is an
    /// escaped literal, not a parameter. The spelling is kept because the documented parameter
    /// name has to match the path key's <c>{token}</c> exactly for the document to be valid.
    /// </remarks>
    public static List<string> Parameters(string route)
    {
        var names = new List<string>();

        for (var i = 0; i < route.Length; i++)
        {
            if (route[i] != '{') continue;

            if (i + 1 < route.Length && route[i + 1] == '{')
            {
                i++;                                    // "{{" is a literal brace
                continue;
            }

            var close = route.IndexOf('}', i + 1);
            if (close < 0) break;

            var body = route.Substring(i + 1, close - i - 1).TrimStart('*');
            var end = body.IndexOfAny(ParameterNameTerminators);
            var name = end < 0 ? body : body.Substring(0, end);
            if (name.Length > 0) names.Add(name);

            i = close;
        }

        return names;
    }

    /// <summary>
    /// Reduces a composed route to the form two routes must share to collide at run time.
    /// </summary>
    /// <remarks>
    /// Each rule here was measured against the real matcher rather than assumed, because a
    /// normalisation that is wrong in either direction turns a diagnostic into a liability:
    /// <list type="bullet">
    /// <item>
    /// <b>Case is folded.</b> <c>/Case</c> and <c>/case</c> are an ambiguous match at run time —
    /// routing is case-insensitive — so treating them as distinct would miss a real 500.
    /// </item>
    /// <item>
    /// <b>Repeated slashes collapse and a trailing slash is dropped.</b> A group prefix of
    /// <c>/api/users</c> with a member path of <c>/</c> composes to <c>/api/users/</c>, which
    /// collides with a literal <c>/api/users</c>. The trailing slash is not a distinction to the
    /// matcher, and the descriptor list has always reported the unnormalised form.
    /// </item>
    /// <item>
    /// <b>Parameter names are erased, constraints are not.</b> <c>/a/{id}</c> and <c>/a/{key}</c>
    /// are an ambiguous match, so the name cannot matter. But <c>/items/{id:int}</c> and
    /// <c>/items/{slug}</c> genuinely coexist, because the constraint disambiguates them — so the
    /// constraint text has to survive.
    /// </item>
    /// </list>
    /// A literal segment beating a parameter segment (<c>/users/me</c> versus <c>/users/{id}</c>)
    /// is deliberately <i>not</i> modelled: both endpoints answer correctly, so there is nothing
    /// to report and normalising them together would be a pure false positive.
    /// </remarks>
    public static string Normalise(string route)
    {
        var builder = new StringBuilder(route.Length + 1);

        // "users" and "/users" are the same route to the matcher.
        if (route.Length == 0 || route[0] != '/') builder.Append('/');

        for (var i = 0; i < route.Length; i++)
        {
            var c = route[i];

            if (c == '{' && i + 1 < route.Length && route[i + 1] == '{')
            {
                builder.Append("{{");                   // an escaped literal brace, not a parameter
                i++;
                continue;
            }

            if (c == '{')
            {
                var close = route.IndexOf('}', i + 1);
                if (close < 0)
                {
                    // Malformed; keep the rest verbatim rather than inventing a parameter.
                    builder.Append(route, i, route.Length - i);
                    break;
                }

                builder.Append(NormaliseParameter(route.Substring(i + 1, close - i - 1)));
                i = close;
                continue;
            }

            // Outside a parameter: collapse runs of '/'.
            if (c == '/' && builder.Length > 0 && builder[builder.Length - 1] == '/') continue;

            builder.Append(char.ToLowerInvariant(c));
        }

        // Drop a trailing slash, but never reduce the root itself to the empty string.
        while (builder.Length > 1 && builder[builder.Length - 1] == '/')
            builder.Length--;

        return builder.ToString();
    }

    /// <summary>
    /// A parameter reduced to what decides whether it can coexist with another at the same
    /// position: whether it is a catch-all, and its constraints.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// The name is erased (<c>{id}</c> and <c>{key}</c> collide).
    /// </item>
    /// <item>
    /// A catch-all keeps a <c>*</c> marker, <c>*</c> and <c>**</c> alike. A catch-all has lower
    /// precedence than an ordinary parameter, so <c>/api/{**rest}</c> and <c>/api/{id}</c> both
    /// answer — erasing the stars would call that a duplicate.
    /// </item>
    /// <item>
    /// The optional marker and a default value are dropped: <c>/a/{id?}</c> and <c>/a/{id}</c> still
    /// both claim <c>/a/5</c> at the same precedence.
    /// </item>
    /// </list>
    /// </remarks>
    private static string NormaliseParameter(string body)
    {
        var catchAll = body.StartsWith("*", StringComparison.Ordinal);
        body = body.TrimStart('*');

        var constraintStart = body.IndexOf(':');
        var constraints = constraintStart < 0 ? "" : body.Substring(constraintStart);

        // Cut a default ("=value") that is not inside a constraint's argument list, then a trailing
        // optional marker. A regex constraint may legitimately contain '=' or '?' inside parentheses.
        var depth = 0;
        for (var i = 0; i < constraints.Length; i++)
        {
            var c = constraints[i];
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (c == '=' && depth == 0)
            {
                constraints = constraints.Substring(0, i);
                break;
            }
        }

        if (constraints.EndsWith("?", StringComparison.Ordinal))
            constraints = constraints.Substring(0, constraints.Length - 1);

        return "{" + (catchAll ? "*" : "") + constraints + "}";
    }
}
