namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Signals that the library itself could not turn the request into a <c>TRequest</c>, and says which
/// HTTP status code describes that failure.
/// </summary>
/// <remarks>
/// This exists so the bridge in <see cref="EndpointBase{TRequest}"/> can tell "the client sent
/// something the library cannot bind" apart from "the endpoint's own binding code threw". Only the
/// former is translated into a response; anything else propagates, because the library has no idea
/// what it means.
/// <para>
/// It is thrown, rather than returned, because <c>BindRequestAsync</c> is a user-overridable member
/// returning <c>TRequest?</c> — there is nowhere in that signature to put a failure, and widening it
/// would push the whole result-or-error ceremony onto every endpoint that binds by hand.
/// </para>
/// </remarks>
public sealed class EndpointBindingException : Exception
{
    /// <summary>Creates a binding failure that should produce <paramref name="statusCode"/>.</summary>
    public EndpointBindingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
        => StatusCode = statusCode;

    /// <summary>The HTTP status code this binding failure should produce. 400 or 415.</summary>
    public int StatusCode { get; }
}
