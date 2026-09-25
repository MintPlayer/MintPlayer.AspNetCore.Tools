using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MintPlayer.AspNetCore.Endpoints;
using Xunit;
using TestAppEndpoints = MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// Where parameter binding sits in the request pipeline (M4, PRD R2.11, R2.13): before the body,
/// through the one <c>OnBindFailedAsync</c> customisation point, and identically whether a raw
/// endpoint is mapped by the generated <c>Map&lt;TEndpoint&gt;</c> or by the manual
/// <c>MapEndpoint&lt;TEndpoint&gt;()</c>.
/// </summary>
/// <remarks>
/// This assembly does not run the endpoint generator, so the fixtures declared here write their
/// <c>BindParameters</c> override / <c>IParameterBinder</c> by hand, in exactly the shape the
/// generator emits. What is under test is the runtime that calls them. The generated binders
/// themselves are exercised through the TestApp, whose endpoints were compiled with the generator.
/// </remarks>
public class ParameterBindingPipelineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ParameterBindingPipelineTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    public sealed record Body(string Name);

    /// <summary>A stream that records every read, so a test can prove the body was never touched.</summary>
    private sealed class RecordingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int Reads;

        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads++;
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            Reads++;
            return base.Read(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Reads++;
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    /// <summary>A PUT with a route-bound id, its binder written as the generator writes it.</summary>
    private sealed class UpdateEndpoint : PutEndpoint<Body>, IPutEndpoint<Body>
    {
        public static string Path => "/items/{id}";

        [RouteParam] public int Id { get; set; }

        public Body? Handled { get; private set; }

        protected override void BindParameters(HttpContext context)
        {
            Id = ParameterBinding.Parsable<int>(context, ParameterSource.Route, "Id");
        }

        public override Task<IResult> HandleAsync(Body request, CancellationToken cancellationToken)
        {
            Handled = request;
            return Task.FromResult<IResult>(Results.Ok(new { id = Id, name = request.Name }));
        }
    }

    /// <summary>Same as <see cref="UpdateEndpoint"/>, but customises binding failures.</summary>
    private sealed class CustomisedUpdateEndpoint : PutEndpoint<Body>, IPutEndpoint<Body>
    {
        public static string Path => "/items/{id}";

        [RouteParam] public int Id { get; set; }

        public EndpointBindingException? SeenFailure { get; private set; }

        protected override void BindParameters(HttpContext context)
        {
            Id = ParameterBinding.Parsable<int>(context, ParameterSource.Route, "Id");
        }

        protected override ValueTask<IResult> OnBindFailedAsync(HttpContext context, EndpointBindingException? failure)
        {
            SeenFailure = failure;
            return new(Results.StatusCode(StatusCodes.Status422UnprocessableEntity));
        }

        public override Task<IResult> HandleAsync(Body request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    /// <summary>The response-only rung, with a customised failure.</summary>
    private sealed class GetEndpointWithCustomFailure : ResponseEndpoint, IGetEndpoint<Body>
    {
        public static string Path => "/items/{id}";

        [RouteParam] public int Id { get; set; }

        public bool Handled { get; private set; }
        public EndpointBindingException? SeenFailure { get; private set; }

        protected override void BindParameters(HttpContext context)
        {
            Id = ParameterBinding.Parsable<int>(context, ParameterSource.Route, "Id");
        }

        protected override ValueTask<IResult> OnBindFailedAsync(HttpContext context, EndpointBindingException failure)
        {
            SeenFailure = failure;
            return new(Results.StatusCode(StatusCodes.Status422UnprocessableEntity));
        }

        public override Task<IResult> HandleAsync(CancellationToken cancellationToken)
        {
            Handled = true;
            return Task.FromResult<IResult>(Results.Ok(Id));
        }
    }

    /// <summary>The response-only rung with the default failure handling.</summary>
    private sealed class PlainGetEndpoint : ResponseEndpoint, IGetEndpoint<Body>
    {
        public static string Path => "/items/{id}";

        [RouteParam] public int Id { get; set; }

        public bool Handled { get; private set; }

        protected override void BindParameters(HttpContext context)
        {
            Id = ParameterBinding.Parsable<int>(context, ParameterSource.Route, "Id");
        }

        public override Task<IResult> HandleAsync(CancellationToken cancellationToken)
        {
            Handled = true;
            return Task.FromResult<IResult>(Results.Ok(Id));
        }
    }

    private static DefaultHttpContext PutContext(string routeId, RecordingStream body)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        context.Request.RouteValues["id"] = routeId;
        context.Request.Body = body;
        context.Request.ContentLength = body.Length;
        context.Request.ContentType = "application/json";
        return context;
    }

    private static RecordingStream ValidBody() => new(Encoding.UTF8.GetBytes("""{"name":"Updated"}"""));

    // ---- ordering ---------------------------------------------------------------------------

    /// <summary>
    /// A malformed route value on a body endpoint is a 400 <b>without the body being read</b>
    /// (R2.11). Asserted on the ordering — zero reads on the body stream — not only on the status.
    /// </summary>
    /// <remarks>
    /// Parameters bind first, inside the same <c>try</c> as the body. The reverse order would still
    /// answer 400, so a status-only assertion could not tell them apart; but it would read, buffer
    /// and deserialize an arbitrarily large body for a request already known to be bad.
    /// </remarks>
    [Fact]
    public async Task BadRouteValue_OnABodyEndpoint_Is400_AndTheBodyIsNeverRead()
    {
        var body = ValidBody();
        var endpoint = new UpdateEndpoint();

        var result = await endpoint.HandleAsync(PutContext("abc", body));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("The route parameter 'Id' must be a valid Int32; 'abc' is not.", problem.ProblemDetails.Detail);
        Assert.Equal(0, body.Reads);
        Assert.Null(endpoint.Handled);
    }

    /// <summary>
    /// The positive control for the test above: with a good route value the body <i>is</i> read, so
    /// the zero-read assertion is not passing because the stream can never be observed.
    /// </summary>
    [Fact]
    public async Task GoodRouteValue_OnABodyEndpoint_BindsBothTheRouteAndTheBody()
    {
        var body = ValidBody();
        var endpoint = new UpdateEndpoint();

        await endpoint.HandleAsync(PutContext("7", body));

        Assert.True(body.Reads > 0);
        Assert.Equal(7, endpoint.Id);
        Assert.Equal(new Body("Updated"), endpoint.Handled);
    }

    // ---- OnBindFailedAsync --------------------------------------------------------------------

    /// <summary>
    /// A parameter failure reaches the endpoint's <c>OnBindFailedAsync</c> override on a body
    /// endpoint, carrying the binding exception — the same customisation point body failures use.
    /// </summary>
    [Fact]
    public async Task ParameterFailure_OnABodyEndpoint_ReachesTheOnBindFailedAsyncOverride()
    {
        var endpoint = new CustomisedUpdateEndpoint();

        var result = await endpoint.HandleAsync(PutContext("abc", ValidBody()));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, Assert.IsType<StatusCodeHttpResult>(result).StatusCode);
        Assert.NotNull(endpoint.SeenFailure);
        Assert.Contains("'abc'", endpoint.SeenFailure!.Message);
    }

    /// <summary>
    /// The response-only rung has its own <c>OnBindFailedAsync</c>, and a parameter failure reaches
    /// the override there too, without the handler running.
    /// </summary>
    [Fact]
    public async Task ParameterFailure_OnAResponseOnlyEndpoint_ReachesTheOnBindFailedAsyncOverride()
    {
        var endpoint = new GetEndpointWithCustomFailure();
        var context = new DefaultHttpContext();
        context.Request.RouteValues["id"] = "abc";

        var result = await endpoint.HandleAsync(context);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, Assert.IsType<StatusCodeHttpResult>(result).StatusCode);
        Assert.NotNull(endpoint.SeenFailure);
        Assert.False(endpoint.Handled);
    }

    /// <summary>
    /// Default handling on the response-only rung: a bad value is a problem-details 400 and the
    /// handler does not run; a good value binds and the handler does.
    /// </summary>
    [Fact]
    public async Task ResponseOnlyEndpoint_DefaultFailureIs400_AndSuccessRunsTheHandler()
    {
        var bad = new PlainGetEndpoint();
        var badContext = new DefaultHttpContext();
        badContext.Request.RouteValues["id"] = "abc";

        var problem = Assert.IsType<ProblemHttpResult>(await bad.HandleAsync(badContext));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.False(bad.Handled);

        var good = new PlainGetEndpoint();
        var goodContext = new DefaultHttpContext();
        goodContext.Request.RouteValues["id"] = "42";

        await good.HandleAsync(goodContext);
        Assert.True(good.Handled);
        Assert.Equal(42, good.Id);
    }

    // ---- raw endpoints: manual MapEndpoint vs generated Map --------------------------------

    /// <summary>A raw endpoint with a hand-written explicit binder, shaped like the generated one.</summary>
    private sealed class RawJournal
    {
        public int Handled;
        public int? LastId;
    }

    private sealed class RawBoundEndpoint(RawJournal journal) : IGetEndpoint, IParameterBinder
    {
        public static string Path => "/raw/{id}";

        [RouteParam] public int Id { get; set; }

        IResult? IParameterBinder.BindParameters(HttpContext context)
        {
            try
            {
                Id = ParameterBinding.Parsable<int>(context, ParameterSource.Route, "Id");
                return null;
            }
            catch (EndpointBindingException failure)
            {
                return ParameterBinding.Failure(failure);
            }
        }

        public Task<IResult> HandleAsync(HttpContext httpContext)
        {
            Interlocked.Increment(ref journal.Handled);
            journal.LastId = Id;
            return Task.FromResult<IResult>(Results.Ok(Id));
        }
    }

    private static async Task<IHost> StartHost(Action<IEndpointRouteBuilder> map, Action<IServiceCollection>? services = null)
        => await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(collection =>
                {
                    collection.AddRouting();
                    collection.AddProblemDetails();
                    services?.Invoke(collection);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(map);
                }))
            .StartAsync();

    /// <summary>
    /// The manual <c>MapEndpoint&lt;T&gt;()</c> calls a raw endpoint's <c>IParameterBinder</c> after
    /// constructing it and before <c>HandleAsync</c>, and a failure short-circuits the handler.
    /// </summary>
    /// <remarks>
    /// A raw endpoint has no base class to bind for it, so the mapper is the only thing that can.
    /// If <c>MapEndpoint</c> forgot to, the property would silently stay 0 and the handler would run
    /// for <c>/raw/abc</c>.
    /// </remarks>
    [Fact]
    public async Task ManualMapEndpoint_CallsTheRawBinder_BeforeTheHandler()
    {
        var journal = new RawJournal();
        using var host = await StartHost(
            endpoints => endpoints.MapEndpoint<RawBoundEndpoint>(),
            services => services.AddSingleton(journal));
        var client = host.GetTestClient();

        var good = await client.GetAsync("/raw/9");
        Assert.Equal(System.Net.HttpStatusCode.OK, good.StatusCode);
        Assert.Equal(9, journal.LastId);

        var bad = await client.GetAsync("/raw/abc");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("The route parameter 'Id' must be a valid Int32; 'abc' is not.", await bad.Content.ReadAsStringAsync());
        Assert.Equal(1, journal.Handled);
    }

    /// <summary>
    /// The TestApp's raw endpoints — compiled with the generator, so carrying <i>generated</i>
    /// <c>IParameterBinder</c> implementations — answer identically when mapped through the manual
    /// <c>MapEndpoint&lt;T&gt;()</c> and through the generated <c>Map&lt;TEndpoint&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The two mappers are separate code that must stay in step (the comment in
    /// <c>MapEndpoint</c> says so). This is the test that holds them to it: same status, same body,
    /// for a good value, a bad value, and an absent defaulted query parameter.
    /// </remarks>
    [Theory]
    [InlineData("DELETE", "/api/users/5")]
    [InlineData("DELETE", "/api/users/abc")]
    [InlineData("GET", "/api/users")]
    [InlineData("GET", "/api/users?page=4")]
    [InlineData("GET", "/api/users?page=xyz")]
    public async Task ManualMapEndpoint_BindsRawEndpointsIdenticallyToTheGeneratedMapping(string method, string url)
    {
        // The sample's user endpoints take a scoped IUserStore in their constructors (since M9's goal
        // check), so this host registers it exactly as the TestApp's Program.cs does.
        using var manualHost = await StartHost(
            endpoints =>
            {
                endpoints.MapEndpoint<TestAppEndpoints.DeleteUser>();
                endpoints.MapEndpoint<TestAppEndpoints.ListUsers>();
            },
            services =>
            {
                services.AddSingleton<MintPlayer.AspNetCore.Endpoints.TestApp.Models.UserData>();
                services.AddScoped<MintPlayer.AspNetCore.Endpoints.TestApp.Models.IUserStore, MintPlayer.AspNetCore.Endpoints.TestApp.Models.InMemoryUserStore>();
            });

        using var manualRequest = new HttpRequestMessage(new HttpMethod(method), url);
        using var generatedRequest = new HttpRequestMessage(new HttpMethod(method), url);

        var manual = await manualHost.GetTestClient().SendAsync(manualRequest);
        var generated = await factory.CreateClient().SendAsync(generatedRequest);

        Assert.Equal(generated.StatusCode, manual.StatusCode);
        Assert.Equal(await Detail(generated), await Detail(manual));
    }

    /// <summary>
    /// The comparable part of a response: the problem <c>detail</c> for a failure, the whole body
    /// otherwise. (A problem's <c>traceId</c> differs per request and per host.)
    /// </summary>
    private static async Task<string> Detail(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode < 400 || text.Length == 0) return text;

        using var document = System.Text.Json.JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() ?? "" : text;
    }
}
