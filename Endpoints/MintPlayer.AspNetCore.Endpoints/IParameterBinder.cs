using System.ComponentModel;
using Microsoft.AspNetCore.Http;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Binds a raw endpoint's <c>[RouteParam]</c>/<c>[QueryParam]</c> properties before its
/// <c>HandleAsync(HttpContext)</c> runs. Implemented by generated code, not by hand.
/// </summary>
/// <remarks>
/// Only raw endpoints need this. Every other level derives from a library base class whose
/// <c>HandleAsync(HttpContext)</c> binds the properties itself, inside the same
/// <c>try</c> that catches a malformed body — so the parameters are always bound first and a bad
/// route value is rejected without the body ever being read. A raw endpoint writes
/// <c>HandleAsync(HttpContext)</c> itself, so there is no base method to hook, and the mapper has
/// to call this on its behalf. It is implemented explicitly, so it stays off the endpoint's public
/// surface.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IParameterBinder
{
    /// <summary>
    /// Assigns the bound properties.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> on success; otherwise the response to send instead of invoking the
    /// endpoint.
    /// </returns>
    IResult? BindParameters(HttpContext context);
}
