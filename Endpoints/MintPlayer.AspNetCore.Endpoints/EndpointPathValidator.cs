using Microsoft.AspNetCore.Routing.Patterns;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// The startup check on a route chosen by <c>IEndpointBase.GetPath</c>: it must have the same route
/// parameters as the endpoint's <c>Path</c> (PRD R2.10). Called by the generated mapping and by
/// <c>MapEndpoint&lt;T&gt;()</c>, only when <c>GetPath</c> returned a value.
/// </summary>
/// <remarks>
/// <para>
/// Bound <c>[RouteParam]</c> properties, and every build-time route check, are tied to the default
/// <c>Path</c>. A configured path that drops or renames a parameter would map, start, and then answer
/// every request with a 400 for a value that cannot be bound; failing at startup instead makes the
/// mistake visible where it was made (PRD D10).
/// </para>
/// <para>
/// Only parameter <b>names</b> are compared, case-insensitively, as routing matches them. Literal
/// segments may differ freely — that is the point of a configurable path — and constraints, defaults,
/// optional and catch-all markers are not compared, because they do not change which properties bind.
/// </para>
/// </remarks>
public static class EndpointPathValidator
{
    /// <summary>
    /// Throws when <paramref name="configured"/> and <paramref name="default"/> do not declare the same
    /// route parameter names.
    /// </summary>
    /// <param name="endpoint">The endpoint class, named in the message.</param>
    /// <param name="configured">The route <c>GetPath</c> returned.</param>
    /// <param name="default">The endpoint's literal <c>Path</c>.</param>
    /// <exception cref="InvalidOperationException">
    /// The parameter names differ, or <paramref name="configured"/> is not a valid route pattern. The
    /// message names the endpoint, both patterns, and the parameters each side is missing.
    /// </exception>
    public static void EnsureSameParameters(Type endpoint, string configured, string @default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(@default);

        var configuredNames = ParameterNames(endpoint, configured, "GetPath");
        var defaultNames = ParameterNames(endpoint, @default, "Path");

        var missingFromConfigured = defaultNames.Where(name => !configuredNames.Contains(name)).ToList();
        var missingFromDefault = configuredNames.Where(name => !defaultNames.Contains(name)).ToList();
        if (missingFromConfigured.Count == 0 && missingFromDefault.Count == 0)
            return;

        var differences = new List<string>();
        if (missingFromConfigured.Count > 0)
            differences.Add($"'{configured}' lacks {Format(missingFromConfigured)}");
        if (missingFromDefault.Count > 0)
            differences.Add($"'{@default}' lacks {Format(missingFromDefault)}");

        throw new InvalidOperationException(
            $"Endpoint '{endpoint.FullName}' maps at '{configured}' from GetPath, but its route parameters differ from those of its default Path '{@default}': " +
            string.Join("; ", differences) + ". " +
            "A configured path must keep the default's route parameters, because the endpoint's bound properties and its build-time route checks use Path.");
    }

    private static HashSet<string> ParameterNames(Type endpoint, string pattern, string source)
    {
        RoutePattern parsed;
        try
        {
            parsed = RoutePatternFactory.Parse(pattern);
        }
        catch (RoutePatternException failure)
        {
            throw new InvalidOperationException($"Endpoint '{endpoint.FullName}' has an invalid route '{pattern}' from {source}: {failure.Message}", failure);
        }

        // Case-insensitive, as routing matches route values.
        return new HashSet<string>(parsed.Parameters.Select(parameter => parameter.Name), StringComparer.OrdinalIgnoreCase);
    }

    private static string Format(IEnumerable<string> names) => string.Join(", ", names.Select(name => "{" + name + "}"));
}
