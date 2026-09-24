using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// What the generator emits for <c>[RouteParam]</c>/<c>[QueryParam]</c> properties (M4, PRD R2.1–R2.14):
/// the <c>BindParameters</c> override on typed levels, the explicit <c>IParameterBinder</c> on raw
/// ones, the conversion helper chosen per property, the <c>ResponseEndpoint</c> base and
/// <c>ProducesResponse</c> for the response-only rung, and the per-request construction in
/// <c>Map&lt;TEndpoint&gt;</c>.
/// </summary>
/// <remarks>
/// Every substring assertion here is paired with a compile of the same fixture through
/// <see cref="EndpointGeneratorHarness.RunAndCompile(string, string[])"/>: a helper name that is
/// right in text but wrong in arity or constraint (<c>OptionalParsable&lt;T&gt;</c> is
/// <c>struct</c>-constrained, <c>OptionalParsableReference&lt;T&gt;</c> is <c>class</c>-constrained)
/// only shows up as a compile error.
/// </remarks>
public class RequestBindingEmissionTests
{
    private const string Binding = "global::MintPlayer.AspNetCore.Endpoints.ParameterBinding";
    private const string Route = "global::MintPlayer.AspNetCore.Endpoints.ParameterSource.Route";
    private const string Query = "global::MintPlayer.AspNetCore.Endpoints.ParameterSource.Query";

    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;

        public record UserBody(string Name);
        public record UserResponse(int Id, string Name);

        public enum Kind { Book, Film }

        /// A reference type implementing IParsable of itself — the case that needs the
        /// class-constrained OptionalParsableReference helper when it is nullable.
        public sealed class Slug : IParsable<Slug>
        {
            public Slug(string value) => Value = value;
            public string Value { get; }
            public static Slug Parse(string s, IFormatProvider? provider) => new(s);
            public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Slug result)
            {
                result = s is null ? null : new Slug(s);
                return result is not null;
            }
        }
        """;

    /// <summary>
    /// One property per conversion form, on one typed endpoint — every row of the R2.5
    /// classification table and all three assignment forms.
    /// </summary>
    private const string EveryFormEndpoint = """
        public partial class Search : IGetEndpoint<UserResponse>
        {
            public static string Path => "/search/{id}/{kind}/{slug}/{name}";

            [RouteParam] public int Id { get; set; }
            [RouteParam] public Kind Kind { get; set; }
            [RouteParam] public Slug Slug { get; set; } = null!;
            [RouteParam] public string Name { get; set; } = "";
            [RouteParam("userId")] public long? UserId { get; set; }

            [QueryParam] public int Page { get; set; } = 1;
            [QueryParam] public int? Size { get; set; }
            [QueryParam] public Kind? Filter { get; set; }
            [QueryParam] public Slug? Tag { get; set; }
            [QueryParam] public string? Term { get; set; }
            [QueryParam] public string Sort { get; set; } = "name";
            [QueryParam] public Kind Order { get; set; } = Kind.Book;
            [QueryParam] public DateOnly Since { get; set; }

            public override Task<IResult> HandleAsync(CancellationToken ct)
                => Task.FromResult(Results.Ok(new UserResponse(Id, Name)));
        }
        """;

    private static string Generated(params string[] sources)
        => string.Join(
            "\n",
            EndpointGeneratorHarness.Run("Fixtures", sources).GeneratedTrees.Select(tree => tree.ToString()));

    private static Diagnostic[] Errors(IEnumerable<Diagnostic> diagnostics)
        => [.. diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];

    private static string Fixture(params string[] endpoints)
        => Preamble + "\n\n" + string.Join("\n\n", endpoints);

    /// <summary>The fixture carrying every conversion form compiles, generated binder included.</summary>
    [Fact]
    public void EveryConversionForm_Compiles_WithZeroErrors()
    {
        var diagnostics = EndpointGeneratorHarness.RunAndCompile("Fixtures", Fixture(EveryFormEndpoint));

        Assert.Empty(Errors(diagnostics));
    }

    /// <summary>
    /// The helper and the assignment form chosen for each property.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>No initializer, not nullable: required — the plain helper, which throws "is required".</item>
    /// <item>Nullable: optional — absent binds <see langword="null"/> via the <c>Optional…</c> helper.</item>
    /// <item>An initializer: the initializer <i>is</i> the default, so the <c>Try…</c> helper assigns only
    /// when a value was supplied. Getting this wrong silently overwrites <c>Page = 1</c> with 0.</item>
    /// <item>A nullable reference type implementing <c>IParsable</c> needs
    /// <c>OptionalParsableReference</c>; <c>OptionalParsable</c> is <c>struct</c>-constrained.</item>
    /// <item>An enum goes to <c>Enum&lt;T&gt;</c> (which checks <c>Enum.IsDefined</c>), never to
    /// <c>Parsable</c>; <c>string</c> goes to <c>String</c>, never to <c>Parsable&lt;string&gt;</c>, though
    /// <c>string</c> implements <c>IParsable&lt;string&gt;</c> too.</item>
    /// <item>An explicit name on the attribute is the key read; the property name is what is assigned.</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData($"this.Id = {Binding}.Parsable<int>(context, {Route}, \"Id\");")]
    [InlineData($"this.Kind = {Binding}.Enum<global::Fixtures.Kind>(context, {Route}, \"Kind\");")]
    [InlineData($"out var __v2)) this.Slug = __v2;")]
    [InlineData($"if ({Binding}.TryParsable<global::Fixtures.Slug>(context, {Route}, \"Slug\", out var __v2))")]
    [InlineData($"if ({Binding}.TryString(context, {Route}, \"Name\", out var __v3)) this.Name = __v3;")]
    [InlineData($"this.UserId = {Binding}.OptionalParsable<long>(context, {Route}, \"userId\");")]
    [InlineData($"if ({Binding}.TryParsable<int>(context, {Query}, \"Page\", out var __v5)) this.Page = __v5;")]
    [InlineData($"this.Size = {Binding}.OptionalParsable<int>(context, {Query}, \"Size\");")]
    [InlineData($"this.Filter = {Binding}.OptionalEnum<global::Fixtures.Kind>(context, {Query}, \"Filter\");")]
    [InlineData($"this.Tag = {Binding}.OptionalParsableReference<global::Fixtures.Slug>(context, {Query}, \"Tag\");")]
    [InlineData($"this.Term = {Binding}.OptionalString(context, {Query}, \"Term\");")]
    [InlineData($"if ({Binding}.TryString(context, {Query}, \"Sort\", out var __v10)) this.Sort = __v10;")]
    [InlineData($"if ({Binding}.TryEnum<global::Fixtures.Kind>(context, {Query}, \"Order\", out var __v11)) this.Order = __v11;")]
    [InlineData($"this.Since = {Binding}.Parsable<global::System.DateOnly>(context, {Query}, \"Since\");")]
    public void EachProperty_GetsTheHelperAndFormForItsShape(string expectedLine)
    {
        Assert.Contains(expectedLine, Generated(Fixture(EveryFormEndpoint)));
    }

    /// <summary>
    /// <c>string</c> is classified before <c>IParsable</c>, and enums never reach the parsable
    /// branch — the R2.5 order, asserted from the negative side.
    /// </summary>
    [Fact]
    public void StringAndEnum_NeverFallIntoTheParsableHelpers()
    {
        var generated = Generated(Fixture(EveryFormEndpoint));

        Assert.DoesNotContain("Parsable<string>", generated);
        Assert.DoesNotContain("Parsable<global::Fixtures.Kind>", generated);
    }

    /// <summary>
    /// The response-only rung gets the <c>ResponseEndpoint</c> base, and the binder is a
    /// <c>BindParameters</c> override inside that same partial.
    /// </summary>
    /// <remarks>
    /// The override is what makes a typed endpoint bind at all — <c>ResponseEndpoint.HandleAsync(HttpContext)</c>
    /// calls it before the handler. Emitted without <c>override</c> it would be a hiding method the
    /// base never calls, and every property would silently stay at its default.
    /// </remarks>
    [Theory]
    [InlineData("IGetEndpoint<UserResponse>")]
    [InlineData("IDeleteEndpoint<UserResponse>")]
    public void ResponseOnlyEndpoint_GetsResponseEndpointBase_AndABindParametersOverride(string endpointInterface)
    {
        var source = Fixture($$"""
            public partial class ByIdEndpoint : {{endpointInterface}}
            {
                public static string Path => "/users/{id}";

                [RouteParam] public int Id { get; set; }

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
            }
            """);

        var generated = Generated(source);

        Assert.Contains("partial class ByIdEndpoint : global::MintPlayer.AspNetCore.Endpoints.ResponseEndpoint", generated);
        Assert.Contains("protected override void BindParameters(global::Microsoft.AspNetCore.Http.HttpContext context)", generated);
        Assert.DoesNotContain("IParameterBinder.BindParameters", generated);
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));
    }

    /// <summary>
    /// A response-only endpoint with no bound properties still gets its base class, and no
    /// empty override.
    /// </summary>
    [Fact]
    public void ResponseOnlyEndpoint_WithoutBoundProperties_GetsJustTheBase()
    {
        var source = Fixture("""
            public partial class Me : IGetEndpoint<UserResponse>
            {
                public static string Path => "/me";

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(1, "Alice")));
            }
            """);

        var generated = Generated(source);

        Assert.Contains("partial class Me : global::MintPlayer.AspNetCore.Endpoints.ResponseEndpoint { }", generated);
        Assert.DoesNotContain("void BindParameters", generated);
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));
    }

    /// <summary>
    /// A body verb with a route parameter keeps its per-verb body base and gains the override — the
    /// route value lives on the endpoint, the body type is untouched (R2.1, R2.9).
    /// </summary>
    [Fact]
    public void BodyEndpoint_WithRouteParameter_KeepsItsBodyBase_AndGetsTheOverride()
    {
        var source = Fixture("""
            public partial class UpdateUser : IPutEndpoint<UserBody, UserResponse>
            {
                public static string Path => "/users/{id}";

                [RouteParam] public int Id { get; set; }

                public override Task<IResult> HandleAsync(UserBody request, CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(Id, request.Name)));
            }
            """);

        var generated = Generated(source);

        Assert.Contains("partial class UpdateUser : global::MintPlayer.AspNetCore.Endpoints.PutEndpoint<global::Fixtures.UserBody>", generated);
        Assert.Contains("protected override void BindParameters(", generated);
        Assert.Contains("Produces<global::Fixtures.UpdateUser, global::Fixtures.UserBody, global::Fixtures.UserResponse>(b);", generated);
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));
    }

    /// <summary>
    /// A raw endpoint with bound properties gets a generated partial that implements
    /// <c>IParameterBinder</c> <b>explicitly</b>, catching the binding failure and turning it into a
    /// result (R2.13).
    /// </summary>
    /// <remarks>
    /// Explicit so the binder stays off the consumer's public surface. A raw endpoint has no base
    /// class, so without this partial the mapper would have nothing to call and the properties
    /// would silently stay at their defaults.
    /// </remarks>
    [Fact]
    public void RawEndpoint_WithBoundProperties_GetsAnExplicitParameterBinder()
    {
        var source = Fixture("""
            public partial class DeleteUser : IDeleteEndpoint
            {
                public static string Path => "/users/{id}";

                [RouteParam] public int Id { get; set; }

                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.NoContent());
            }
            """);

        var generated = Generated(source);

        Assert.Contains("partial class DeleteUser : global::MintPlayer.AspNetCore.Endpoints.IParameterBinder", generated);
        Assert.Contains(
            "global::Microsoft.AspNetCore.Http.IResult? global::MintPlayer.AspNetCore.Endpoints.IParameterBinder.BindParameters(global::Microsoft.AspNetCore.Http.HttpContext context)",
            generated);
        Assert.Contains("catch (global::MintPlayer.AspNetCore.Endpoints.EndpointBindingException failure)", generated);
        Assert.Contains($"return {Binding}.Failure(failure);", generated);
        Assert.DoesNotContain("protected override void BindParameters", generated);
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));
    }

    /// <summary>
    /// A raw endpoint without bound properties gets no generated partial — and therefore does not
    /// need to be <c>partial</c> at all, and gets no diagnostic for not being one.
    /// </summary>
    /// <remarks>
    /// The M4 gate: widening the partial gate for raw endpoints with bound properties must not
    /// start demanding <c>partial</c> from every raw endpoint, which would break every existing
    /// consumer's health check.
    /// </remarks>
    [Theory]
    [InlineData("public class Health : IGetEndpoint")]
    [InlineData("public partial class Health : IGetEndpoint")]
    public void RawEndpoint_WithoutBoundProperties_GetsNoPartial_AndNeedsNone(string declaration)
    {
        var source = Fixture($$"""
            {{declaration}}
            {
                public static string Path => "/health";

                public int NotBound { get; set; }

                public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
            }
            """);

        var result = EndpointGeneratorHarness.Run("Fixtures", source);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain("partial class Health", generated);
        Assert.Contains("Map<global::Fixtures.Health>", generated);
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));
    }

    /// <summary>
    /// A bound property with a <c>private set</c> declared on the endpoint itself is assignable: the
    /// generated partial is the same class. Only a private setter on a <i>base</i> class is MPEP020.
    /// </summary>
    [Fact]
    public void PrivateSetterOnTheEndpointItself_IsBound()
    {
        var source = Fixture("""
            public partial class GetUser : IGetEndpoint<UserResponse>
            {
                public static string Path => "/users/{id}";

                [RouteParam] public int Id { get; private set; }

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
            }
            """);

        var result = EndpointGeneratorHarness.Run("Fixtures", source);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("this.Id = ", string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString())));
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));
    }

    /// <summary>
    /// A bound endpoint nested inside partial containers gets its binder emitted inside every
    /// reopened container, and compiles.
    /// </summary>
    [Fact]
    public void NestedEndpoint_WithBoundProperties_Compiles()
    {
        var source = Fixture("""
            public static partial class Outer
            {
                public partial class Inner
                {
                    public partial class NestedGetUser : IGetEndpoint<UserResponse>
                    {
                        public static string Path => "/nested/{id}";

                        [RouteParam] public int Id { get; set; }

                        public override Task<IResult> HandleAsync(CancellationToken ct)
                            => Task.FromResult(Results.Ok(new UserResponse(Id, "Nested")));
                    }
                }
            }
            """);

        Assert.Contains("protected override void BindParameters(", Generated(source));
        Assert.Empty(Errors(EndpointGeneratorHarness.RunAndCompile("Fixtures", source)));
    }

    /// <summary>
    /// The response-only rung gets <c>ProducesResponse&lt;TEndpoint, TResponse&gt;</c> — it has no
    /// request type, so the three-argument <c>Produces</c> cannot name it.
    /// </summary>
    [Fact]
    public void ResponseOnlyEndpoint_EmitsProducesResponse()
    {
        var generated = Generated(FixtureSources.Corpus);

        Assert.Contains("ProducesResponse<global::Fixtures.GetUser, global::Fixtures.UserResponse>(b);", generated);
        Assert.DoesNotContain("Produces<global::Fixtures.GetUser,", generated);
        Assert.DoesNotContain("ProducesResponse<global::Fixtures.DeleteUser", generated);
        Assert.Contains(
            "private static void ProducesResponse<TEndpoint, TResponse>(global::Microsoft.AspNetCore.Builder.RouteHandlerBuilder builder) where TEndpoint : global::MintPlayer.AspNetCore.Endpoints.IResponseEndpoint<TResponse>",
            generated);
    }

    /// <summary>
    /// Executed, not matched: the response-only endpoint's route carries <c>Produces</c> metadata
    /// for its response type at 200, and a <c>SuccessStatusCode</c> override on
    /// <c>IResponseEndpoint&lt;T&gt;</c> reaches it.
    /// </summary>
    /// <remarks>
    /// <c>SuccessStatusCode</c> is a static virtual on a different interface than the typed rung's,
    /// so an override written against the wrong one would compile and be ignored.
    /// </remarks>
    [Fact]
    public void ResponseOnlyEndpoint_ProducesMetadata_ReachesTheRoute()
    {
        const string assemblyName = "Fixtures.ProducesResponse";
        var source = Fixture("""
            public partial class GetUser : IGetEndpoint<UserResponse>
            {
                public static string Path => "/users/{id}";

                [RouteParam] public int Id { get; set; }

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
            }

            public partial class Accepted : IDeleteEndpoint<UserResponse>
            {
                public static string Path => "/users/{id}/accepted";

                static int IResponseEndpoint<UserResponse>.SuccessStatusCode => 202;

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Accepted());
            }
            """);

        var generated = EndpointGeneratorHarness.RunAndLoad(assemblyName, source);
        var routes = GeneratedEndpointHost.MapAndCollectRoutes(generated, assemblyName);

        var get = Assert.Single(routes, route => route.RoutePattern.RawText == "/users/{id}");
        // Since M5 the bound Id also declares its 400, and the restored default 200 must not be added
        // next to a declared success: exactly one success, the typed one.
        var getResponses = get.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        var produces = Assert.Single(getResponses, m => m.StatusCode is >= 200 and < 300);
        Assert.Equal(200, produces.StatusCode);
        Assert.Equal("UserResponse", produces.Type?.Name);
        Assert.Contains(getResponses, m => m.StatusCode == 400);

        // Binds nothing, so declares nothing beyond its success.
        var accepted = Assert.Single(routes, route => route.RoutePattern.RawText == "/users/{id}/accepted");
        Assert.Equal(202, Assert.Single(accepted.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()).StatusCode);
    }

    /// <summary>
    /// R2.12, the cross-request-leak guard: the endpoint instance is constructed <b>inside</b> the
    /// request delegate of the emitted <c>Map&lt;TEndpoint&gt;</c> helper, never in its closure.
    /// </summary>
    /// <remarks>
    /// <c>MapMethods</c> captures its lambda once for the life of the application. Since M4 an
    /// endpoint carries mutable per-request state in its bound properties, so hoisting
    /// <c>factory(...)</c> one line up — out of the lambda — would make every endpoint a
    /// process-wide singleton and serve one caller's route values to the next. That is invisible to
    /// every single-threaded test, so it is pinned here by position: <c>factory(</c> must appear
    /// exactly once in the helper, after <c>async (</c> and before the lambda's closing <c>);</c>.
    /// </remarks>
    /// <remarks>
    /// Since M5 there are two helpers — <c>Map&lt;TEndpoint, TShadow&gt;</c> adds the
    /// <c>[AsParameters]</c> shadow to the delegate's parameter list — and both are pinned.
    /// </remarks>
    [Theory]
    [InlineData("Map<TEndpoint>(")]
    [InlineData("Map<TEndpoint, TShadow>(")]
    public void MapHelper_ConstructsTheEndpointInsideTheRequestDelegate(string helperName)
    {
        var generated = Generated(FixtureSources.Corpus);

        var helperStart = generated.IndexOf("private static global::Microsoft.AspNetCore.Builder.RouteHandlerBuilder " + helperName, StringComparison.Ordinal);
        Assert.True(helperStart >= 0, $"{helperName} helper not found");

        var helperEnd = generated.IndexOf("return builder;", helperStart, StringComparison.Ordinal);
        Assert.True(helperEnd > helperStart, "Map<TEndpoint> helper has no 'return builder;'");
        var helper = generated.Substring(helperStart, helperEnd - helperStart);

        var lambdaStart = helper.IndexOf("async (", StringComparison.Ordinal);
        Assert.True(lambdaStart >= 0, "the request delegate was not found in Map<TEndpoint>");

        // The lambda is the last argument of MapMethods, so it closes at the first line that is
        // just ");" after it opens.
        var lines = helper.Substring(lambdaStart).Split('\n');
        var lambdaLength = 0;
        foreach (var line in lines)
        {
            if (line.Trim() == ");") break;
            lambdaLength += line.Length + 1;
        }
        Assert.True(lambdaLength < helper.Length - lambdaStart, "the request delegate's closing ');' was not found");

        var lambda = helper.Substring(lambdaStart, lambdaLength);
        var beforeLambda = helper.Substring(0, lambdaStart);
        var afterLambda = helper.Substring(lambdaStart + lambdaLength);

        Assert.Contains("factory(ctx.RequestServices", lambda);
        Assert.DoesNotContain("factory(", beforeLambda);
        Assert.DoesNotContain("factory(", afterLambda);

        // And the raw-endpoint binder runs after construction, before the handler — so a bad
        // route value is rejected without the handler ever running.
        var construct = lambda.IndexOf("factory(", StringComparison.Ordinal);
        var bind = lambda.IndexOf("binder.BindParameters(ctx)", StringComparison.Ordinal);
        var handle = lambda.IndexOf("ep.HandleAsync(ctx)", StringComparison.Ordinal);
        Assert.True(construct < bind && bind < handle, "expected factory(...) < BindParameters < HandleAsync inside the delegate");
    }
}
