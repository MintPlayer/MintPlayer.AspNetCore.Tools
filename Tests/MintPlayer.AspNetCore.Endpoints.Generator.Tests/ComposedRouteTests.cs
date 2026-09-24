using MintPlayer.AspNetCore.Endpoints.Generator;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// Pins route composition and the normalisation that decides whether two routes collide.
/// </summary>
/// <remarks>
/// Each normalisation rule here corresponds to a behaviour measured against the real ASP.NET Core
/// matcher, not to an intuition about routing. That matters in both directions: a rule that is too
/// loose reports a conflict between two endpoints that coexist perfectly well and blocks a build,
/// and a rule that is too strict misses an ambiguous-match 500 that this diagnostic exists to
/// catch. The most easily-got-wrong case is the last one — a constraint is a real distinction and
/// a parameter name is not.
/// </remarks>
public class ComposedRouteTests
{
    [Fact]
    public void Compose_JoinsPrefixesOutermostFirst()
    {
        Assert.Equal("/api/users/{id}", ComposedRoute.Compose(["/api", "/users"], "/{id}"));
    }

    [Fact]
    public void Compose_WorksWithNoGroups()
    {
        Assert.Equal("/health", ComposedRoute.Compose([], "/health"));
    }

    /// <summary>
    /// Composition is all-or-nothing. Treating an unrecoverable prefix as empty would yield a
    /// route wrong by exactly that prefix, and every diagnostic downstream would then be
    /// confidently wrong rather than silent.
    /// </summary>
    [Fact]
    public void Compose_ReturnsNullWhenAPrefixIsUnknown()
    {
        Assert.Null(ComposedRoute.Compose(["/api", null], "/{id}"));
    }

    [Fact]
    public void Compose_ReturnsNullWhenThePathIsUnknown()
    {
        Assert.Null(ComposedRoute.Compose(["/api"], null));
    }

    [Theory]
    // Routing is case-insensitive: /Case and /case are an ambiguous match at run time.
    [InlineData("/Case", "/case")]
    // A group prefix plus a member path of "/" composes with a trailing slash, which the matcher
    // does not treat as a distinction from the bare prefix.
    [InlineData("/api/users/", "/api/users")]
    [InlineData("/api//users", "/api/users")]
    // The parameter's name is erased: /a/{id} and /a/{key} are an ambiguous match.
    [InlineData("/a/{id}", "/a/{}")]
    [InlineData("/a/{key}", "/a/{}")]
    // ... but the constraint survives, because /items/{id:int} and /items/{slug} genuinely coexist.
    [InlineData("/items/{id:int}", "/items/{:int}")]
    [InlineData("/items/{slug}", "/items/{}")]
    // The root must not normalise away to nothing.
    [InlineData("/", "/")]
    public void Normalise_ReducesToWhatTheMatcherActuallyCompares(string route, string expected)
    {
        Assert.Equal(expected, ComposedRoute.Normalise(route));
    }

    /// <summary>
    /// The pairs that must compare equal, because at run time they are an ambiguous-match 500.
    /// </summary>
    [Theory]
    [InlineData("/a/{id}", "/a/{key}")]
    [InlineData("/Case", "/case")]
    [InlineData("/api/users/", "/api/users")]
    public void Normalise_CollapsesRoutesThatCollideAtRuntime(string left, string right)
    {
        Assert.Equal(ComposedRoute.Normalise(left), ComposedRoute.Normalise(right));
    }

    /// <summary>
    /// The pairs that must stay distinct. <c>/users/me</c> versus <c>/users/{id}</c> is the
    /// important one: both answer correctly at run time because a literal segment beats a
    /// parameter, so reporting a conflict there would be a pure false positive on any codebase
    /// that has a "me" route.
    /// </summary>
    [Theory]
    [InlineData("/users/me", "/users/{id}")]
    [InlineData("/items/{id:int}", "/items/{slug}")]
    [InlineData("/api/users", "/api/products")]
    public void Normalise_KeepsRoutesThatCoexistDistinct(string left, string right)
    {
        Assert.NotEqual(ComposedRoute.Normalise(left), ComposedRoute.Normalise(right));
    }

    /// <summary>
    /// Route parameter names, as the OpenAPI path key spells them: constraint, default, optional
    /// marker and catch-all stars removed, template case kept, escaped braces skipped.
    /// </summary>
    /// <remarks>
    /// These names become shadow members and must match the document's <c>{token}</c> exactly; a
    /// name carrying <c>:int</c> or a leading <c>**</c> would produce a parameter matching no token,
    /// which is the invalid document M5 exists to fix.
    /// </remarks>
    [Theory]
    [InlineData("/api/users/{id}", "id")]
    [InlineData("/api/users/{Id:int}/orders/{orderId?}", "Id|orderId")]
    [InlineData("/api/{**path}", "path")]
    [InlineData("/files/{*rest}", "rest")]
    [InlineData("/page/{n=1}", "n")]
    [InlineData("/lit/{{notaparam}}/{real}", "real")]
    [InlineData("/health", "")]
    public void Parameters_ReturnsTheTokenNamesInTemplateOrder(string route, string expected)
    {
        Assert.Equal(
            expected.Length == 0 ? [] : expected.Split('|'),
            ComposedRoute.Parameters(route).ToArray());
    }
}
