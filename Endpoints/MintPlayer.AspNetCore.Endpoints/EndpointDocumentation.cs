using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// The request-side OpenAPI metadata every endpoint registration declares — generated and manual
/// alike. Not intended to be called by hand.
/// </summary>
/// <remarks>
/// Everything here is in the shared framework, so it is applied whether or not the application
/// produces an OpenAPI document; ApiExplorer, and whatever reads it, sees the same thing either way.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EndpointDocumentation
{
    /// <summary>
    /// Declares a JSON request body of <paramref name="requestType"/>, and the 400 and 415 a body
    /// can fail with (PRD R4.2, R4.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deliberately not <c>.Accepts&lt;T&gt;("application/json")</c>.</b> Measured on net10.0:
    /// an <see cref="IAcceptsMetadata"/> that lists content types is also read by routing's
    /// <c>AcceptsMatcherPolicy</c>, which then answers every other content type itself with a 415
    /// and an empty body — before the endpoint runs. That would replace the library's own 415
    /// <c>problem+json</c> with a bare status, and break every consumer whose MVC input formatters
    /// read XML or anything else. Metadata with <i>no</i> content types is ignored by the matcher,
    /// and the OpenAPI document still gets an identical <c>requestBody</c>, because the document
    /// defaults an unlisted body to <c>application/json</c>.
    /// </para>
    /// </remarks>
    public static void DeclareRequestBody(RouteHandlerBuilder builder, Type requestType)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(requestType);

        builder.WithMetadata(new RequestBodyMetadata(requestType));
        builder.ProducesProblem(StatusCodes.Status400BadRequest);
        builder.ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        KeepDefaultSuccessResponse(builder);
    }

    /// <summary>
    /// Declares the 400 a <c>[RouteParam]</c>/<c>[QueryParam]</c> property produces when its value
    /// cannot be converted (PRD R4.3).
    /// </summary>
    public static void DeclareBindingFailure(RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ProducesProblem(StatusCodes.Status400BadRequest);
        KeepDefaultSuccessResponse(builder);
    }

    /// <summary>
    /// Restores the <c>200</c> ApiExplorer would have assumed, when nothing else declares a success.
    /// </summary>
    /// <remarks>
    /// ApiExplorer only invents a default <c>200</c> for an endpoint with <i>no</i> response
    /// metadata at all. Measured: adding <c>ProducesProblem(400)</c> to an endpoint that declared
    /// nothing else leaves it documenting only the 400 — so a raw <c>ListUsers</c> or a typed
    /// <c>UpdateUser</c> would lose the success response it has always had. This puts that 200
    /// back, but only when no success is declared by anything: a <c>Produces</c> call, the
    /// endpoint's own <c>[ProducesResponseType(204)]</c> (which is an
    /// <see cref="IApiResponseMetadataProvider"/>, not an <see cref="IProducesResponseTypeMetadata"/>,
    /// so both are checked), or its <c>Configure</c> hook. It runs as a <c>Finally</c> convention
    /// so it sees all of them.
    /// </remarks>
    private static void KeepDefaultSuccessResponse(RouteHandlerBuilder builder) =>
        builder.Finally(endpoint =>
        {
            foreach (var metadata in endpoint.Metadata)
            {
                var status = metadata switch
                {
                    IProducesResponseTypeMetadata produces => produces.StatusCode,
                    IApiResponseMetadataProvider provider => provider.StatusCode,
                    _ => 0,
                };

                if (status is >= 200 and < 300)
                    return;
            }

            endpoint.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(void)));
        });

    /// <summary>A request body with no content types, so routing does not filter on it.</summary>
    private sealed class RequestBodyMetadata(Type requestType) : IAcceptsMetadata
    {
        public IReadOnlyList<string> ContentTypes => [];
        public Type? RequestType => requestType;
        public bool IsOptional => false;
    }
}
