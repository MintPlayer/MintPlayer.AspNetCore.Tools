using System.Reflection;
using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// The generated <c>Routes</c> class (M8, PRD R7.1): its visibility, its shape, the names it
/// derives, the parameter types it chooses, and what it leaves out.
/// </summary>
/// <remarks>
/// Most assertions load the emitted assembly and read the <c>Routes</c> tree by reflection rather
/// than matching text: a method's parameter types, optionality and constant values are what a
/// consumer compiles against, and text matching would pass on a method that does not compile. Every
/// fixture also goes through <see cref="EndpointGeneratorHarness.RunAndCompile(string, string[])"/>,
/// because a malformed link class is an error in a file the consumer cannot edit.
/// </remarks>
public class TypedLinkEmissionTests
{
    private const string RoutesFile = "EndpointRoutes.g.cs";

    private const string Preamble = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;
        """;

    private static string Fixture(string body) => Preamble + "\n\n" + body;

    private static string? RoutesText(params string[] sources)
        => EndpointGeneratorHarness.Run("Fixtures", sources).GeneratedTrees
            .FirstOrDefault(tree => tree.FilePath.EndsWith(RoutesFile, StringComparison.Ordinal))
            ?.ToString();

    private static string MappingText(params string[] sources)
        => EndpointGeneratorHarness.Run("Fixtures", sources).GeneratedTrees
            .Single(tree => tree.FilePath.EndsWith("EndpointMapping.g.cs", StringComparison.Ordinal))
            .ToString();

    private static Diagnostic[] Errors(IEnumerable<Diagnostic> diagnostics)
        => [.. diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];

    private static void AssertCompilesClean(params string[] sources)
    {
        var errors = Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", sources));
        Assert.True(errors.Length == 0, string.Join(" | ", errors.Select(e => $"{e.Id}: {e.GetMessage()}")));
    }

    /// <summary>The <c>Routes</c> type, or one of its nested classes by dotted path (<c>"Api.Users"</c>).</summary>
    private static Type RoutesClass(Assembly assembly, string path = "")
    {
        var type = assembly.GetType("MintPlayer.AspNetCore.Endpoints.Generated.Routes")
            ?? throw new InvalidOperationException("No Routes class was emitted.");

        foreach (var name in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            type = type.GetNestedType(name, BindingFlags.Public)
                ?? throw new InvalidOperationException($"No nested class '{name}' in '{type.FullName}'. Has: {string.Join(", ", type.GetNestedTypes().Select(t => t.Name))}");

        return type;
    }

    private static string[] NestedNames(Type type)
        => [.. type.GetNestedTypes(BindingFlags.Public).Select(nested => nested.Name).OrderBy(name => name, StringComparer.Ordinal)];

    private static string Template(Type container, string endpointName)
        => (string)container.GetField(endpointName + "Template", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;

    private static EndpointRoute Invoke(Type container, string method, params object?[] arguments)
        => (EndpointRoute)container.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, arguments)!;

    // ---- Visibility and file shape --------------------------------------------------------------

    /// <summary>
    /// <c>Routes</c> is <c>internal</c>, so two endpoint assemblies can reference each other.
    /// </summary>
    /// <remarks>
    /// Every endpoint assembly emits a type with this fixed name in this fixed namespace. Made
    /// <c>public</c>, it would be <c>CS0433</c> (the type exists in both assemblies) at the first
    /// reference from one endpoint project to another — found by the consumer, not by any test that
    /// compiles a single assembly. So the modifier is pinned here directly.
    /// </remarks>
    [Fact]
    public void Routes_IsInternal()
    {
        var assembly = EndpointGeneratorHarness.RunAndLoad("Fixtures.Links.Internal", FixtureSources.Corpus);
        var routes = RoutesClass(assembly);

        Assert.True(routes.IsNotPublic, "Routes must be internal");
        Assert.True(routes.IsAbstract && routes.IsSealed, "Routes must be static");
        Assert.Contains("internal static class Routes", RoutesText(FixtureSources.Corpus));
    }

    /// <summary>
    /// The routes file has no <c>using</c> directive, and the corpus compiles with it — with and
    /// without the Web SDK's implicit usings.
    /// </summary>
    /// <remarks>
    /// Same rule as the mapping file (R6.9): a <c>using</c> would put the consumer's import set on a
    /// generated file's critical path, and a project with implicit usings off would fail to compile
    /// a file it cannot edit.
    /// </remarks>
    [Fact]
    public void RoutesFile_HasNoUsings_AndCompilesWithoutImplicitUsings()
    {
        var text = RoutesText(FixtureSources.Corpus)!;
        Assert.DoesNotContain(text.Split('\n'), line => line.TrimStart().StartsWith("using ", StringComparison.Ordinal));

        AssertCompilesClean(FixtureSources.Corpus);

        var bare = EndpointGeneratorHarness.CreateCompilation("Fixtures", [FixtureSources.Corpus], includeImplicitUsings: false);
        var errors = Errors(EndpointGeneratorHarness.RunAndCompile(bare));
        Assert.True(errors.Length == 0, string.Join(" | ", errors.Select(e => $"{e.Id}: {e.GetMessage()}")));
    }

    /// <summary>
    /// The emitted routes file itself carries no diagnostic at all — not even a warning — so a
    /// consumer with <c>TreatWarningsAsErrors</c> is not broken by it.
    /// </summary>
    [Fact]
    public void RoutesFile_ProducesNoDiagnosticsOfItsOwn()
    {
        var diagnostics = EndpointGeneratorHarness.RunAndCompile("Fixtures", FixtureSources.Corpus);

        var inRoutes = diagnostics
            .Where(d => d.Location.SourceTree?.FilePath.EndsWith(RoutesFile, StringComparison.Ordinal) == true)
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToArray();
        Assert.True(inRoutes.Length == 0, string.Join(" | ", inRoutes.Select(e => $"{e.Id}: {e.GetMessage()}")));
    }

    /// <summary>No endpoint that can be linked to means no file, rather than an empty class.</summary>
    [Fact]
    public void NoLinkableEndpoint_EmitsNoRoutesFile()
    {
        var source = Fixture("""
            public class Computed : IGetEndpoint
            {
                private static readonly string Suffix = "x";
                public static string Path => "/computed/" + Suffix;
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """);

        Assert.Null(RoutesText(source));
        AssertCompilesClean(source);
    }

    // ---- The tree ------------------------------------------------------------------------------

    /// <summary>
    /// Nested classes mirror the group tree, a group's class drops one trailing <c>Api</c> or
    /// <c>Group</c>, and ungrouped endpoints sit on <c>Routes</c> itself.
    /// </summary>
    /// <remarks>
    /// Also pins that the tree contains only groups that have a link in them: the corpus declares no
    /// empty group, so an extra nested class here would mean the tree was built from declarations
    /// rather than from links.
    /// </remarks>
    [Fact]
    public void Tree_MirrorsGroups_WithSuffixesStripped()
    {
        var assembly = EndpointGeneratorHarness.RunAndLoad("Fixtures.Links.Tree", FixtureSources.Corpus);
        var routes = RoutesClass(assembly);

        Assert.Equal(["Api"], NestedNames(routes));
        Assert.Equal(["Products", "Users"], NestedNames(RoutesClass(assembly, "Api")));

        Assert.NotNull(routes.GetMethod("HealthCheck"));
        Assert.NotNull(routes.GetMethod("PreflightEndpoint"));
        Assert.NotNull(RoutesClass(assembly, "Api.Users").GetMethod("GetUser"));
        Assert.NotNull(RoutesClass(assembly, "Api.Products").GetMethod("ListProducts"));
    }

    /// <summary>
    /// The suffix is kept when removing it would leave nothing, or would collide with a sibling
    /// group or with a link in the same class; a residual clash between two same-named groups in
    /// different namespaces is resolved deterministically.
    /// </summary>
    /// <remarks>
    /// Each of these, left unhandled, is a <c>CS0102</c> (duplicate member) or <c>CS0542</c> (member
    /// named after its enclosing type) in the generated file — an error the consumer cannot fix
    /// there, and whose only real fix would be renaming their own types.
    /// </remarks>
    [Fact]
    public void GroupNames_FallBackToTheFullName_OnEveryKindOfCollision()
    {
        var source = Preamble.Replace("namespace Fixtures;", "") + """

            namespace Fixtures
            {
                public class Api : IEndpointGroup { public static string Prefix => "/bare"; }
                public class Group : IEndpointGroup { public static string Prefix => "/group"; }
                public class UsersApi : IEndpointGroup { public static string Prefix => "/users-api"; }
                public class UsersGroup : IEndpointGroup { public static string Prefix => "/users-group"; }
                public class StatusApi : IEndpointGroup { public static string Prefix => "/status-api"; }
                public class OrdersApi : IEndpointGroup { public static string Prefix => "/orders-a"; }

                [MemberOf<Api>] public class InBare : IGetEndpoint { public static string Path => "/"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }
                [MemberOf<Group>] public class InGroup : IGetEndpoint { public static string Path => "/"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }
                [MemberOf<UsersApi>] public class InUsersApi : IGetEndpoint { public static string Path => "/"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }
                [MemberOf<UsersGroup>] public class InUsersGroup : IGetEndpoint { public static string Path => "/"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }
                [MemberOf<StatusApi>] public class InStatus : IGetEndpoint { public static string Path => "/"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }
                [MemberOf<OrdersApi>] public class InOrdersA : IGetEndpoint { public static string Path => "/"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }

                // An ungrouped endpoint whose name is what StatusApi would strip to.
                public class Status : IGetEndpoint { public static string Path => "/status"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }
            }

            namespace Fixtures.Other
            {
                public class OrdersApi : IEndpointGroup { public static string Prefix => "/orders-b"; }
                [MemberOf<OrdersApi>] public class InOrdersB : IGetEndpoint { public static string Path => "/"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }
            }
            """;

        AssertCompilesClean(source);
        var routes = RoutesClass(EndpointGeneratorHarness.RunAndLoad("Fixtures.Links.Collisions", source));

        Assert.Equal(
            [
                "Api",              // strips to "" — the full name is kept
                "Group",            // strips to "" — the full name is kept
                "OrdersApi",        // Fixtures.OrdersApi: "Orders" is shared with Fixtures.Other.OrdersApi
                "OrdersApiRoutes",  // Fixtures.Other.OrdersApi: the full name is taken too; ordinal order decides
                "StatusApi",        // "Status" is a link on Routes
                "UsersApi",         // "Users" is shared with UsersGroup
                "UsersGroup",
            ],
            NestedNames(routes));

        Assert.NotNull(routes.GetMethod("Status"));
        Assert.Equal("/orders-a/", Template(routes.GetNestedType("OrdersApi")!, "InOrdersA"));
        Assert.Equal("/orders-b/", Template(routes.GetNestedType("OrdersApiRoutes")!, "InOrdersB"));
    }

    /// <summary>
    /// An endpoint whose name is its own class's name, or already taken there, gets no link rather
    /// than a member that does not compile — and the rest of the file still compiles.
    /// </summary>
    [Fact]
    public void LinkThatCannotBeAMemberOfItsClass_IsLeftOut()
    {
        var source = Fixture("""
            // Named after the class it would live in (CS0542).
            [EndpointDescriptorName("Routes")]
            public class A : IGetEndpoint { public static string Path => "/a"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }

            // "XTemplate" is the constant another link already declares (CS0102).
            public class X : IGetEndpoint { public static string Path => "/x"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }
            public class XTemplate : IGetEndpoint { public static string Path => "/x-template"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }

            // Not an identifier: still named for routing, but no method can carry it.
            [EndpointDescriptorName("get-thing")]
            public class B : IGetEndpoint { public static string Path => "/b"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }
            """);

        AssertCompilesClean(source);
        var routes = RoutesClass(EndpointGeneratorHarness.RunAndLoad("Fixtures.Links.Unrepresentable", source));

        Assert.Equal(["X"], routes.GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToArray());
        Assert.Contains("WithName(b, \"get-thing\")", MappingText(source));
    }

    // ---- Parameters -----------------------------------------------------------------------------

    private const string ParameterShapes = """
        public enum Colour { Red, Green }

        public class LangGroup : IEndpointGroup { public static string Prefix => "/{lang=en}"; }

        public record Thing(int Id);

        // Bound tokens take the property's type; the query property is always optional.
        public partial class GetThing : IGetEndpoint<Thing>
        {
            public static string Path => "/things/{id}/{colour}";
            [RouteParam] public Guid Id { get; set; }
            [RouteParam] public Colour Colour { get; set; }
            [QueryParam] public int Page { get; set; } = 1;
            [QueryParam("q")] public string? Search { get; set; }
            public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok());
        }

        // Unbound tokens are strings; {id?}, {n=1} and a catch-all are optional.
        public class Raw : IGetEndpoint
        {
            public static string Path => "/raw/{slug}/{n=1}/{**rest}";
            public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok());
        }

        public partial class Optional : IGetEndpoint
        {
            public static string Path => "/opt/{id?}";
            [RouteParam] public int? Id { get; set; }
            public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok());
        }

        // A defaulted token contributed by the group, before a required one: nullable, but it
        // cannot have a default value in C# (CS1737), so it must be passed.
        [MemberOf<LangGroup>]
        public class Localised : IGetEndpoint
        {
            public static string Path => "/items/{id}";
            public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok());
        }
        """;

    /// <summary>
    /// One parameter per token of the <b>composed</b> route, in template order, typed from the
    /// <c>[RouteParam]</c> property that binds it — then one optional parameter per
    /// <c>[QueryParam]</c>.
    /// </summary>
    /// <remarks>
    /// The type is what makes a wrong argument a compile error; <c>string</c> everywhere would compile
    /// <c>GetThing(id: "42")</c> and generate a link the endpoint then answers with 400. Optionality
    /// has to follow the template exactly: a required parameter for <c>{id?}</c> forces callers to
    /// invent a value, and an optional one for <c>{id}</c> lets them build a link that cannot be
    /// generated.
    /// </remarks>
    [Fact]
    public void Parameters_AreTypedFromBoundProperties_AndOptionalExactlyWhereTheTemplateIs()
    {
        var source = Fixture(ParameterShapes);
        AssertCompilesClean(source);
        var assembly = EndpointGeneratorHarness.RunAndLoad("Fixtures.Links.Parameters", source);
        var routes = RoutesClass(assembly);

        static string Describe(ParameterInfo parameter)
        {
            var type = Nullable.GetUnderlyingType(parameter.ParameterType) is { } underlying
                ? underlying.Name + "?"
                : parameter.ParameterType.Name + (new NullabilityInfoContext().Create(parameter).WriteState == NullabilityState.Nullable ? "?" : "");
            return $"{type} {parameter.Name}{(parameter.HasDefaultValue ? " = null" : "")}";
        }

        string Signature(Type container, string method) =>
            string.Join(", ", container.GetMethod(method)!.GetParameters().Select(Describe));

        Assert.Equal("Guid id, Colour colour, Int32? page = null, String? search = null", Signature(routes, "GetThing"));
        Assert.Equal("String slug, String? n = null, String? rest = null", Signature(routes, "Raw"));
        Assert.Equal("Int32? id = null", Signature(routes, "Optional"));
        Assert.Equal("String? lang, String id", Signature(RoutesClass(assembly, "Lang"), "Localised"));
    }

    /// <summary>
    /// A query argument is stored under its key, not its property name, and left out when null; the
    /// values reach the link as the query string.
    /// </summary>
    [Fact]
    public void Values_UseTheBindingKeys_AndOmitNullOptionals()
    {
        var assembly = EndpointGeneratorHarness.RunAndLoad("Fixtures.Links.Values", Fixture(ParameterShapes));
        var routes = RoutesClass(assembly);
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        var green = Enum.Parse(assembly.GetType("Fixtures.Colour")!, "Green");
        var link = Invoke(routes, "GetThing", id, green, null, "a b");
        Assert.Equal("GetThing", link.Name);
        Assert.Equal(["id", "colour", "q"], link.Values.Keys.ToArray());
        Assert.Equal("/things/0f8fad5b-d9cb-469f-a165-70867728950e/Green?q=a%20b", link.ToString());

        Assert.Equal("/opt", Invoke(routes, "Optional", [null]).ToString());
        Assert.Equal("/raw/s", Invoke(routes, "Raw", "s", null, null).ToString());
        Assert.Equal("/raw/s/2/a/b", Invoke(routes, "Raw", "s", "2", "a/b").ToString());
    }

    /// <summary>
    /// Each link has a <c>…Template</c> constant holding the composed route — the value the mapping
    /// registers, which the grouping tests now assert against instead of repeating the strings.
    /// </summary>
    [Fact]
    public void TemplateConstants_HoldTheComposedRoute()
    {
        var assembly = EndpointGeneratorHarness.RunAndLoad("Fixtures.Links.Templates", Fixture(ParameterShapes), FixtureSources.Corpus.Replace("namespace Fixtures;", "namespace Fixtures.Corpus;"));
        var routes = RoutesClass(assembly);

        Assert.Equal("/things/{id}/{colour}", Template(routes, "GetThing"));
        Assert.Equal("/raw/{slug}/{n=1}/{**rest}", Template(routes, "Raw"));
        Assert.Equal("/{lang=en}/items/{id}", Template(RoutesClass(assembly, "Lang"), "Localised"));
        Assert.Equal("/api/users/{id}/patch", Template(RoutesClass(assembly, "Api.Users"), "PatchUser"));
    }

    // ---- What is left out ------------------------------------------------------------------------

    /// <summary>
    /// An endpoint whose composed route cannot be recovered — a computed <c>Path</c>, or a group
    /// with a computed <c>Prefix</c> — gets no link, and MPEP011 still explains why.
    /// </summary>
    /// <remarks>
    /// The alternative is a link built from a guessed template, which compiles, and points
    /// somewhere else. PRD M8 gate: skipped, never emitted wrong.
    /// </remarks>
    [Fact]
    public void NonConstantRoute_IsSkipped_NotEmittedWrong()
    {
        var source = Fixture("""
            public class ComputedGroup : IEndpointGroup
            {
                private static readonly string Root = "/computed";
                public static string Prefix => Root;
            }

            [MemberOf<ComputedGroup>]
            public class InComputedGroup : IGetEndpoint { public static string Path => "/x"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }

            public class ComputedPath : IGetEndpoint
            {
                private static readonly string Suffix = "y";
                public static string Path => "/y/" + Suffix;
                public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok());
            }

            public class Constant : IGetEndpoint { public static string Path => "/constant"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }
            """);

        AssertCompilesClean(source);
        var routes = RoutesClass(EndpointGeneratorHarness.RunAndLoad("Fixtures.Links.NonConstant", source));

        Assert.Equal(["Constant"], routes.GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToArray());
        Assert.Empty(NestedNames(routes));
        Assert.Contains(EndpointGeneratorHarness.Run("Fixtures", source).Diagnostics, d => d.Id == "MPEP011");

        // Still mapped and still named — only the link is withheld.
        var mapping = MappingText(source);
        Assert.Contains("WithName(b, \"ComputedPath\")", mapping);
        Assert.Contains("WithName(b, \"InComputedGroup\")", mapping);
    }

    // ---- Call-site errors -----------------------------------------------------------------------

    /// <summary>
    /// A wrong argument type is <c>CS1503</c> and a missing argument is <c>CS7036</c>, at the call site.
    /// </summary>
    /// <remarks>
    /// This is the point of typed links over <c>$"/api/users/{id}"</c>: the magic string compiles with
    /// any value, and with none.
    /// </remarks>
    [Theory]
    [InlineData("Routes.Api.Users.GetUser(id: \"42\")", "CS1503")]
    [InlineData("Routes.Api.Users.GetUser()", "CS7036")]
    public void WrongOrMissingArgument_IsACompileError(string call, string expected)
    {
        var consumer = $$"""
            namespace Fixtures.Consumer;

            public static class Caller
            {
                public static string Link() => {{call}};
            }
            """;

        var errors = Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", FixtureSources.Corpus, consumer));

        var error = Assert.Single(errors);
        Assert.Equal(expected, error.Id);
    }

    /// <summary>
    /// A correct call compiles and converts to <see cref="string"/> implicitly, so
    /// <c>Results.Created(Routes…, body)</c> needs no <c>.ToString()</c>.
    /// </summary>
    [Fact]
    public void CorrectCall_CompilesAndConvertsToString()
    {
        const string consumer = """
            namespace Fixtures.Consumer;

            public static class Caller
            {
                public static string Link() => Routes.Api.Users.GetUser(id: 42);
                public static Microsoft.AspNetCore.Http.IResult Created() => Microsoft.AspNetCore.Http.Results.Created(Routes.Api.Users.GetUser(id: 42), new { });
            }
            """;

        var assembly = EndpointGeneratorHarness.RunAndLoad("Fixtures.Links.Call", FixtureSources.Corpus, consumer);

        var link = (string)assembly.GetType("Fixtures.Consumer.Caller")!.GetMethod("Link")!.Invoke(null, null)!;
        Assert.Equal("/api/users/42", link);
    }

    // ---- .WithName() ----------------------------------------------------------------------------

    /// <summary>
    /// Every mapping carries <c>WithName</c> with the effective name, in static form — so it needs no
    /// <c>using</c> — and a descriptor name overrides the class name.
    /// </summary>
    [Fact]
    public void EveryMapping_IsNamed_WithTheEffectiveName()
    {
        var source = Fixture("""
            public class Plain : IGetEndpoint { public static string Path => "/plain"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }

            [EndpointDescriptorName("Renamed")]
            public class Original : IGetEndpoint { public static string Path => "/renamed"; public Task<IResult> HandleAsync(HttpContext c) => Task.FromResult(Results.Ok()); }
            """);

        var mapping = MappingText(source);
        Assert.Contains("global::Microsoft.AspNetCore.Builder.RoutingEndpointConventionBuilderExtensions.WithName(b, \"Plain\");", mapping);
        Assert.Contains("global::Microsoft.AspNetCore.Builder.RoutingEndpointConventionBuilderExtensions.WithName(b, \"Renamed\");", mapping);
        Assert.DoesNotContain("WithName(b, \"Original\")", mapping);

        var routes = RoutesClass(EndpointGeneratorHarness.RunAndLoad("Fixtures.Links.Named", source));
        Assert.NotNull(routes.GetMethod("Renamed"));
        Assert.Null(routes.GetMethod("Original"));
    }

    /// <summary>
    /// An endpoint answering several methods gets the operationId hook; a single-verb one does not;
    /// and the hook compiles against <c>Microsoft.AspNetCore.OpenApi</c> and is erased without it.
    /// </summary>
    /// <remarks>
    /// Without the hook, one endpoint name is one <c>operationId</c> on several operations — an
    /// invalid OpenAPI document. The runtime effect is asserted on the sample app's document
    /// (<c>TestAppOpenApiDocumentTests</c>); this pins which endpoints get it and that both
    /// compilations stay clean.
    /// </remarks>
    [Fact]
    public void MultiMethodEndpoint_GetsTheOperationIdHook_OnlyIt()
    {
        var mapping = MappingText(FixtureSources.Corpus);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(mapping, @"OnMultiMethodEndpointNamed\d+\(b\);"));

        var withOpenApi = EndpointGeneratorHarness.CreateCompilation("Fixtures", [FixtureSources.Corpus], includeOpenApi: true);
        var errors = Errors(EndpointGeneratorHarness.RunAndCompile(withOpenApi));
        Assert.True(errors.Length == 0, string.Join(" | ", errors.Select(e => $"{e.Id}: {e.GetMessage()}")));

        AssertCompilesClean(FixtureSources.Corpus);
    }

    /// <summary>
    /// The name reaches the endpoint metadata the framework reads — route name and endpoint name
    /// both — for every endpoint of the corpus.
    /// </summary>
    [Fact]
    public void MappedEndpoints_CarryTheirNamesInMetadata()
    {
        const string assemblyName = "Fixtures.Links.Metadata";
        var generated = EndpointGeneratorHarness.RunAndLoad(assemblyName, FixtureSources.Corpus);

        var names = GeneratedEndpointHost.MapAndCollectRoutes(generated, assemblyName)
            .Select(route => (
                Endpoint: route.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IEndpointNameMetadata>()?.EndpointName,
                Route: route.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IRouteNameMetadata>()?.RouteName))
            .ToArray();

        Assert.All(names, pair => Assert.Equal(pair.Endpoint, pair.Route));
        Assert.Equal(
            ["CreateUser", "DeleteUser", "GetUser", "HealthCheck", "ListProducts", "ListUsers", "PatchUser", "PreflightEndpoint", "UpdateUser"],
            names.Select(pair => pair.Endpoint!).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }
}
