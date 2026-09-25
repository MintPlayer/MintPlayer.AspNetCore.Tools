using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

// RS1035 bans file IO in analyzers. This is a test host, not an analyzer — it only inherits the
// rule from MintPlayer.SourceGenerators.Tools' build props — and reading the sample TestApp from
// disk is the point of TheTestApp_ProducesNoWarningOrErrorFromTheGenerator.
#pragma warning disable RS1035

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// The route diagnostics of milestone M6: MPEP007 (duplicate verb and route), MPEP008 (route token
/// with no bound property), MPEP009 (bound property not in the route), MPEP010 (path repeats its
/// group prefix), MPEP011 (path not constant), MPEP016 (group never joined) and MPEP018 (arity-1 type
/// argument that looks like a request).
/// </summary>
/// <remarks>
/// <para>
/// Each rule is pinned from both sides, because both failure modes are real. A rule that does not
/// fire leaves the defect it exists for — an ambiguous-match 500, a 400 on every request, a silent
/// 404 — invisible until production. A rule that fires where the matcher is actually fine is worse
/// than no rule: <c>/users/me</c> beside <c>/users/{id}</c> is correct routing (a literal beats a
/// parameter), and a diagnostic there would train consumers to suppress the id wholesale.
/// </para>
/// <para>
/// The route checks are opportunistic (PRD R3.2): an unrecoverable <c>Path</c> or <c>Prefix</c> must
/// make them silent, never guess. And none of them suppresses emission, so compiling a fixture with
/// what the generator emitted must not add any <c>CS</c> error beside them (R3.4).
/// </para>
/// </remarks>
public class RouteDiagnosticTests
{
    private const string Preamble = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;

        public record UserResponse(int Id, string Name);
        """;

    private static readonly string[] RouteIds = ["MPEP007", "MPEP008", "MPEP009", "MPEP010", "MPEP011", "MPEP016", "MPEP018"];

    private static string Fixture(string body) => Preamble + "\n\n" + body;

    /// <summary>A raw endpoint: no bound-property requirements, one verb interface or a custom <c>Methods</c>.</summary>
    private static string Raw(string name, string path, string contract = "IGetEndpoint", string members = "", string attributes = "") => $$"""
        {{attributes}}
        public class {{name}} : {{contract}}
        {
            public static string Path => {{path}};
            {{members}}
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }
        """;

    /// <summary>A response-only typed GET — the level whose handler cannot reach a route value any other way.</summary>
    private static string Typed(string name, string path, string members = "", string attributes = "") => $$"""
        {{attributes}}
        public partial class {{name}} : IGetEndpoint<UserResponse>
        {
            public static string Path => {{path}};
            {{members}}
            public override Task<IResult> HandleAsync(CancellationToken ct)
                => Task.FromResult(Results.Ok(new UserResponse(1, "x")));
        }
        """;

    private static string Group(string name, string prefix, string attributes = "") => $$"""
        {{attributes}}
        public class {{name}} : IEndpointGroup
        {
            public static string Prefix => {{prefix}};
        }
        """;

    private static string Q(string literal) => "\"" + literal + "\"";

    private static string TextAt(Location location)
        => location.SourceTree!.GetText().ToString(location.SourceSpan);

    private static Diagnostic[] Reported(string source, string id)
        => [.. EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics.Where(d => d.Id == id)];

    private static Diagnostic[] RouteDiagnostics(string source)
        => [.. EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics.Where(d => RouteIds.Contains(d.Id))];

    /// <summary>
    /// R3.4: the full compile — fixture plus everything the generator emitted — has no error other
    /// than the ones named. A route diagnostic that suppressed emission would show up here as a
    /// <c>CS0534</c>/<c>CS0115</c> in the consumer's own file.
    /// </summary>
    private static void AssertNoCascade(string source, params string[] allowedErrorIds)
    {
        var errors = EndpointGeneratorHarness.RunAndCompile("Fixtures", source)
            .Where(d => d.Severity == DiagnosticSeverity.Error && !allowedErrorIds.Contains(d.Id))
            .ToArray();

        Assert.True(errors.Length == 0, "unexpected errors: " + string.Join(" | ", errors.Select(e => $"{e.Id}: {e.GetMessage()}")));
    }

    // ---- MPEP007 --------------------------------------------------------------------------------

    /// <summary>
    /// <c>/a/{id}</c> against <c>/a/{key}</c> is a measured ambiguous-match 500: the parameter name is
    /// not a distinction to the matcher.
    /// </summary>
    /// <remarks>
    /// Pins the whole contract once: id, Warning severity (R3.6 — a wrong matcher model must not block
    /// a build), reported on the <i>later</i> endpoint in ordinal order, naming the earlier one, the
    /// shared verb and the route.
    /// </remarks>
    [Fact]
    public void MPEP007_ParameterNamesThatDiffer_IsADuplicate_ReportedOnTheLaterEndpoint()
    {
        var source = Fixture(
            Raw("FirstEndpoint", Q("/a/{id}")) + "\n" +
            Raw("SecondEndpoint", Q("/a/{key}")));

        var diagnostic = Assert.Single(Reported(source, "MPEP007"));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("SecondEndpoint", TextAt(diagnostic.Location));
        Assert.Equal(
            "Endpoint 'SecondEndpoint' answers GET on '/a/{key}', which endpoint 'FirstEndpoint' already answers; every such request fails at run time with an ambiguous-match 500",
            diagnostic.GetMessage());
        AssertNoCascade(source);
    }

    /// <summary>Routing is case-insensitive: <c>/Case</c> and <c>/case</c> are a measured 500.</summary>
    [Fact]
    public void MPEP007_RoutesDifferingOnlyInCase_AreADuplicate()
    {
        var source = Fixture(
            Raw("FirstEndpoint", Q("/Case")) + "\n" +
            Raw("SecondEndpoint", Q("/case")));

        Assert.Single(Reported(source, "MPEP007"));
    }

    /// <summary>
    /// A group <c>/api/users</c> with a member at <c>/</c> composes to <c>/api/users/</c>, which the
    /// matcher does not distinguish from a literal <c>/api/users</c> — a measured 500 that is only
    /// visible on the <i>composed</i> route.
    /// </summary>
    [Fact]
    public void MPEP007_GroupRootAgainstTheSameLiteral_IsADuplicate()
    {
        var source = Fixture(
            Group("UsersGroup", Q("/api/users")) + "\n" +
            Raw("ListUsers", Q("/"), attributes: "[MemberOf<UsersGroup>]") + "\n" +
            Raw("LegacyListUsers", Q("/api/users")));

        var diagnostic = Assert.Single(Reported(source, "MPEP007"));
        Assert.Equal("ListUsers", TextAt(diagnostic.Location));
        Assert.Contains("'/api/users/'", diagnostic.GetMessage());
        Assert.Contains("'LegacyListUsers'", diagnostic.GetMessage());
    }

    /// <summary>
    /// The measured partial overlap — <c>["GET","HEAD"]</c> against <c>["HEAD","OPTIONS"]</c> — where GET
    /// and OPTIONS answer 200 and every HEAD is a 500. ASP0022 misses it; a literal <c>Methods</c>
    /// makes it visible here, and only the shared verb is named.
    /// </summary>
    [Fact]
    public void MPEP007_PartialVerbOverlapOfLiteralMethods_NamesOnlyTheSharedVerb()
    {
        var source = Fixture(
            Raw("FirstEndpoint", Q("/probe"), "IEndpoint", """public static IEnumerable<string> Methods => ["GET", "HEAD"];""") + "\n" +
            Raw("SecondEndpoint", Q("/probe"), "IEndpoint", """public static IEnumerable<string> Methods => new[] { "head", "OPTIONS" };"""));

        var diagnostic = Assert.Single(Reported(source, "MPEP007"));
        Assert.StartsWith("Endpoint 'SecondEndpoint' answers HEAD on '/probe'", diagnostic.GetMessage());
    }

    /// <summary>
    /// <c>HttpVerbs.Get</c> is <c>static readonly</c>, not a constant, but it is the library's own
    /// spelling of a known verb, so it must count as GET rather than as unknown.
    /// </summary>
    [Fact]
    public void MPEP007_HttpVerbsFieldAgainstAVerbInterface_IsADuplicate()
    {
        var source = Fixture(
            Raw("FirstEndpoint", Q("/x"), "IEndpoint", "public static IEnumerable<string> Methods => HttpVerbs.Get;") + "\n" +
            Raw("SecondEndpoint", Q("/x")));

        Assert.Single(Reported(source, "MPEP007"));
    }

    /// <summary>Three endpoints on one verb and route are three pairs, each reported once.</summary>
    [Fact]
    public void MPEP007_IsReportedOncePerCollidingPair()
    {
        var source = Fixture(
            Raw("AEndpoint", Q("/same")) + "\n" +
            Raw("BEndpoint", Q("/same")) + "\n" +
            Raw("CEndpoint", Q("/same")));

        var reported = Reported(source, "MPEP007");
        Assert.Equal(3, reported.Length);
        Assert.Equal(["BEndpoint", "CEndpoint", "CEndpoint"], reported.Select(d => TextAt(d.Location)).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>
    /// Pairs the matcher resolves correctly, each measured: a literal beats a parameter, a constraint
    /// disambiguates, a catch-all loses to an ordinary parameter, and different verbs never collide.
    /// </summary>
    /// <remarks>
    /// <c>/users/me</c> against <c>/users/{id}</c> is the case that matters most — reporting it would
    /// be a false positive on every codebase with a "me" route.
    /// </remarks>
    [Theory]
    [InlineData("/users/me", "IGetEndpoint", "/users/{id}", "IGetEndpoint")]
    [InlineData("/items/{id:int}", "IGetEndpoint", "/items/{slug}", "IGetEndpoint")]
    [InlineData("/api/{**rest}", "IGetEndpoint", "/api/{id}", "IGetEndpoint")]
    [InlineData("/api/{**rest}", "IGetEndpoint", "/api/things", "IGetEndpoint")]
    [InlineData("/things/{id}", "IGetEndpoint", "/things/{id}", "IDeleteEndpoint")]
    public void MPEP007_DoesNotFire_WhereTheMatcherResolvesBoth(string firstPath, string firstContract, string secondPath, string secondContract)
    {
        var source = Fixture(
            Raw("FirstEndpoint", Q(firstPath), firstContract) + "\n" +
            Raw("SecondEndpoint", Q(secondPath), secondContract));

        Assert.Empty(Reported(source, "MPEP007"));
    }

    /// <summary>
    /// A custom <c>Methods</c> that cannot be read at compile time is unknown, and unknown is "no
    /// conflict" (R3.3 adjustment) — never a guessed conflict.
    /// </summary>
    [Fact]
    public void MPEP007_DoesNotFire_ForUnrecoverableCustomVerbs()
    {
        var source = Fixture(
            Raw("FirstEndpoint", Q("/x"), "IEndpoint", """
                private static readonly string[] Verbs = ["GET"];
                public static IEnumerable<string> Methods => Verbs;
                """) + "\n" +
            Raw("SecondEndpoint", Q("/x")));

        Assert.Empty(Reported(source, "MPEP007"));
    }

    /// <summary>
    /// Two endpoints whose computed paths are equal at run time are still not compared: the route is
    /// unknown, and a route check must stay silent rather than guess.
    /// </summary>
    [Fact]
    public void MPEP007_DoesNotFire_WhenThePathIsNotConstant()
    {
        var source = Fixture("""
            public static class Segments { public static readonly string Name = "x"; }
            """ + "\n" +
            Raw("FirstEndpoint", "$\"/{Segments.Name}\"") + "\n" +
            Raw("SecondEndpoint", "$\"/{Segments.Name}\""));

        Assert.Empty(Reported(source, "MPEP007"));
    }

    // ---- MPEP008 --------------------------------------------------------------------------------

    /// <summary>
    /// A typed endpoint's handler has no way to reach an unbound route value, so an unbound token is
    /// almost always a typo; the message names the keys the endpoint does bind.
    /// </summary>
    [Fact]
    public void MPEP008_UnboundTokenOnATypedEndpoint_Warns_NamingTheBoundKeys()
    {
        var source = Fixture(Typed("GetThing", Q("/things/{id}/{extra}"), "[RouteParam] public int Id { get; set; }"));

        var diagnostic = Assert.Single(Reported(source, "MPEP008"));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("GetThing", TextAt(diagnostic.Location));
        Assert.Equal(
            "Route parameter '{extra}' in the Path of endpoint 'GetThing' is not bound to any property; add [RouteParam] to a property named 'extra' (or [RouteParam(\"extra\")] on another), otherwise the handler cannot read it; the endpoint's [RouteParam] properties bind 'Id'",
            diagnostic.GetMessage());
        AssertNoCascade(source);
    }

    /// <summary>The body levels are typed too, and checked the same way.</summary>
    [Fact]
    public void MPEP008_FiresForABodyEndpoint()
    {
        var source = Fixture("""
            public record ThingBody(string Name);

            public partial class PutThing : IPutEndpoint<ThingBody>
            {
                public static string Path => "/things/{id}";

                public override Task<IResult> HandleAsync(ThingBody request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """);

        Assert.Single(Reported(source, "MPEP008"));
    }

    /// <summary>A raw endpoint may read <c>RouteValues</c> by hand, so an unbound token proves nothing.</summary>
    [Fact]
    public void MPEP008_DoesNotFire_ForARawEndpoint()
    {
        Assert.Empty(Reported(Fixture(Raw("GetThing", Q("/things/{id}"))), "MPEP008"));
    }

    /// <summary>Route values match case-insensitively at run time, so <c>{ID}</c> is bound by <c>Id</c>.</summary>
    [Fact]
    public void MPEP008_DoesNotFire_WhenTheTokenDiffersFromTheKeyOnlyInCase()
    {
        var source = Fixture(Typed("GetThing", Q("/things/{ID}"), "[RouteParam] public int Id { get; set; }"));

        Assert.Empty(Reported(source, "MPEP008"));
    }

    /// <summary>
    /// A token contributed by the group prefix is the group's business; MPEP008 reads the endpoint's
    /// own <c>Path</c>, so the endpoint is not blamed for it.
    /// </summary>
    [Fact]
    public void MPEP008_DoesNotBlameTheEndpointForAGroupPrefixToken()
    {
        var source = Fixture(
            Group("TenantGroup", Q("/tenants/{tenant}")) + "\n" +
            Typed("GetThing", Q("/things/{id}"), "[RouteParam] public int Id { get; set; }", "[MemberOf<TenantGroup>]"));

        Assert.Empty(Reported(source, "MPEP008"));
    }

    // ---- MPEP009 --------------------------------------------------------------------------------

    /// <summary>
    /// Prototype A's control C3: <c>Path =&gt; "/{identifier}"</c> with <c>[RouteParam] int Id</c> compiles
    /// clean and answers 400 on every request, forever — so it is an Error, on the property.
    /// </summary>
    [Fact]
    public void MPEP009_BoundPropertyMatchingNoToken_IsAnError_OnTheProperty()
    {
        var source = Fixture(Typed("GetThing", Q("/{identifier}"), "[RouteParam] public int Id { get; set; }"));

        var diagnostic = Assert.Single(Reported(source, "MPEP009"));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Id", TextAt(diagnostic.Location));
        Assert.Equal(
            "Property 'Id' is bound to route parameter 'Id', but the route '/{identifier}' of endpoint 'GetThing' has no such parameter; its parameters are '{identifier}'",
            diagnostic.GetMessage());

        // The unbound '{identifier}' is MPEP008 on top, which is accurate and only a Warning.
        Assert.Single(Reported(source, "MPEP008"));
        AssertNoCascade(source, "MPEP009");
    }

    /// <summary>A raw endpoint's bound property is bound the same way and fails the same way.</summary>
    [Fact]
    public void MPEP009_FiresForARawEndpoint_NamingARouteWithNoParameters()
    {
        var source = Fixture("""
            public partial class DeleteThing : IDeleteEndpoint
            {
                public static string Path => "/things";

                [RouteParam] public int Id { get; set; }

                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.NoContent());
            }
            """);

        var diagnostic = Assert.Single(Reported(source, "MPEP009"));
        Assert.EndsWith("has no such parameter; it has no route parameters", diagnostic.GetMessage());
    }

    /// <summary>
    /// A token supplied by the group prefix is present in the route the request spells, so a property
    /// bound to it is fine — MPEP009 checks the composed route.
    /// </summary>
    [Fact]
    public void MPEP009_DoesNotFire_WhenTheTokenComesFromTheGroupPrefix()
    {
        var source = Fixture(
            Group("TenantGroup", Q("/tenants/{tenantId}")) + "\n" +
            Typed("GetThing", Q("/"), "[RouteParam] public string TenantId { get; set; } = \"\";", "[MemberOf<TenantGroup>]"));

        Assert.Empty(RouteDiagnostics(source));
        AssertNoCascade(source);
    }

    /// <summary>
    /// With an unrecoverable prefix the composed route is unknown, so MPEP009 stays silent rather
    /// than report a parameter as missing from a route it cannot see.
    /// </summary>
    [Fact]
    public void MPEP009_DoesNotFire_WhenTheGroupPrefixIsNotConstant()
    {
        var source = Fixture("""
            public static class Segments { public static readonly string Tenant = "{tenantId}"; }

            public class TenantGroup : IEndpointGroup
            {
                public static string Prefix => $"/tenants/{Segments.Tenant}";
            }
            """ + "\n" +
            Typed("GetThing", Q("/"), "[RouteParam] public string TenantId { get; set; } = \"\";", "[MemberOf<TenantGroup>]"));

        Assert.Empty(RouteDiagnostics(source));
    }

    // ---- MPEP010 --------------------------------------------------------------------------------

    /// <summary>
    /// A group-relative path written absolutely maps at the prefix twice and 404s silently at the URL
    /// the author meant.
    /// </summary>
    [Fact]
    public void MPEP010_PathRepeatingItsGroupPrefix_Warns_WithTheRouteItActuallyMapsAt()
    {
        var source = Fixture(
            Group("UsersGroup", Q("/api/users")) + "\n" +
            Raw("GetUser", Q("/api/users/{id}"), attributes: "[MemberOf<UsersGroup>]"));

        var diagnostic = Assert.Single(Reported(source, "MPEP010"));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("GetUser", TextAt(diagnostic.Location));
        Assert.Equal(
            "The Path '/api/users/{id}' of endpoint 'GetUser' already begins with its group prefix '/api/users', so the endpoint is mapped at '/api/users/api/users/{id}'; a Path is relative to its group",
            diagnostic.GetMessage());
        AssertNoCascade(source);
    }

    /// <summary>
    /// The prefix compared is the <i>composed</i> one of the whole chain, and a path equal to it (not
    /// only one extending it) repeats it.
    /// </summary>
    [Fact]
    public void MPEP010_FiresAgainstTheComposedPrefixOfANestedChain()
    {
        var source = Fixture(
            Group("ApiGroup", Q("/api")) + "\n" +
            Group("UsersGroup", Q("/users"), "[MemberOf<ApiGroup>]") + "\n" +
            Raw("ListUsers", Q("/API/Users/"), attributes: "[MemberOf<UsersGroup>]"));

        Assert.Single(Reported(source, "MPEP010"));
    }

    /// <summary>
    /// Only at a segment boundary: a prefix of <c>/api</c> does not claim a path of <c>/apiary</c>, and a
    /// root prefix of <c>/</c> claims nothing.
    /// </summary>
    [Theory]
    [InlineData("/api", "/apiary")]
    [InlineData("/", "/things")]
    [InlineData("/api", "/users/api")]
    public void MPEP010_DoesNotFire_OffASegmentBoundary(string prefix, string path)
    {
        var source = Fixture(
            Group("SomeGroup", Q(prefix)) + "\n" +
            Raw("SomeEndpoint", Q(path), attributes: "[MemberOf<SomeGroup>]"));

        Assert.Empty(Reported(source, "MPEP010"));
    }

    // ---- MPEP011 --------------------------------------------------------------------------------

    /// <summary>
    /// A computed <c>Path</c> is Info, so it is invisible by default, and every other route check on
    /// that endpoint is silent — even where the unbound property would otherwise be MPEP009 and the
    /// token MPEP008.
    /// </summary>
    [Fact]
    public void MPEP011_NonConstantPath_IsInfo_AndSilencesTheOtherRouteChecks()
    {
        var source = Fixture("""
            public static class Segments { public static readonly string Name = "{identifier}"; }
            """ + "\n" +
            Typed("GetThing", "$\"/things/{Segments.Name}\"", "[RouteParam] public int Id { get; set; }"));

        var diagnostic = Assert.Single(RouteDiagnostics(source));

        Assert.Equal("MPEP011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal("GetThing", TextAt(diagnostic.Location));
        Assert.Equal(
            "The Path of endpoint 'GetThing' is not a compile-time constant, so its route checks (MPEP007-MPEP010) are skipped",
            diagnostic.GetMessage());
        AssertNoCascade(source);
    }

    /// <summary>A constant folded through <c>const</c> interpolation is still a constant.</summary>
    [Fact]
    public void MPEP011_DoesNotFire_ForAConstantInterpolation()
    {
        var source = Fixture("""
            public static class Segments { public const string Name = "things"; }
            """ + "\n" +
            Raw("GetThing", "$\"/{Segments.Name}\""));

        Assert.Empty(Reported(source, "MPEP011"));
    }

    // ---- MPEP016 --------------------------------------------------------------------------------

    /// <summary>A group nobody joins gets no <c>MapGroup</c>, so its prefix silently never applies.</summary>
    [Fact]
    public void MPEP016_GroupNeverJoined_IsInfo_OnTheGroup()
    {
        var source = Fixture(
            Group("OrphanGroup", Q("/orphans")) + "\n" +
            Raw("Health", Q("/health")));

        var diagnostic = Assert.Single(Reported(source, "MPEP016"));

        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal("OrphanGroup", TextAt(diagnostic.Location));
        Assert.Equal(
            "Endpoint group 'OrphanGroup' is not joined by any endpoint, directly or through a nested group, so it is not mapped",
            diagnostic.GetMessage());
    }

    /// <summary>A root group joined only through a child group is joined.</summary>
    [Fact]
    public void MPEP016_DoesNotFire_ForAGroupJoinedOnlyThroughAChild()
    {
        var source = Fixture(
            Group("ApiGroup", Q("/api")) + "\n" +
            Group("UsersGroup", Q("/users"), "[MemberOf<ApiGroup>]") + "\n" +
            Raw("ListUsers", Q("/"), attributes: "[MemberOf<UsersGroup>]"));

        Assert.Empty(Reported(source, "MPEP016"));
    }

    // ---- MPEP018 --------------------------------------------------------------------------------

    /// <summary>
    /// <c>IGetEndpoint&lt;T&gt;</c>'s single argument is the RESPONSE; a <c>*Request</c> there usually means
    /// the two-argument form was meant, and the message says which.
    /// </summary>
    [Theory]
    [InlineData("GetThingRequest", "IGetEndpoint")]
    [InlineData("DeleteThingCommand", "IDeleteEndpoint")]
    [InlineData("ThingBody", "IGetEndpoint")]
    public void MPEP018_ArityOneArgumentNamedLikeARequest_IsInfo(string typeName, string contract)
    {
        var source = Fixture($$"""
            public record {{typeName}}(int Id);

            public partial class ThingEndpoint : {{contract}}<{{typeName}}>
            {
                public static string Path => "/things";

                public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok());
            }
            """);

        var diagnostic = Assert.Single(Reported(source, "MPEP018"));

        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal("ThingEndpoint", TextAt(diagnostic.Location));
        Assert.Equal(
            $"Endpoint 'ThingEndpoint' uses '{typeName}' as the single type argument of {contract}<T>, which is the RESPONSE type; if '{typeName}' is meant to be the request, use {contract}<{typeName}, TResponse>",
            diagnostic.GetMessage());
        AssertNoCascade(source);
    }

    /// <summary>
    /// A response-named type, the two-argument form, and a generic whose <i>argument</i> is
    /// request-named are all fine.
    /// </summary>
    [Fact]
    public void MPEP018_DoesNotFire_ForResponsesOrTheTwoArgumentForm()
    {
        var source = Fixture("""
            public record GetThingRequest(int Id);

            public partial class GetOne : IGetEndpoint<UserResponse>
            {
                public static string Path => "/one";
                public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok());
            }

            public partial class GetMany : IGetEndpoint<List<GetThingRequest>>
            {
                public static string Path => "/many";
                public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok());
            }
            """);

        Assert.Empty(Reported(source, "MPEP018"));
    }

    // ---- The sample stays clean ------------------------------------------------------------------

    /// <summary>
    /// The shared corpus — every shape the library claims to support — produces no Warning or Error
    /// from this generator.
    /// </summary>
    /// <remarks>
    /// The corpus deliberately puts GET, PUT, PATCH and DELETE on <c>/api/users/{id}</c> and a
    /// catch-all beside literal routes; a duplicate-route rule that ignored verbs or erased the
    /// catch-all marker fails here.
    /// </remarks>
    [Fact]
    public void TheCorpus_ProducesNoWarningOrErrorFromTheGenerator()
    {
        var noisy = EndpointGeneratorHarness.Run(FixtureSources.Ns, FixtureSources.Corpus).Diagnostics
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToArray();

        Assert.True(noisy.Length == 0, string.Join(" | ", noisy.Select(d => $"{d.Id}: {d.GetMessage()}")));
    }

    /// <summary>
    /// The sample TestApp itself — read from disk, so it cannot drift from the corpus unnoticed —
    /// produces no Warning or Error from this generator.
    /// </summary>
    /// <remarks>
    /// The TestApp is a Web SDK project and leans on implicit usings, so those are supplied as a
    /// global-usings file; without them nothing would bind and the test would pass vacuously. The
    /// generated mapping is asserted to name the sample's endpoints for the same reason.
    /// </remarks>
    [Fact]
    public void TheTestApp_ProducesNoWarningOrErrorFromTheGenerator()
    {
        // The TestApp closes the TestLibrary's generic endpoints (issue #34), so the library is compiled
        // through the generator first and referenced as metadata, exactly as the real build does.
        var library = SourcesOf("MintPlayer.AspNetCore.Endpoints.TestLibrary");
        var libraryCompilation = EndpointGeneratorHarness.CreateCompilation("MintPlayer.AspNetCore.Endpoints.TestLibrary", library);
        Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver
            .Create(new EndpointGenerator())
            .RunGeneratorsAndUpdateCompilation(libraryCompilation, out var libraryUpdated, out var libraryDiagnostics);
        Assert.DoesNotContain(libraryDiagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
        Assert.DoesNotContain(libraryUpdated.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

        using var image = new MemoryStream();
        Assert.True(libraryUpdated.Emit(image).Success);

        var compilation = EndpointGeneratorHarness
            .CreateCompilation("MintPlayer.AspNetCore.Endpoints.TestApp", SourcesOf("MintPlayer.AspNetCore.Endpoints.TestApp"))
            .AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        var result = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver
            .Create(new EndpointGenerator())
            .RunGenerators(compilation)
            .GetRunResult();

        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));
        Assert.Contains("GetUser", generated);
        Assert.Contains("PreflightEndpoint", generated);
        Assert.Contains("Passkeys_AppUser", generated);

        var noisy = result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
        Assert.True(noisy.Length == 0, string.Join(" | ", noisy.Select(d => $"{d.Id}: {d.GetMessage()}")));
    }

    private static string[] SourcesOf(string project)
    {
        var directory = Path.Combine(RepositoryRoot(), "Endpoints", project);
        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(directory, file))
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .Prepend(WebSdkImplicitUsings)
            .ToArray();
    }

    private const string WebSdkImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Net.Http.Json;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Microsoft.AspNetCore.Builder;
        global using Microsoft.AspNetCore.Hosting;
        global using Microsoft.AspNetCore.Http;
        global using Microsoft.AspNetCore.Routing;
        global using Microsoft.Extensions.Configuration;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Hosting;
        global using Microsoft.Extensions.Logging;
        """;

    private static bool IsBuildOutput(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first is "bin" or "obj";
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MintPlayer.AspNetCore.Tools.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not find MintPlayer.AspNetCore.Tools.sln above " + AppContext.BaseDirectory);
    }
}
