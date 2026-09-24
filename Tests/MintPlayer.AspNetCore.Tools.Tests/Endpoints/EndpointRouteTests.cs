using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.Endpoints;
using Xunit;
using Xunit.Abstractions;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// <see cref="EndpointRoute.ToString"/> produces exactly what ASP.NET Core's own
/// <see cref="LinkGenerator"/> produces, byte for byte, for every value shape that has ever diverged.
/// </summary>
/// <remarks>
/// <para>
/// This is the load-bearing test of typed links. <c>ToString()</c> — and so the implicit
/// <see cref="string"/> conversion every <c>Results.Created(Routes…, body)</c> goes through — cannot
/// ask a <see cref="LinkGenerator"/>, so it substitutes into the template itself. The failure mode is
/// silent: a link that differs from the framework's only in how <c>@</c> or <c>/</c> is escaped still
/// looks like a URL, and a substitution helper built on <c>Uri.EscapeDataString</c> once matched on
/// 12 cases and diverged on 7 of 16. So every case is compared against a real
/// <see cref="LinkGenerator"/> resolving the same template by name, not against a hand-written
/// expectation that could be wrong in the same way as the code.
/// </para>
/// <para>
/// The generator is not involved: the endpoints are registered directly, named, with the same
/// templates the generated <c>Routes</c> class would carry. What the generated methods put in
/// <see cref="EndpointRoute.Values"/> is covered by the generator tests.
/// </para>
/// </remarks>
public class EndpointRouteTests
{
    private readonly ITestOutputHelper output;

    public EndpointRouteTests(ITestOutputHelper output) => this.output = output;

    private static readonly (string Name, string Template)[] Templates =
    [
        ("Plain", "/t/{v}"),
        ("CatchAll", "/c/{**path}"),
        ("Star", "/s/{*path}"),
        ("Optional", "/api/opt/{id?}"),
        ("Defaulted", "/d/{n=1}"),
        ("Dotted", "/f/{name}.{ext?}"),
        ("MiddleDefault", "/{lang=en}/items/{id}"),
        ("Root", "/"),
    ];

    private enum Shade { Light, Dark }

    /// <summary>The battery: a label, the endpoint name, and the values.</summary>
    public static readonly (string Label, string Name, (string Key, object? Value)[] Values)[] Cases =
    [
        ("int", "Plain", [("v", 42)]),
        ("Guid", "Plain", [("v", Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"))]),
        ("enum", "Plain", [("v", Shade.Dark)]),
        ("DateOnly", "Plain", [("v", new DateOnly(2026, 9, 24))]),
        ("double", "Plain", [("v", 1.5)]),
        ("a@b", "Plain", [("v", "a@b")]),
        ("a!b", "Plain", [("v", "a!b")]),
        ("a$b", "Plain", [("v", "a$b")]),
        ("a(b)", "Plain", [("v", "a(b)")]),
        ("a*b", "Plain", [("v", "a*b")]),
        ("a,b", "Plain", [("v", "a,b")]),
        ("a;b", "Plain", [("v", "a;b")]),
        ("a/b", "Plain", [("v", "a/b")]),
        ("a?b", "Plain", [("v", "a?b")]),
        ("a#b", "Plain", [("v", "a#b")]),
        ("a%b", "Plain", [("v", "a%b")]),
        ("a b", "Plain", [("v", "a b")]),
        ("a+b", "Plain", [("v", "a+b")]),
        ("ü", "Plain", [("v", "ü")]),
        ("日本", "Plain", [("v", "日本")]),
        ("..", "Plain", [("v", "..")]),
        ("a&b=c", "Plain", [("v", "a&b=c")]),
        ("catch-all a/b/c", "CatchAll", [("path", "a/b/c")]),
        ("catch-all a?b", "CatchAll", [("path", "a?b")]),
        ("catch-all a b/ü", "CatchAll", [("path", "a b/ü")]),
        ("catch-all omitted", "CatchAll", []),
        ("single-star a/b", "Star", [("path", "a/b")]),
        ("optional omitted", "Optional", []),
        ("optional supplied", "Optional", [("id", 7)]),
        ("optional empty string", "Optional", [("id", "")]),
        ("defaulted omitted", "Defaulted", []),
        ("defaulted equal (string)", "Defaulted", [("n", "1")]),
        ("defaulted equal (int)", "Defaulted", [("n", 1)]),
        ("defaulted other", "Defaulted", [("n", 2)]),
        ("dotted with ext", "Dotted", [("name", "report"), ("ext", "pdf")]),
        ("dotted without ext", "Dotted", [("name", "report")]),
        ("middle default omitted", "MiddleDefault", [("id", 5)]),
        ("middle default supplied", "MiddleDefault", [("lang", "nl"), ("id", 5)]),
        ("query", "Plain", [("v", 1), ("q", "a b"), ("page", 2)]),
        ("query skips null and empty", "Plain", [("v", 1), ("a", null), ("b", ""), ("c", "x")]),
        ("query escapes key and value", "Plain", [("v", 1), ("k y", "a&b=c+d?#")]),
        ("root", "Root", []),
        ("root with query", "Root", [("q", "x")]),
    ];

    public static TheoryData<string> CaseLabels()
    {
        var data = new TheoryData<string>();
        foreach (var @case in Cases) data.Add(@case.Label);
        return data;
    }

    /// <summary>A real <see cref="LinkGenerator"/> over named endpoints with the battery's templates.</summary>
    private static LinkGenerator Links()
    {
        var endpoints = Templates
            .Select((entry, order) =>
            {
                var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse(entry.Template), order)
                {
                    DisplayName = entry.Name,
                };
                builder.Metadata.Add(new EndpointNameMetadata(entry.Name));
                builder.Metadata.Add(new RouteNameMetadata(entry.Name));
                return (Endpoint)builder.Build();
            })
            .ToList();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<EndpointDataSource>(new DefaultEndpointDataSource(endpoints));
        return services.BuildServiceProvider().GetRequiredService<LinkGenerator>();
    }

    private static EndpointRoute Route(string label)
    {
        var @case = Cases.Single(candidate => candidate.Label == label);
        var values = new RouteValueDictionary();
        foreach (var (key, value) in @case.Values) values[key] = value;
        return new EndpointRoute(@case.Name, Templates.Single(t => t.Name == @case.Name).Template, values);
    }

    private static string Describe(Func<string> produce)
    {
        try { return produce(); }
        catch (InvalidOperationException) { return "(fails)"; }
    }

    /// <summary>
    /// One case of the battery: <see cref="EndpointRoute.ToString"/> equals
    /// <see cref="EndpointRoute.Path(LinkGenerator)"/>, or both fail.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseLabels))]
    public void ToString_MatchesLinkGenerator(string label)
    {
        var route = Route(label);
        var links = Links();

        Assert.Equal(Describe(() => route.Path(links)), Describe(route.ToString));
    }

    /// <summary>
    /// The same battery against the URL builder the typed-client generator emitted into
    /// <c>MintPlayer.AspNetCore.Endpoints.TestApp.Client</c> (M9) — a copy of the runtime's
    /// <c>EndpointTemplateBinder</c> source compiled in a project without ASP.NET Core.
    /// </summary>
    /// <remarks>
    /// This is what makes "the client builds URLs exactly as <see cref="LinkGenerator"/> does" a
    /// measured fact rather than an inference from sharing a file: it runs the generated copy.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CaseLabels))]
    public void GeneratedClientUrlBuilder_MatchesLinkGenerator(string label)
    {
        var @case = Cases.Single(candidate => candidate.Label == label);
        var template = Templates.Single(t => t.Name == @case.Name).Template;
        var values = @case.Values.Select(pair => new KeyValuePair<string, object?>(pair.Key, pair.Value)).ToList();

        var generated = MintPlayer.AspNetCore.Endpoints.Generated.EndpointTemplateBinder.Bind(template, values) ?? "(fails)";

        Assert.Equal(Describe(() => Route(label).Path(Links())), generated);
    }

    /// <summary>
    /// Writes the whole battery as a table — label, template, what <see cref="LinkGenerator"/>
    /// produced, what <see cref="EndpointRoute.ToString"/> produced — and asserts they all agree.
    /// </summary>
    /// <remarks>
    /// The per-case theory is what fails usefully; this is the record, read with a detailed console
    /// logger, of what the framework actually does on this TFM.
    /// </remarks>
    [Fact]
    public void Battery_Table()
    {
        var links = Links();
        var mismatches = 0;

        output.WriteLine($"{Environment.Version} | label | template | LinkGenerator | ToString | match");
        foreach (var @case in Cases)
        {
            var route = Route(@case.Label);
            var expected = Describe(() => route.Path(links));
            var actual = Describe(route.ToString);
            if (expected != actual) mismatches++;
            output.WriteLine($"| {@case.Label} | {route.Template} | {expected} | {actual} | {(expected == actual ? "yes" : "NO")} |");
        }

        Assert.Equal(0, mismatches);
    }

    /// <summary>
    /// The facts R7.2 measured, pinned as literal expectations as well, so the parity test cannot pass
    /// by both sides drifting together (say, after a framework upgrade changed its encoder).
    /// </summary>
    [Theory]
    [InlineData("a@b", "/t/a@b")]
    [InlineData("a/b", "/t/a%2Fb")]
    [InlineData("a b", "/t/a%20b")]
    [InlineData("catch-all a/b/c", "/c/a/b/c")]
    [InlineData("catch-all a?b", "/c/a%3Fb")]
    [InlineData("single-star a/b", "/s/a%2Fb")]
    [InlineData("optional omitted", "/api/opt")]
    [InlineData("query", "/t/1?q=a%20b&page=2")]
    [InlineData("defaulted omitted", "/d")]
    public void MeasuredFacts_HoldLiterally(string label, string expected)
    {
        Assert.Equal(expected, Route(label).ToString());
        Assert.Equal(expected, Route(label).Path(Links()));
    }

    /// <summary>
    /// Values are formatted with the invariant culture, whatever the current culture is — a
    /// <c>1,5</c> in a Dutch process would be a different URL from the one the server generates.
    /// </summary>
    [Fact]
    public void Formatting_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-BE");
            Assert.Equal("/t/1.5", Route("double").ToString());
            Assert.Equal(Route("double").Path(Links()), Route("double").ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// <see cref="EndpointRoute.Path(LinkGenerator)"/> throws, naming the endpoint, where
    /// <see cref="LinkGenerator"/> would silently return <see langword="null"/>: an unknown name, or a
    /// missing required value.
    /// </summary>
    [Fact]
    public void Path_Throws_WhereLinkGeneratorReturnsNull()
    {
        var links = Links();

        var unknown = new EndpointRoute("NoSuchEndpoint", "/nowhere", new RouteValueDictionary());
        Assert.Null(links.GetPathByName("NoSuchEndpoint", new RouteValueDictionary()));
        Assert.Contains("NoSuchEndpoint", Assert.Throws<InvalidOperationException>(() => unknown.Path(links)).Message);

        var missing = new EndpointRoute("Plain", "/t/{v}", new RouteValueDictionary());
        Assert.Throws<InvalidOperationException>(() => missing.Path(links));
        Assert.Throws<InvalidOperationException>(() => missing.ToString());

        var empty = new EndpointRoute("Plain", "/t/{v}", new RouteValueDictionary { ["v"] = "" });
        Assert.Throws<InvalidOperationException>(() => empty.Path(links));
        Assert.Throws<InvalidOperationException>(() => (string)empty);
    }

    /// <summary>
    /// The <see cref="HttpContext"/> overload honours <c>PathBase</c> — and does not borrow the
    /// current request's route values to fill a token the link left out.
    /// </summary>
    [Fact]
    public void Path_WithHttpContext_HonoursPathBase_WithoutAmbientValues()
    {
        var services = new ServiceCollection().AddSingleton(Links()).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.PathBase = "/app";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.test");
        context.Request.RouteValues["id"] = 99;

        Assert.Equal("/app/api/opt", Route("optional omitted").Path(context));
        Assert.Equal("/app/t/a@b", Route("a@b").Path(context));
        Assert.Equal("https://example.test/app/t/42", Route("int").Uri(context));
    }

    /// <summary>
    /// The implicit conversion to <see cref="string"/> is what lets <c>Results.Created</c> take a link
    /// unchanged; it must be the same string as <see cref="EndpointRoute.ToString"/>.
    /// </summary>
    [Fact]
    public void ImplicitConversion_IsToString()
    {
        string path = Route("a@b");
        Assert.Equal("/t/a@b", path);

        Assert.Equal("/t/42", TypedResults.Created(Route("int")).Location);
    }
}
