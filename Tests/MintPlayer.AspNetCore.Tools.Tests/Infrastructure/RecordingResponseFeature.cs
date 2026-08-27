using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace MintPlayer.AspNetCore.Tools.Tests.Infrastructure;

/// <summary>
/// An <see cref="IHttpResponseFeature"/> that remembers the callbacks handed to
/// <see cref="OnStarting"/> so a test can fire them on demand.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <c>DefaultHttpContext</c>'s built-in response feature implements
/// <c>OnStarting(callback, state)</c> as an <b>empty method</b>: the callback is discarded and
/// never invoked. Any middleware that writes its header inside <c>OnStarting</c> — which is the
/// whole point of both <c>NoSniffMiddleware</c> and <c>ImprovedHstsMiddleware</c> — is therefore
/// invisible to a plain <c>DefaultHttpContext</c> test.
/// </para>
/// <para>
/// The dangerous half is not that a positive assertion fails. It is that a test asserting the
/// header is <i>absent</i> <b>passes for the wrong reason</b>, which is how a suite that proves
/// nothing gets shipped. Tests use <see cref="StartingCallbackCount"/> to assert the callback was
/// registered at all, separately from asserting what it did.
/// </para>
/// <para>
/// Do <b>not</b> assert callback <i>ordering</i> against this type. Real servers fire
/// <c>OnStarting</c> callbacks in reverse registration order (a stack, in Kestrel); this
/// implementation fires them in registration order. Ordering is only trustworthy against a real
/// server loop, so it is asserted in the <c>TestServer</c> groups instead.
/// </para>
/// </remarks>
internal sealed class RecordingResponseFeature : IHttpResponseFeature
{
    private readonly List<(Func<object, Task> Callback, object State)> starting = [];
    private readonly List<(Func<object, Task> Callback, object State)> completed = [];

    public int StatusCode { get; set; } = 200;
    public string? ReasonPhrase { get; set; }
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public Stream Body { get; set; } = Stream.Null;

    /// <summary>Always false, so middleware never short-circuits on it.</summary>
    public bool HasStarted => false;

    /// <summary>How many callbacks were registered. Assert this, not just the effect.</summary>
    public int StartingCallbackCount => starting.Count;

    public int CompletedCallbackCount => completed.Count;

    public void OnStarting(Func<object, Task> callback, object state) => starting.Add((callback, state));

    public void OnCompleted(Func<object, Task> callback, object state) => completed.Add((callback, state));

    /// <summary>Fires the registered <c>OnStarting</c> callbacks in registration order.</summary>
    public async Task FireOnStartingAsync()
    {
        foreach (var (callback, state) in starting)
        {
            await callback(state);
        }
    }

    public async Task FireOnCompletedAsync()
    {
        foreach (var (callback, state) in completed)
        {
            await callback(state);
        }
    }

    /// <summary>
    /// Builds a <see cref="DefaultHttpContext"/> whose response feature is a
    /// <see cref="RecordingResponseFeature"/>, and returns both.
    /// </summary>
    public static (DefaultHttpContext Context, RecordingResponseFeature Response) CreateContext()
    {
        var feature = new RecordingResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(feature);
        return (context, feature);
    }
}
