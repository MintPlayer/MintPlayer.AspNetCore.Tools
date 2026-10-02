namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Endpoint metadata recording the endpoint class a route was mapped from, so "is endpoint X
/// mapped?" is answered by type instead of by route string. Read it through
/// <see cref="EndpointDataSourceExtensions.IsEndpointMapped(Microsoft.AspNetCore.Routing.EndpointDataSource, Type)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every endpoint this library maps carries exactly one, added by
/// <see cref="EndpointAttributes.ForMetadata"/>, which both the source-generated mapping and
/// <see cref="EndpointRouteBuilderExtensions.MapEndpoint{TEndpoint}"/> call once per endpoint. Recording
/// it there rather than in a line of generated code means a library compiled against an older version
/// of this package gets it too, without a rebuild: its generated mapping calls this assembly's
/// <c>ForMetadata</c> at run time.
/// </para>
/// <para>
/// <see cref="EndpointType"/> is always the <b>closed</b> type, <c>typeof(TEndpoint)</c>, including an
/// open-generic endpoint closed by the application through <c>[assembly: EndpointTypeArgument]</c>.
/// It deliberately carries no <c>[DynamicallyAccessedMembers]</c>: nothing reflects over the type's
/// members, and the annotation would make every unannotated <c>Map&lt;TEndpoint&gt;</c> warn (IL2087).
/// </para>
/// </remarks>
public sealed class EndpointTypeMetadata
{
    /// <summary>Records <paramref name="endpointType"/> as the class an endpoint was mapped from.</summary>
    public EndpointTypeMetadata(Type endpointType)
    {
        ArgumentNullException.ThrowIfNull(endpointType);

        EndpointType = endpointType;
    }

    /// <summary>The closed endpoint class this endpoint was mapped from.</summary>
    public Type EndpointType { get; }
}
