using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// What M5 emits for OpenAPI (PRD R4.1–R4.4, R4.7): the <c>string?</c> shadow parameter types and
/// the request-side metadata in <c>EndpointMapping.g.cs</c>, and the schema transformers in
/// <c>EndpointOpenApi.g.cs</c> — which exists only when the consumer references
/// <c>Microsoft.AspNetCore.OpenApi</c>.
/// </summary>
/// <remarks>
/// Every text assertion is paired with a compile, in both consumer shapes: the transformer names
/// types from a package the library itself does not reference, so "right in text, missing at compile
/// time" is exactly the failure to guard against — and the mapping file must compile identically with
/// that package absent.
/// </remarks>
public class OpenApiEmissionTests
{
    private const string MappingFile = "EndpointMapping.g.cs";
    private const string OpenApiFile = "EndpointOpenApi.g.cs";

    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;

        public record UserBody(string Name);
        public record UserResponse(int Id, string Name);
        public enum Kind { Book, Film }
        """;

    /// <summary>One endpoint per interesting shadow shape.</summary>
    private const string Endpoints = """
        public partial class Search : IGetEndpoint<UserResponse>
        {
            public static string Path => "/search/{id}/{kind}/{userId}/{Slug}";

            [RouteParam] public int Id { get; set; }
            [RouteParam] public Kind Kind { get; set; }
            [RouteParam("USERID")] public Guid UserId { get; set; }
            [RouteParam] public string Slug { get; set; } = "";
            [QueryParam] public int Page { get; set; } = 1;
            [QueryParam("q")] public string? Term { get; set; }
            [QueryParam] public bool? Active { get; set; }

            public override Task<IResult> HandleAsync(CancellationToken ct) => Task.FromResult(Results.Ok());
        }

        public partial class Rename : IPutEndpoint<UserBody>
        {
            public static string Path => "/users/{id}";

            [RouteParam] public int Id { get; set; }

            public override Task<IResult> HandleAsync(UserBody request, CancellationToken ct) => Task.FromResult(Results.Ok());
        }

        public partial class Create : IPostEndpoint<UserBody, UserResponse>
        {
            public static string Path => "/users";

            public override Task<IResult> HandleAsync(UserBody request, CancellationToken ct) => Task.FromResult(Results.Ok());
        }

        public class Files : IGetEndpoint
        {
            public static string Path => "/files/{**rest}";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }

        public class Health : IGetEndpoint
        {
            public static string Path => "/health";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }
        """;

    private static readonly string Source = Preamble + "\n\n" + Endpoints;

    private static readonly Regex UsingDirective = new(@"^\s*using\s+[\w.]+\s*;", RegexOptions.Multiline);

    private static Dictionary<string, string> Files(bool includeOpenApi, params string[] sources)
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", sources, includeOpenApi: includeOpenApi);
        var result = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver
            .Create(new EndpointGenerator())
            .RunGenerators(compilation)
            .GetRunResult();

        return result.GeneratedTrees.ToDictionary(tree => Path.GetFileName(tree.FilePath), tree => tree.ToString());
    }

    private static Diagnostic[] Errors(bool includeOpenApi, bool includeImplicitUsings, params string[] sources)
        => [.. EndpointGeneratorHarness
            .RunAndCompile(EndpointGeneratorHarness.CreateCompilation("Fixtures", sources, includeImplicitUsings, includeOpenApi))
            .Where(d => d.Severity == DiagnosticSeverity.Error)];

    // ---------- the shadow parameter ----------

    /// <summary>
    /// Every shadow member is a <c>string?</c> with the right source attribute, whatever the bound
    /// property's type.
    /// </summary>
    /// <remarks>
    /// A typed member (<c>int?</c>) documents the same schema but makes the framework parse first, so
    /// <c>/search/abc</c> becomes a 400 with an empty body and the library's message never runs —
    /// spike S1's whole finding. Non-nullable <c>string</c> would 400 on a <i>missing</i> value.
    /// </remarks>
    [Fact]
    public void ShadowMembers_AreNullableStrings_WithFromRouteOrFromQuery()
    {
        var mapping = Files(false, Source)[MappingFile];

        Assert.Contains("[global::Microsoft.AspNetCore.Mvc.FromRouteAttribute] public string? @Id { get; set; }", mapping);
        Assert.Contains("[global::Microsoft.AspNetCore.Mvc.FromRouteAttribute] public string? @Kind { get; set; }", mapping);
        Assert.Contains("[global::Microsoft.AspNetCore.Mvc.FromRouteAttribute] public string? @Slug { get; set; }", mapping);
        Assert.Contains("[global::Microsoft.AspNetCore.Mvc.FromQueryAttribute] public string? @Page { get; set; }", mapping);
        Assert.Contains("[global::Microsoft.AspNetCore.Mvc.FromQueryAttribute] public string? @Active { get; set; }", mapping);

        // No member of any shadow type is anything but string?.
        var members = Regex.Matches(mapping, @"\[global::Microsoft\.AspNetCore\.Mvc\.From(Route|Query)Attribute[^\]]*\] public (\S+) @");
        Assert.NotEmpty(members);
        Assert.All(members, match => Assert.Equal("string?", match.Groups[2].Value));
    }

    /// <summary>
    /// <c>Name</c> is left unset unless the consumer supplied a name — and a supplied name is
    /// corrected to the template's spelling.
    /// </summary>
    /// <remarks>
    /// Unset because ApiExplorer then documents the template's spelling (measured: a member <c>Id</c>
    /// against <c>{id}</c> is documented as <c>id</c>). Set explicitly to <c>"Id"</c> it would be
    /// documented as <c>Id</c>, which Swagger UI cannot match to the <c>{id}</c> in the path — so an
    /// explicit <c>[RouteParam("USERID")]</c> against <c>{userId}</c> becomes <c>Name = "userId"</c>.
    /// A query key has no template, so its explicit name is kept as written.
    /// </remarks>
    [Fact]
    public void ShadowName_IsUnsetUnlessSupplied_AndFollowsTheTemplateSpelling()
    {
        var mapping = Files(false, Source)[MappingFile];

        Assert.Contains("""[global::Microsoft.AspNetCore.Mvc.FromRouteAttribute(Name = "userId")] public string? @UserId { get; set; }""", mapping);
        Assert.Contains("""[global::Microsoft.AspNetCore.Mvc.FromQueryAttribute(Name = "q")] public string? @Term { get; set; }""", mapping);
        Assert.Equal(2, Regex.Matches(mapping, @"Name = ").Count);   // and no other member names itself
    }

    /// <summary>
    /// A route token no bound property covers still gets a shadow member, so a raw endpoint with a
    /// templated route is documented with its path parameter.
    /// </summary>
    /// <remarks>
    /// Measured: <c>/raw/{**path}</c> mapped with only an <c>HttpContext</c> parameter is documented
    /// with no path parameter at all — an invalid document — even though nothing binds the value.
    /// </remarks>
    [Fact]
    public void UnboundRouteToken_GetsAShadowMember()
    {
        var mapping = Files(false, Source)[MappingFile];

        Assert.Contains("Map<global::Fixtures.Files, Files_Parameters", mapping);
        Assert.Contains("[global::Microsoft.AspNetCore.Mvc.FromRouteAttribute] public string? @rest { get; set; }", mapping);

        // An endpoint with neither keeps the plain helper.
        Assert.Matches(@"Map<global::Fixtures\.Health>\(app, _f\d+\);", mapping);
    }

    // ---------- request-side metadata ----------

    /// <summary>
    /// Body endpoints declare their body, 400 and 415; endpoints with bound properties declare 400;
    /// an endpoint with neither declares nothing (R4.2, R4.3).
    /// </summary>
    /// <remarks>
    /// Through <c>EndpointDocumentation</c>, the runtime helper the manual <c>MapEndpoint&lt;T&gt;()</c>
    /// calls too — and never as <c>.Accepts&lt;T&gt;("application/json")</c>, whose content types
    /// routing enforces with an empty-body 415 before the library's formatters run.
    /// </remarks>
    [Fact]
    public void RequestMetadata_IsDeclaredPerLevel()
    {
        var mapping = Files(false, Source)[MappingFile];
        const string Documentation = "global::MintPlayer.AspNetCore.Endpoints.EndpointDocumentation";

        Assert.Contains($"{Documentation}.DeclareRequestBody(b, typeof(global::Fixtures.UserBody));", mapping);
        Assert.Equal(2, Regex.Matches(mapping, Regex.Escape($"{Documentation}.DeclareRequestBody(")).Count);   // Rename, Create
        Assert.Single(Regex.Matches(mapping, Regex.Escape($"{Documentation}.DeclareBindingFailure("))); // Search
        Assert.DoesNotContain("Accepts", mapping);
        Assert.DoesNotContain("WithName", mapping);   // M6, with its uniqueness diagnostic
    }

    // ---------- the OpenAPI file, and its absence ----------

    /// <summary>
    /// Without <c>Microsoft.AspNetCore.OpenApi</c> only the mapping file is emitted, it names no
    /// OpenAPI type, and it compiles — with and without implicit usings.
    /// </summary>
    /// <remarks>
    /// This is the consumer the library must not tax (R4.7): the mapping file calls the
    /// <c>OnEndpointMapped{n}</c> hooks it declares, and with no implementation the compiler removes
    /// them. A single OpenAPI type named in the mapping file would be a compile error here.
    /// </remarks>
    [Fact]
    public void WithoutTheOpenApiPackage_OnlyTheMappingFileIsEmitted_AndItCompiles()
    {
        var files = Files(false, Source);

        Assert.Equal([MappingFile], files.Keys.ToArray());
        Assert.DoesNotContain("Microsoft.AspNetCore.OpenApi", files[MappingFile]);
        Assert.DoesNotContain("Microsoft.OpenApi", files[MappingFile]);
        Assert.Contains("static partial void OnEndpointMapped", files[MappingFile]);   // non-vacuous: the hooks are there

        Assert.Empty(Errors(includeOpenApi: false, includeImplicitUsings: true, Source));
        Assert.Empty(Errors(includeOpenApi: false, includeImplicitUsings: false, Source));
    }

    /// <summary>
    /// The unimplemented hooks really are erased: the compiled assembly has no <c>OnEndpointMapped</c>
    /// method at all without the package, and has them with it.
    /// </summary>
    /// <remarks>
    /// The evidence that the seam costs a non-OpenAPI consumer nothing — not a no-op method, not a call.
    /// </remarks>
    [Fact]
    public void Hooks_AreErasedWithoutTheOpenApiFile_AndPresentWithIt()
    {
        static MethodInfo[] Hooks(Assembly assembly) =>
            [.. assembly.GetTypes()
                .Where(type => type.Namespace == "MintPlayer.AspNetCore.Endpoints.Generated")
                .SelectMany(type => type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
                .Where(method => method.Name.StartsWith("OnEndpointMapped", StringComparison.Ordinal))];

        var without = EndpointGeneratorHarness.RunAndLoad(
            EndpointGeneratorHarness.CreateCompilation("Fixtures.HooksErased", [Source]));
        var with = EndpointGeneratorHarness.RunAndLoad(
            EndpointGeneratorHarness.CreateCompilation("Fixtures.HooksImplemented", [Source], includeOpenApi: true));

        Assert.Empty(Hooks(without));
        Assert.NotEmpty(Hooks(with));
    }

    /// <summary>
    /// With the package, both files are emitted, neither carries a using directive, and together they
    /// compile — with and without implicit usings.
    /// </summary>
    [Fact]
    public void WithTheOpenApiPackage_BothFilesAreEmitted_WithoutUsings_AndCompile()
    {
        var files = Files(true, Source);

        Assert.Equal([MappingFile, OpenApiFile], files.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.All(files.Values, text => Assert.DoesNotMatch(UsingDirective, text));
        Assert.Contains("global::Microsoft.AspNetCore.Builder.OpenApiEndpointConventionBuilderExtensions.AddOpenApiOperationTransformer(builder,", files[OpenApiFile]);

        Assert.Empty(Errors(includeOpenApi: true, includeImplicitUsings: true, Source));
        Assert.Empty(Errors(includeOpenApi: true, includeImplicitUsings: false, Source));
        Assert.Empty(Errors(includeOpenApi: true, includeImplicitUsings: true, FixtureSources.Corpus));
    }

    /// <summary>
    /// Each typed member is restored with its CLR type's shape; strings and unbound tokens are left
    /// alone; enums go through the generic enum schema.
    /// </summary>
    /// <remarks>
    /// The names are the binder's keys (<c>USERID</c>, <c>q</c>), matched ignoring case — the document
    /// spells route parameters the template's way, which need not be the key's.
    /// </remarks>
    [Fact]
    public void OpenApiFile_RestoresEachTypedMembersSchema()
    {
        var openApi = Files(true, Source)[OpenApiFile];

        Assert.Contains("""("Id", true, context => ParameterSchema("int32"))""", openApi);
        Assert.Contains("""("Kind", true, context => EnumParameterSchema<global::Fixtures.Kind>(context))""", openApi);
        Assert.Contains("""("USERID", true, context => ParameterSchema("uuid"))""", openApi);
        Assert.Contains("""("Page", false, context => ParameterSchema("int32"))""", openApi);
        Assert.Contains("""("Active", false, context => ParameterSchema("boolean"))""", openApi);

        Assert.DoesNotContain("\"Slug\"", openApi);
        Assert.DoesNotContain("\"q\"", openApi);
        Assert.DoesNotContain("\"rest\"", openApi);

        // The schema code exists once, not per endpoint.
        Assert.Single(Regex.Matches(openApi, Regex.Escape("private static global::Microsoft.OpenApi.IOpenApiSchema ParameterSchema(")));
    }

    /// <summary>
    /// With the package but nothing typed to restore, no OpenAPI file is emitted — not an empty one.
    /// </summary>
    [Fact]
    public void WithTheOpenApiPackage_ButNothingToRestore_NoOpenApiFileIsEmitted()
    {
        const string onlyStrings = Preamble + """


            public partial class BySlug : IGetEndpoint
            {
                public static string Path => "/by/{slug}";
                [RouteParam] public string Slug { get; set; } = "";
                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """;

        var files = Files(true, onlyStrings);

        Assert.Equal([MappingFile], files.Keys.ToArray());
        Assert.DoesNotContain("OnEndpointMapped", files[MappingFile]);
        Assert.Empty(Errors(includeOpenApi: true, includeImplicitUsings: true, onlyStrings));
    }
}
