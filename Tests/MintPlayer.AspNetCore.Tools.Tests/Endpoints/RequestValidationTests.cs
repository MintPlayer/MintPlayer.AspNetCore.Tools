using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Validation;
using MintPlayer.AspNetCore.Endpoints;
using MintPlayer.AspNetCore.Endpoints.TestApp.Models;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

// The fixtures are top-level and public on purpose: the validation generator only emits a resolver
// entry for a [ValidatableType] it can name from its own generated file.

/// <summary>Plain DataAnnotations on properties.</summary>
[ValidatableType]
public sealed class SignupRequest
{
    [Required] public string? Name { get; set; }
    [Range(1, 120)] public int Age { get; set; }
    [EmailAddress] public string? Email { get; set; }
}

/// <summary>DataAnnotations on positional record parameters, with no <c>property:</c> target.</summary>
[ValidatableType]
public sealed record PositionalSignupRequest([Required] string? Name, [Range(1, 10)] int Level);

/// <summary>Cross-member rules, one of which names no member.</summary>
[ValidatableType]
public sealed class OrderRequest : IValidatableObject
{
    [Range(1, 1000)] public int Quantity { get; set; }
    public int MaxQuantity { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Quantity <= MaxQuantity)
            yield break;

        yield return new ValidationResult("Quantity exceeds MaxQuantity.", [nameof(Quantity), nameof(MaxQuantity)]);
        yield return new ValidationResult("The order cannot be fulfilled.");
    }
}

/// <summary>A nested member whose own type is deliberately <b>not</b> marked.</summary>
public sealed class ShippingAddress
{
    [Required] public string? Name { get; set; }
}

[ValidatableType]
public sealed class ShipmentRequest
{
    [Required] public string? Title { get; set; }
    public ShippingAddress? Inner { get; set; }
}

/// <summary>
/// M7 / PRD R5: <see cref="EndpointBase{TRequest}"/> validates the bound body through
/// <c>Microsoft.Extensions.Validation</c> before the typed handler runs.
/// </summary>
/// <remarks>
/// <para>
/// The failure mode this class exists for is PRD P9: an app that calls <c>AddValidation()</c> and
/// marks its request type still returned 200 on invalid input, because the framework discovers types
/// from hand-written <c>Map</c> calls and the library's are generated. Nothing reported it. Each test
/// runs the real bridge, <see cref="EndpointBase{TRequest}.HandleAsync(HttpContext)"/>, and executes
/// the returned result, so the status code, content type and <c>errors</c> keys are the ones a client
/// would see.
/// </para>
/// <para>
/// The <c>AddValidation()</c> calls below are what make the validation generator emit a resolver for
/// this test assembly's <c>[ValidatableType]</c> fixtures, exactly as it would for a consumer app.
/// </para>
/// </remarks>
public class RequestValidationTests
{
    private sealed class Probe<TRequest> : BodyEndpoint<TRequest>
    {
        public TRequest? Received { get; private set; }
        public bool HandlerRan { get; private set; }

        public override Task<IResult> HandleAsync(TRequest request, CancellationToken cancellationToken)
        {
            HandlerRan = true;
            Received = request;
            return Task.FromResult(Results.Ok());
        }
    }

    private sealed record Outcome(int StatusCode, string? ContentType, string Body)
    {
        public JsonElement Errors
        {
            get
            {
                using var document = JsonDocument.Parse(Body);
                return document.RootElement.GetProperty("errors").Clone();
            }
        }

        public string[] ErrorKeys => [.. Errors.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];
    }

    private static async Task<Outcome> Send<TRequest>(
        EndpointBase<TRequest> endpoint,
        string body,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configureServices?.Invoke(services);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = "application/json";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var result = await endpoint.HandleAsync(context);
        await result.ExecuteAsync(context);

        return new Outcome(context.Response.StatusCode, context.Response.ContentType, Encoding.UTF8.GetString(responseBody.ToArray()));
    }

    /// <summary>
    /// The one <c>AddValidation()</c> call site in this assembly — keep it the only one.
    /// </summary>
    /// <remarks>
    /// On net10.0 the validation generator emits one fixed-name file per call site, so a second call
    /// anywhere in the same compilation makes it throw (CS8785, duplicate hint name
    /// <c>ValidatableInfoResolver.g.cs</c>) and contribute nothing: every test here would then see an
    /// empty resolver and "pass" the no-validation behaviour. Measured; net11.0 does not have the bug.
    /// </remarks>
    private static void WithValidation(IServiceCollection services) => services.AddValidation();

    /// <summary>
    /// An invalid <c>[ValidatableType]</c> body is a 400 <c>application/problem+json</c> with one key
    /// per failing field, and the handler never runs.
    /// </summary>
    /// <remarks>
    /// Also pins the RFC 9457 members: the library's explicit path emits <c>type</c>, <c>title</c> and
    /// <c>status</c>, which the framework's own validation filter omits (spike S2).
    /// </remarks>
    [Fact]
    public async Task InvalidBody_Returns400ProblemDetails_WithFieldKeys()
    {
        var endpoint = new Probe<SignupRequest>();

        var outcome = await Send(endpoint, """{"name":null,"age":0,"email":"not-an-email"}""", WithValidation);

        Assert.Equal(StatusCodes.Status400BadRequest, outcome.StatusCode);
        Assert.StartsWith("application/problem+json", outcome.ContentType);
        Assert.Equal(["Age", "Email", "Name"], outcome.ErrorKeys);
        Assert.False(endpoint.HandlerRan);

        using var document = JsonDocument.Parse(outcome.Body);
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("type", out _));
        Assert.True(document.RootElement.TryGetProperty("title", out _));
    }

    /// <summary>A valid body reaches the handler unchanged.</summary>
    [Fact]
    public async Task ValidBody_RunsTheHandler()
    {
        var endpoint = new Probe<SignupRequest>();

        var outcome = await Send(endpoint, """{"name":"Ann","age":30,"email":"ann@example.com"}""", WithValidation);

        Assert.Equal(StatusCodes.Status200OK, outcome.StatusCode);
        Assert.True(endpoint.HandlerRan);
        Assert.Equal("Ann", endpoint.Received!.Name);
    }

    /// <summary>
    /// Without <c>AddValidation()</c> the same invalid body reaches the handler: the app behaves
    /// exactly as it did before validation existed.
    /// </summary>
    /// <remarks>
    /// The guard that makes this true is the type-info lookup returning false, not a null check on
    /// the options — <c>IOptions&lt;ValidationOptions&gt;</c> resolves whether or not
    /// <c>AddValidation()</c> was called (spike S2). A guard written the other way round would either
    /// never fire or throw here.
    /// </remarks>
    [Fact]
    public async Task WithoutAddValidation_InvalidBody_StillRunsTheHandler()
    {
        var endpoint = new Probe<SignupRequest>();

        var outcome = await Send(endpoint, """{"name":null,"age":0,"email":"not-an-email"}""");

        Assert.Equal(StatusCodes.Status200OK, outcome.StatusCode);
        Assert.True(endpoint.HandlerRan);
    }

    /// <summary>
    /// Attributes on a positional record parameter — <c>record R([Required] string Name)</c>, no
    /// <c>property:</c> target — are enforced.
    /// </summary>
    /// <remarks>
    /// Written without a target, the attribute lands on the constructor parameter, not the property.
    /// This pins that the validation generator reads it there anyway, which is what MPEP015 and the
    /// sample's <c>CreateUserRequest</c> both rely on.
    /// </remarks>
    [Fact]
    public async Task PositionalRecordParameterAttributes_AreEnforced()
    {
        var endpoint = new Probe<PositionalSignupRequest>();

        var outcome = await Send(endpoint, """{"name":null,"level":0}""", WithValidation);

        Assert.Equal(StatusCodes.Status400BadRequest, outcome.StatusCode);
        Assert.Equal(["Level", "Name"], outcome.ErrorKeys);
        Assert.False(endpoint.HandlerRan);
    }

    /// <summary>
    /// <see cref="IValidatableObject"/> results report under the member names they carry, and a result
    /// carrying none lands under the empty key rather than being dropped.
    /// </summary>
    [Fact]
    public async Task ValidatableObject_ReportsMemberKeys_AndTheEmptyKey()
    {
        var endpoint = new Probe<OrderRequest>();

        var outcome = await Send(endpoint, """{"quantity":5,"maxQuantity":2}""", WithValidation);

        Assert.Equal(StatusCodes.Status400BadRequest, outcome.StatusCode);
        Assert.Equal(["", "MaxQuantity", "Quantity"], outcome.ErrorKeys);
        Assert.Contains("cannot be fulfilled", outcome.Errors.GetProperty("").ToString());
    }

    /// <summary>
    /// A nested member reports as <c>Inner.Name</c>, and its type needs no attribute of its own — the
    /// generated resolver recurses from the marked root.
    /// </summary>
    [Fact]
    public async Task NestedMember_ReportsItsPath_WithoutMarkingTheInnerType()
    {
        var endpoint = new Probe<ShipmentRequest>();

        var outcome = await Send(endpoint, """{"title":"Box","inner":{"name":null}}""", WithValidation);

        Assert.Equal(StatusCodes.Status400BadRequest, outcome.StatusCode);
        Assert.Equal(["Inner.Name"], outcome.ErrorKeys);
    }

    private sealed class CustomFailure : BodyEndpoint<SignupRequest>
    {
        public IReadOnlyDictionary<string, string[]>? Seen { get; private set; }

        public override Task<IResult> HandleAsync(SignupRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());

        protected override ValueTask<IResult> OnValidationFailedAsync(HttpContext context, IReadOnlyDictionary<string, string[]> errors)
        {
            Seen = errors;
            return new(Results.UnprocessableEntity());
        }
    }

    /// <summary>
    /// An override of <see cref="EndpointBase{TRequest}.OnValidationFailedAsync"/> decides the
    /// response, and receives the errors as <c>string[]</c> per key on every target framework.
    /// </summary>
    /// <remarks>
    /// .NET 11 reports errors as <c>IReadOnlyList&lt;ValidationError&gt;</c>; the hook's signature
    /// must not change with the TFM, so the library normalises it. This test compiles against both.
    /// </remarks>
    [Fact]
    public async Task OnValidationFailedAsync_Override_IsHonoured()
    {
        var endpoint = new CustomFailure();

        var outcome = await Send(endpoint, """{"name":null,"age":30}""", WithValidation);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, outcome.StatusCode);
        var messages = Assert.Contains("Name", endpoint.Seen!);
        Assert.NotEmpty(Assert.Single(messages));
    }

    private sealed class BindThenValidate : BodyEndpoint<SignupRequest>
    {
        public bool ValidationFailureReported { get; private set; }
        public bool BindFailureReported { get; private set; }

        public override Task<IResult> HandleAsync(SignupRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());

        protected override ValueTask<IResult> OnBindFailedAsync(HttpContext context, EndpointBindingException? failure)
        {
            BindFailureReported = true;
            return base.OnBindFailedAsync(context, failure);
        }

        protected override ValueTask<IResult> OnValidationFailedAsync(HttpContext context, IReadOnlyDictionary<string, string[]> errors)
        {
            ValidationFailureReported = true;
            return base.OnValidationFailedAsync(context, errors);
        }
    }

    /// <summary>
    /// A body that cannot be bound is a bind failure only; validation never runs on it.
    /// </summary>
    /// <remarks>
    /// Ordering guarantee: parameter binding, body binding, validation, handler. Reporting a malformed
    /// body through the validation hook too would hand an override a request that does not exist.
    /// </remarks>
    [Fact]
    public async Task BindFailure_DoesNotRunValidation()
    {
        var endpoint = new BindThenValidate();

        var outcome = await Send(endpoint, "{ not json", WithValidation);

        Assert.Equal(StatusCodes.Status400BadRequest, outcome.StatusCode);
        Assert.True(endpoint.BindFailureReported);
        Assert.False(endpoint.ValidationFailureReported);
    }

    private sealed class RouteRangeEndpoint : BodyEndpoint<SignupRequest>
    {
        [RouteParam, Range(1, 1000)]
        public int Id { get; set; } = -1;

        public int IdSeenByHandler { get; private set; } = -1;

        // Stands in for the generated binder: assigns a route value that violates [Range].
        protected override void BindParameters(HttpContext context) => Id = 0;

        public override Task<IResult> HandleAsync(SignupRequest request, CancellationToken cancellationToken)
        {
            IdSeenByHandler = Id;
            return Task.FromResult(Results.Ok());
        }
    }

    /// <summary>
    /// <c>[Range]</c> on a <c>[RouteParam]</c> endpoint property is <b>not</b> evaluated, even with
    /// validation registered and a validatable body (PRD R5.1a).
    /// </summary>
    /// <remarks>
    /// Validation covers the body only; the endpoint class is not a type the framework knows about.
    /// This pins the documented behaviour so that adding route-value validation is a deliberate
    /// decision with a failing test, not something that drifts in.
    /// </remarks>
    [Fact]
    public async Task RouteParamRange_IsNotEvaluated()
    {
        var endpoint = new RouteRangeEndpoint();

        var outcome = await Send(endpoint, """{"name":"Ann","age":30}""", WithValidation);

        Assert.Equal(StatusCodes.Status200OK, outcome.StatusCode);
        Assert.Equal(0, endpoint.IdSeenByHandler);
    }

    private const string InvalidCreateUser = """{"name":"","email":"not-an-email"}""";

    /// <summary>
    /// A <c>[ValidatableType]</c> declared in another assembly is silently not validated when only the
    /// host's own assembly calls <c>AddValidation()</c> (PRD R5.5).
    /// </summary>
    /// <remarks>
    /// <see cref="CreateUserRequest"/> is marked in the TestApp. This test assembly calls
    /// <c>AddValidation()</c>, but the resolver that call registers covers only this assembly's types,
    /// so the invalid body reaches the handler. That is the claim the README makes; if a framework
    /// update ever starts discovering across assemblies, this fails and the README can be corrected.
    /// </remarks>
    [Fact]
    public async Task CrossAssemblyType_WithOnlyTheHostsAddValidation_IsSilentlyNotValidated()
    {
        var endpoint = new Probe<CreateUserRequest>();

        var outcome = await Send(endpoint, InvalidCreateUser, WithValidation);

        Assert.Equal(StatusCodes.Status200OK, outcome.StatusCode);
        Assert.True(endpoint.HandlerRan);
    }

    /// <summary>
    /// The preferred remedy from spike S2: the declaring assembly calls <c>AddValidation()</c> itself
    /// and exposes it, and the host calls that alongside its own.
    /// </summary>
    [Fact]
    public async Task CrossAssemblyType_WithTheDeclaringAssemblysRegistration_IsValidated()
    {
        var endpoint = new Probe<CreateUserRequest>();

        var outcome = await Send(endpoint, InvalidCreateUser, services =>
        {
            WithValidation(services);
            services.AddTestAppValidation();
        });

        Assert.Equal(StatusCodes.Status400BadRequest, outcome.StatusCode);
        Assert.Equal(["Email", "Name"], outcome.ErrorKeys);
        Assert.False(endpoint.HandlerRan);
    }
}
