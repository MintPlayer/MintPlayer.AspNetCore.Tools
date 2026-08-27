using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.Endpoints;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// Covers <see cref="BodyEndpoint{TRequest}"/>'s request binding: the MVC input-formatter path,
/// the JSON fallback, and the interaction between them.
/// </summary>
/// <remarks>
/// This is the densest real logic in the runtime library. Each case builds a
/// <see cref="DefaultHttpContext"/> with a real <c>RequestServices</c>, a body stream and a content
/// type — no server needed, since binding reads only the request.
/// <para>
/// Every failure now arrives as one type, <see cref="EndpointBindingException"/>, carrying the status
/// code the client deserves. Before, three different exception types escaped — <c>JsonException</c>,
/// <c>InvalidOperationException</c>, and a silent null — and all three became a 500.
/// </para>
/// </remarks>
public class BodyEndpointBindingTests
{
    public sealed record Req(int Id, string Name);

    /// <summary>Exposes the protected binder so it can be driven directly.</summary>
    private sealed class Probe : BodyEndpoint<Req>
    {
        public ValueTask<Req?> Bind(HttpContext context) => BindRequestAsync(context);

        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    private static DefaultHttpContext ContextWith(
        string? body,
        string? contentType = "application/json",
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        var bytes = body is null ? [] : Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = contentType;

        return context;
    }

    private static async Task<EndpointBindingException> BindFailure(HttpContext context)
        => await Assert.ThrowsAsync<EndpointBindingException>(async () => await new Probe().Bind(context));

    // ---------- JSON fallback path (no MVC registered) ----------

    [Fact]
    public async Task NoMvcRegistered_DeserializesJsonBody()
    {
        var context = ContextWith("""{"id":7,"name":"seven"}""");

        var request = await new Probe().Bind(context);

        Assert.Equal(new Req(7, "seven"), request);
    }

    /// <summary>
    /// An empty body is a 400, not a 500.
    /// </summary>
    /// <remarks>
    /// <c>ReadFromJsonAsync</c> on a zero-length body does not return <c>default</c> — it fails to
    /// find a JSON token and throws. Nothing caught that, so an application returned 500 for a
    /// request that is unambiguously the client's fault.
    /// </remarks>
    [Fact]
    public async Task NoMvcRegistered_EmptyBody_IsABadRequest()
    {
        var failure = await BindFailure(ContextWith(body: null));

        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
    }

    /// <summary>Whitespace-only bodies fail the same way as empty ones.</summary>
    [Fact]
    public async Task NoMvcRegistered_WhitespaceBody_IsABadRequest()
    {
        var failure = await BindFailure(ContextWith("   "));

        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
    }

    /// <summary>Malformed JSON is a 400.</summary>
    [Fact]
    public async Task NoMvcRegistered_MalformedJson_IsABadRequest()
    {
        var failure = await BindFailure(ContextWith("{ this is not json"));

        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
        Assert.IsType<System.Text.Json.JsonException>(failure.InnerException);
    }

    /// <summary>
    /// A non-JSON content type is a 415, not a 500.
    /// </summary>
    /// <remarks>
    /// <c>ReadFromJsonAsync</c> reports an unsupported content type as an
    /// <c>InvalidOperationException</c>, which reads like a bug in the application rather than a
    /// request the endpoint cannot accept.
    /// </remarks>
    [Fact]
    public async Task NoMvcRegistered_NonJsonContentType_IsUnsupportedMediaType()
    {
        var failure = await BindFailure(
            ContextWith("id=7&name=seven", contentType: "application/x-www-form-urlencoded"));

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, failure.StatusCode);
        Assert.Contains("application/x-www-form-urlencoded", failure.Message);
    }

    /// <summary>
    /// A literal JSON <c>null</c> body binds to null, which the bridge — not the binder — turns into
    /// a 400.
    /// </summary>
    /// <remarks>
    /// <c>ReadFromJsonAsync</c> deserializes the token <c>null</c> to <c>default</c>, correctly by
    /// JSON semantics, so the binder has nothing to complain about. This is a body clients genuinely
    /// send; see <c>EndpointBaseTests</c> for the half that stops it reaching a handler whose
    /// signature promises a non-null request.
    /// </remarks>
    [Fact]
    public async Task NoMvcRegistered_LiteralJsonNullBody_BindsToNull()
    {
        var context = ContextWith("null");

        Assert.Null(await new Probe().Bind(context));
    }

    /// <summary>Cancellation is not a binding failure and must propagate untouched.</summary>
    [Fact]
    public async Task NoMvcRegistered_HonoursRequestAborted()
    {
        using var cts = new CancellationTokenSource();
        var context = ContextWith("""{"id":1,"name":"x"}""");
        context.RequestAborted = cts.Token;
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new Probe().Bind(context));
    }

    /// <summary>
    /// With an <c>IModelMetadataProvider</c> present but no <c>MvcOptions</c>, binding falls back
    /// to JSON — the null-conditional on the options lookup.
    /// </summary>
    [Fact]
    public async Task ModelMetadataProviderWithoutMvcOptions_FallsBackToJson()
    {
        var context = ContextWith(
            """{"id":3,"name":"three"}""",
            configureServices: services => services.AddSingleton<
                Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider>(
                    new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider()));

        Assert.Equal(new Req(3, "three"), await new Probe().Bind(context));
    }

    // ---------- MVC input-formatter path ----------

    /// <summary>A formatter that records whether it was consulted and what it did.</summary>
    private sealed class RecordingFormatter(bool canRead, Func<InputFormatterContext, Task<InputFormatterResult>> read)
        : IInputFormatter
    {
        public int CanReadCalls { get; private set; }
        public int ReadCalls { get; private set; }

        public bool CanRead(InputFormatterContext context)
        {
            CanReadCalls++;
            return canRead;
        }

        public Task<InputFormatterResult> ReadAsync(InputFormatterContext context)
        {
            ReadCalls++;
            return read(context);
        }
    }

    private static DefaultHttpContext ContextWithFormatters(
        string? body,
        params IInputFormatter[] formatters)
        => ContextWith(body, configureServices: services =>
        {
            services.AddControllers();
            services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
            {
                options.InputFormatters.Clear();
                foreach (var formatter in formatters)
                {
                    options.InputFormatters.Add(formatter);
                }
            });
        });

    [Fact]
    public async Task WithMvcRegistered_UsesTheFirstFormatterThatCanRead()
    {
        var expected = new Req(9, "nine");
        var formatter = new RecordingFormatter(
            canRead: true,
            read: _ => Task.FromResult(InputFormatterResult.Success(expected)));

        var request = await new Probe().Bind(ContextWithFormatters("ignored", formatter));

        Assert.Equal(expected, request);
        Assert.Equal(1, formatter.ReadCalls);
    }

    [Fact]
    public async Task WithMvcRegistered_SkipsFormattersThatCannotRead()
    {
        var skipped = new RecordingFormatter(
            canRead: false,
            read: _ => throw new InvalidOperationException("must not be read"));
        var used = new RecordingFormatter(
            canRead: true,
            read: _ => Task.FromResult(InputFormatterResult.Success(new Req(1, "one"))));

        await new Probe().Bind(ContextWithFormatters("ignored", skipped, used));

        Assert.Equal(1, skipped.CanReadCalls);
        Assert.Equal(0, skipped.ReadCalls);
        Assert.Equal(1, used.ReadCalls);
    }

    /// <summary>
    /// A formatter that consumes the body and then returns <c>NoValue</c> does not spoil the JSON
    /// fallback: the body is rewound and the fallback reads it in full.
    /// </summary>
    /// <remarks>
    /// The formatter is entitled to do this — it claimed the body with <c>CanRead</c> and then decided
    /// it had nothing to produce. What used to happen next was that the fallback read a drained
    /// stream and threw a <c>JsonException</c>, surfacing as a 500 with a parse error at position 0
    /// for a request whose body was perfectly valid JSON. The diagnostic actively misled: it blamed
    /// the client's payload for a double-read inside the library.
    /// </remarks>
    [Fact]
    public async Task WithMvcRegistered_FormatterConsumesBodyThenReturnsNoValue_FallsBackToJsonSuccessfully()
    {
        var draining = new RecordingFormatter(
            canRead: true,
            read: async context =>
            {
                await new StreamReader(context.HttpContext.Request.Body).ReadToEndAsync();
                return InputFormatterResult.NoValue();
            });

        var context = ContextWithFormatters("""{"id":5,"name":"five"}""", draining);

        Assert.Equal(new Req(5, "five"), await new Probe().Bind(context));
        Assert.Equal(1, draining.ReadCalls);
    }

    /// <summary>
    /// The next formatter also sees a full body, not the leftovers of the previous one.
    /// </summary>
    [Fact]
    public async Task WithMvcRegistered_BodyIsRewoundBetweenFormatters()
    {
        string? secondFormatterSawBody = null;

        var draining = new RecordingFormatter(
            canRead: true,
            read: async context =>
            {
                await new StreamReader(context.HttpContext.Request.Body).ReadToEndAsync();
                return InputFormatterResult.NoValue();
            });
        var second = new RecordingFormatter(
            canRead: true,
            read: async context =>
            {
                secondFormatterSawBody = await new StreamReader(context.HttpContext.Request.Body).ReadToEndAsync();
                return InputFormatterResult.Success(new Req(1, "one"));
            });

        await new Probe().Bind(ContextWithFormatters("""{"id":1,"name":"one"}""", draining, second));

        Assert.Equal("""{"id":1,"name":"one"}""", secondFormatterSawBody);
    }

    /// <summary>
    /// A formatter's validation errors reach the caller instead of being thrown away.
    /// </summary>
    /// <remarks>
    /// The <c>ModelStateDictionary</c> handed to the formatter was constructed inline and never read,
    /// so every error a formatter recorded was discarded — and binding then "succeeded" by falling
    /// through to JSON on a body that happens to parse. The caller could not tell a validation
    /// failure from an empty body.
    /// </remarks>
    [Fact]
    public async Task WithMvcRegistered_FormatterFailure_SurfacesTheModelStateErrors()
    {
        var failing = new RecordingFormatter(
            canRead: true,
            read: context =>
            {
                context.ModelState.AddModelError("Id", "must be positive");
                return Task.FromResult(InputFormatterResult.Failure());
            });

        var failure = await BindFailure(ContextWithFormatters("""{"id":-1,"name":"neg"}""", failing));

        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
        Assert.Contains("Id", failure.Message);
        Assert.Contains("must be positive", failure.Message);
    }

    /// <summary>
    /// A formatter that declines without recording anything is not an error — the next formatter, and
    /// then the JSON fallback, still get their turn.
    /// </summary>
    [Fact]
    public async Task WithMvcRegistered_FormatterDeclinesWithNoErrors_IsNotAFailure()
    {
        var declining = new RecordingFormatter(
            canRead: true,
            read: _ => Task.FromResult(InputFormatterResult.NoValue()));

        Assert.Equal(
            new Req(4, "four"),
            await new Probe().Bind(ContextWithFormatters("""{"id":4,"name":"four"}""", declining)));
    }

    [Fact]
    public async Task WithMvcRegistered_ButNoInputFormatters_FallsBackToJson()
    {
        var context = ContextWithFormatters("""{"id":2,"name":"two"}""");

        Assert.Equal(new Req(2, "two"), await new Probe().Bind(context));
    }

    /// <summary>An override replaces the base binding entirely.</summary>
    private sealed class OverridingProbe : BodyEndpoint<Req>
    {
        public ValueTask<Req?> Bind(HttpContext context) => BindRequestAsync(context);

        protected override ValueTask<Req?> BindRequestAsync(HttpContext context)
            => new(new Req(-99, "from-override"));

        public override Task<IResult> HandleAsync(Req request, CancellationToken cancellationToken)
            => Task.FromResult(Results.Ok());
    }

    [Fact]
    public async Task Override_ReplacesFormatterAndJsonLogicEntirely()
    {
        var formatter = new RecordingFormatter(
            canRead: true,
            read: _ => throw new InvalidOperationException("must not run"));
        var context = ContextWithFormatters("""{"id":1,"name":"one"}""", formatter);

        Assert.Equal(new Req(-99, "from-override"), await new OverridingProbe().Bind(context));
        Assert.Equal(0, formatter.CanReadCalls);
    }
}
