using System.Reflection;
using System.Runtime.CompilerServices;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Selects the class-level attributes of an endpoint that belong in its route metadata.
/// </summary>
/// <remarks>
/// Called from both registration paths — <see cref="EndpointRouteBuilderExtensions.MapEndpoint{T}"/>
/// and the source-generated mapping — so the rule lives in one place.
/// <para>
/// Plain <c>GetCustomAttributes(true)</c> also hands back the attributes the <i>compiler</i> put on
/// the type. A nullable-enabled endpoint class carries <c>NullableAttribute</c> and
/// <c>NullableContextAttribute</c>, which then show up in <c>endpoint.Metadata</c> and confuse
/// anything that enumerates or filters it. Those are an artifact of how the class was compiled,
/// never a routing convention.
/// </para>
/// </remarks>
public static class EndpointAttributes
{
    /// <summary>The attributes of <paramref name="endpointType"/> to add as endpoint metadata.</summary>
    public static object[] ForMetadata(Type endpointType)
    {
        ArgumentNullException.ThrowIfNull(endpointType);

        return [.. endpointType.GetCustomAttributes(inherit: true).Where(IsMeaningful)];
    }

    private static bool IsMeaningful(object attribute)
    {
        var type = attribute.GetType();

        return type.Namespace != "System.Runtime.CompilerServices"
            && type.GetCustomAttribute<CompilerGeneratedAttribute>(inherit: false) is null;
    }
}
