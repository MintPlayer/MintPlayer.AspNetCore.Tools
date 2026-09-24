using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// The OpenAPI document the sample app serves at <c>/openapi/v1.json</c> (M5, PRD R4.1–R4.3, R4.7).
/// </summary>
/// <remarks>
/// <para>
/// Before M5 the generated request delegate took only <c>HttpContext</c>, so ApiExplorer — which
/// builds parameters from delegate parameters alone — documented no path parameter on any templated
/// route (an invalid document), no request body and no 400/415. These tests read the served
/// document rather than endpoint metadata, because the document is the contract a client generator
/// consumes, and several of the steps between metadata and document (the shadow parameter's
/// flattening, the schema transformer in <c>EndpointOpenApi.g.cs</c>, ApiExplorer's default-200
/// rule) are only visible there.
/// </para>
/// <para>
/// The same class also asserts the library's own 400 message still reaches the client. That is the
/// other half of spike S1: a typed shadow parameter would make the document right and the message
/// disappear (the framework would 400 first, with an empty body). Keeping both assertions side by
/// side is what stops a "simplification" to a typed shadow from passing.
/// </para>
/// </remarks>
public class TestAppOpenApiDocumentTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Regex Token = new(@"\{([^}]+)\}", RegexOptions.Compiled);
    private static readonly string[] Verbs = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    private readonly WebApplicationFactory<Program> factory;

    public TestAppOpenApiDocumentTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    private async Task<JsonElement> DocumentAsync()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json");
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static IEnumerable<(string Path, string Verb, JsonElement Operation)> Operations(JsonElement document)
    {
        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (Verbs.Contains(operation.Name))
                    yield return (path.Name, operation.Name, operation.Value);
            }
        }
    }

    private static JsonElement Operation(JsonElement document, string path, string verb)
        => document.GetProperty("paths").GetProperty(path).GetProperty(verb);

    private static JsonElement[] Parameters(JsonElement operation)
        => operation.TryGetProperty("parameters", out var parameters) ? [.. parameters.EnumerateArray()] : [];

    private static JsonElement Parameter(JsonElement operation, string name, string location)
        => Assert.Single(Parameters(operation), p => p.GetProperty("name").GetString() == name && p.GetProperty("in").GetString() == location);

    private static string[] ResponseCodes(JsonElement operation)
        => [.. operation.GetProperty("responses").EnumerateObject().Select(response => response.Name).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The path-templating rule of the OpenAPI specification, checked programmatically for every
    /// operation: each <c>{token}</c> in the path key has exactly one <c>in: path</c> parameter of
    /// that exact name, marked <c>required: true</c> — and no path parameter names a token that is
    /// not there.
    /// </summary>
    /// <remarks>
    /// This is the defect M5 exists to fix (P4), and it covers the case the bound properties alone
    /// would miss: the raw <c>/api/{**path}</c> catch-all binds nothing, yet its token needs a
    /// parameter just as much — the shadow includes every unbound route token for that reason. Names
    /// are compared case-sensitively because a validator and Swagger UI do: <c>Id</c> against
    /// <c>{id}</c> does not match.
    /// </remarks>
    [Fact]
    public async Task EveryPathToken_HasExactlyOneRequiredPathParameter_OfTheSameName()
    {
        var document = await DocumentAsync();
        var operations = Operations(document).ToArray();
        Assert.NotEmpty(operations);

        foreach (var (path, verb, operation) in operations)
        {
            var tokens = Token.Matches(path).Select(match => match.Groups[1].Value).ToArray();
            var pathParameters = Parameters(operation).Where(p => p.GetProperty("in").GetString() == "path").ToArray();

            foreach (var token in tokens)
            {
                var parameter = Assert.Single(pathParameters, p => p.GetProperty("name").GetString() == token);
                Assert.True(parameter.GetProperty("required").GetBoolean(), $"{verb} {path}: '{token}' is not required");
            }

            Assert.All(pathParameters, p => Assert.Contains(p.GetProperty("name").GetString(), tokens));
        }

        // Non-vacuous: the templated routes of the sample are actually in the document.
        Assert.Contains(operations, o => o.Path == "/api/users/{id}");
        Assert.Contains(operations, o => o.Path == "/api/{path}");
    }

    /// <summary>
    /// <c>GET /api/users/{id}</c>'s <c>id</c> carries the exact schema ASP.NET Core gives a typed
    /// <c>int</c> route parameter, although the shadow member behind it is a <c>string?</c>.
    /// </summary>
    /// <remarks>
    /// The shape is the pattern, <c>type: [integer, string]</c> and <c>format: int32</c> triple — not
    /// plain <c>type: integer</c>, which is what a first transformer wrote in S1 until a byte-diff
    /// caught it. A <c>type: string</c> here means the transformer in <c>EndpointOpenApi.g.cs</c> did
    /// not run: the package reference, the hook, or the name match is broken.
    /// </remarks>
    [Fact]
    public async Task GetUserById_IdHasTheInt32Schema()
    {
        var id = Parameter(Operation(await DocumentAsync(), "/api/users/{id}", "get"), "id", "path");
        var schema = id.GetProperty("schema");

        Assert.Equal(@"^-?(?:0|[1-9]\d*)$", schema.GetProperty("pattern").GetString());
        Assert.Equal(["integer", "string"], schema.GetProperty("type").EnumerateArray().Select(t => t.GetString()!).ToArray());
        Assert.Equal("int32", schema.GetProperty("format").GetString());
    }

    /// <summary>
    /// The raw <c>ListUsers</c>' <c>[QueryParam] int Page = 1</c> is documented as an optional
    /// query parameter with the int32 schema.
    /// </summary>
    /// <remarks>
    /// Documented as <c>Page</c>, the property name: a query key has no template to take its
    /// spelling from, and the binder matches it case-insensitively, so <c>?page=2</c> works as well.
    /// </remarks>
    [Fact]
    public async Task ListUsers_PageQueryParameterIsDocumented()
    {
        var page = Parameter(Operation(await DocumentAsync(), "/api/users", "get"), "Page", "query");

        Assert.False(page.TryGetProperty("required", out var required) && required.GetBoolean());
        Assert.Equal("int32", page.GetProperty("schema").GetProperty("format").GetString());
    }

    /// <summary>Every operation of a body verb documents a JSON <c>requestBody</c> (R4.2).</summary>
    [Fact]
    public async Task EveryBodyVerb_HasAJsonRequestBody()
    {
        var bodyOperations = Operations(await DocumentAsync())
            .Where(o => o.Verb is "post" or "put" or "patch")
            .ToArray();

        Assert.Equal(2, bodyOperations.Length);   // POST /api/users, PUT /api/users/{id}
        Assert.All(bodyOperations, o =>
        {
            var body = o.Operation.GetProperty("requestBody");
            Assert.True(body.GetProperty("required").GetBoolean());
            Assert.True(body.GetProperty("content").TryGetProperty("application/json", out _), $"{o.Verb} {o.Path}");
        });
    }

    /// <summary>
    /// Typed endpoints document the failures the README's binding table guarantees: 400 and 415 for
    /// a body, 400 for a bound property — and keep their success response (R4.3).
    /// </summary>
    /// <remarks>
    /// The success codes are asserted too, because declaring a problem response switches off
    /// ApiExplorer's assumed 200; without the library putting it back, <c>PUT</c> and the raw
    /// <c>GET /api/users</c> would document only their failures.
    /// </remarks>
    [Fact]
    public async Task Endpoints_DocumentTheirFailureAndSuccessResponses()
    {
        var document = await DocumentAsync();

        Assert.Equal(["201", "400", "415"], ResponseCodes(Operation(document, "/api/users", "post")));
        Assert.Equal(["200", "400", "415"], ResponseCodes(Operation(document, "/api/users/{id}", "put")));
        Assert.Equal(["200", "400"], ResponseCodes(Operation(document, "/api/users/{id}", "get")));
        Assert.Equal(["200", "400"], ResponseCodes(Operation(document, "/api/users", "get")));

        // Nothing to bind, nothing declared: the plain default.
        Assert.Equal(["200"], ResponseCodes(Operation(document, "/health", "get")));

        var problem = Operation(document, "/api/users/{id}", "get").GetProperty("responses").GetProperty("400").GetProperty("content");
        Assert.True(problem.TryGetProperty("application/problem+json", out _));
    }

    /// <summary>
    /// <c>DELETE /api/users/{id}</c> documents 204 — from the endpoint's own
    /// <c>[ProducesResponseType]</c>, since a raw endpoint's status is invisible to the generator —
    /// and does not also claim the default 200.
    /// </summary>
    [Fact]
    public async Task DeleteUser_Documents204_AndNot200()
        => Assert.Equal(["204", "400"], ResponseCodes(Operation(await DocumentAsync(), "/api/users/{id}", "delete")));

    /// <summary>
    /// The S1 property: with the shadow parameter in place, <c>GET /api/users/abc</c> is still the
    /// library's <c>problem+json</c> 400 naming the parameter, the value and the type.
    /// </summary>
    /// <remarks>
    /// A typed shadow (<c>int?</c>) would document the same schema but make the framework reject
    /// <c>abc</c> first, with a zero-length body and no content type. This test fails for exactly
    /// that change.
    /// </remarks>
    [Fact]
    public async Task GetUserWithNonNumericId_StillReturnsTheLibrarysProblemDetails()
    {
        var response = await factory.CreateClient().GetAsync("/api/users/abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("The route parameter 'Id' must be a valid Int32; 'abc' is not.", body.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>
    /// Declaring the request body did not hand content-type filtering to routing: an unsupported
    /// content type is still answered by the library, with its <c>problem+json</c> 415.
    /// </summary>
    /// <remarks>
    /// <c>.Accepts&lt;T&gt;("application/json")</c> — the obvious way to declare the body — adds
    /// metadata that routing's <c>AcceptsMatcherPolicy</c> enforces: measured, it returns an empty-body
    /// 415 for <c>text/plain</c> and <c>application/xml</c> before the endpoint runs, which would also
    /// break consumers whose MVC input formatters read XML. The library declares the body with no
    /// content types instead, and this is the guard.
    /// </remarks>
    [Fact]
    public async Task UnsupportedContentType_IsStillTheLibrarys415()
    {
        using var content = new StringContent("name=Carol", System.Text.Encoding.UTF8, "text/plain");

        var response = await factory.CreateClient().PostAsync("/api/users/", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
