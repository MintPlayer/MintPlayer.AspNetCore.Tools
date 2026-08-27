using System.Text;
using System.Text.Json;
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
/// This is the densest real logic in the runtime library and where its defects live. Each case
/// builds a <see cref="DefaultHttpContext"/> with a real <c>RequestServices</c>, a body stream and
/// a content type — no server needed, since binding reads only the request.
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

    // ---------- JSON fallback path (no MVC registered) ----------

    [Fact]
    public async Task NoMvcRegistered_DeserializesJsonBody()
    {
        var context = ContextWith("""{"id":7,"name":"seven"}""");

        var request = await new Probe().Bind(context);

        Assert.Equal(new Req(7, "seven"), request);
    }

    /// <summary>
    /// An empty body throws <see cref="JsonException"/> — it does <b>not</b> bind to null.
    /// </summary>
    /// <remarks>
    /// Measured, and it corrects the obvious guess. <c>ReadFromJsonAsync</c> on a zero-length body
    /// does not return <c>default</c>; it fails to find a JSON token and throws. So the empty-body
    /// case is a 500 rather than a 400 (still wrong), but it is <i>not</i> a route to D-G5's
    /// null-laundering. See <see cref="NoMvcRegistered_LiteralJsonNullBody_BindsToNull_KnownBug"/>
    /// for the path that actually reaches it.
    /// </remarks>
    [Fact]
    public async Task NoMvcRegistered_EmptyBody_ThrowsJsonException_KnownBug()
    {
        var context = ContextWith(body: null);

        await Assert.ThrowsAsync<JsonException>(async () => await new Probe().Bind(context));
    }

    /// <summary>
    /// A literal JSON <c>null</c> body binds to null, which is how D-G5 is actually reachable.
    /// </summary>
    /// <remarks>
    /// <c>ReadFromJsonAsync</c> deserializes the token <c>null</c> to <c>default</c> — correctly, by
    /// JSON semantics. The bridge then launders it through <c>request!</c> into a handler whose
    /// signature promises non-null. This is a body clients genuinely send, so D-G5 is a real defect
    /// and not a theoretical one; the empty-body case above just is not how you get there.
    /// </remarks>
    [Fact]
    public async Task NoMvcRegistered_LiteralJsonNullBody_BindsToNull_KnownBug()
    {
        var context = ContextWith("null");

        Assert.Null(await new Probe().Bind(context));
    }

    /// <summary>Whitespace-only bodies fail the same way as empty ones.</summary>
    [Fact]
    public async Task NoMvcRegistered_WhitespaceBody_ThrowsJsonException_KnownBug()
    {
        var context = ContextWith("   ");

        await Assert.ThrowsAsync<JsonException>(async () => await new Probe().Bind(context));
    }

    /// <summary>
    /// Malformed JSON escapes as a <see cref="JsonException"/>.
    /// </summary>
    /// <remarks>
    /// D-G5b. Nothing catches this, so an application returns 500 for what is unambiguously a
    /// client error and should be a 400.
    /// </remarks>
    [Fact]
    public async Task NoMvcRegistered_MalformedJson_ThrowsJsonException_KnownBug()
    {
        var context = ContextWith("{ this is not json");

        await Assert.ThrowsAsync<JsonException>(async () => await new Probe().Bind(context));
    }

    /// <summary>
    /// A non-JSON content type escapes as an <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <remarks>
    /// D-G5c. Should be a 415 Unsupported Media Type; is a 500.
    /// </remarks>
    [Fact]
    public async Task NoMvcRegistered_NonJsonContentType_ThrowsInvalidOperation_KnownBug()
    {
        var context = ContextWith("id=7&name=seven", contentType: "application/x-www-form-urlencoded");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new Probe().Bind(context));
    }

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
    /// A formatter returning <c>NoValue</c> lets the loop continue, and the JSON fallback then
    /// reads a body the formatter may already have consumed.
    /// </summary>
    /// <remarks>
    /// D-G7, with the symptom corrected by measurement. The formatter claimed the body
    /// (<c>CanRead</c> true), consumed the stream, then declined to produce a model. The loop falls
    /// through to <c>ReadFromJsonAsync</c>, which sees a drained stream and throws
    /// <see cref="JsonException"/> — not, as first assumed, a silent null.
    /// <para>
    /// That makes the observable failure a 500 with a JSON parse error pointing at position 0, for
    /// a request whose body was perfectly valid JSON. The diagnostic actively misleads: it blames
    /// the client's payload for what is a double-read inside the library.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task WithMvcRegistered_FormatterConsumesBodyThenReturnsNoValue_ThrowsMisleadingJsonException_KnownBug()
    {
        var draining = new RecordingFormatter(
            canRead: true,
            read: async context =>
            {
                await new StreamReader(context.HttpContext.Request.Body).ReadToEndAsync();
                return InputFormatterResult.NoValue();
            });

        var context = ContextWithFormatters("""{"id":5,"name":"five"}""", draining);

        await Assert.ThrowsAsync<JsonException>(async () => await new Probe().Bind(context));

        Assert.Equal(1, draining.ReadCalls);
    }

    /// <summary>
    /// Formatter validation errors are discarded.
    /// </summary>
    /// <remarks>
    /// D-G7b. The <c>ModelStateDictionary</c> handed to the formatter is constructed inline and
    /// never read, so every error a formatter records is thrown away. The caller cannot tell a
    /// validation failure from an empty body.
    /// </remarks>
    [Fact]
    public async Task WithMvcRegistered_FormatterFailure_ModelStateErrorsAreDiscarded_KnownBug()
    {
        var failing = new RecordingFormatter(
            canRead: true,
            read: context =>
            {
                context.ModelState.AddModelError("Id", "must be positive");
                return Task.FromResult(InputFormatterResult.Failure());
            });

        // Falls through to the JSON fallback on a body that is valid JSON, so the invalid-model
        // signal is lost entirely and binding "succeeds".
        var request = await new Probe().Bind(ContextWithFormatters("""{"id":-1,"name":"neg"}""", failing));

        Assert.Equal(new Req(-1, "neg"), request);
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
