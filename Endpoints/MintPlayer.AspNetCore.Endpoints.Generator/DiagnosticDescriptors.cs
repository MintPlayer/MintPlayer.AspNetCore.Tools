using Microsoft.CodeAnalysis;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor EndpointMustBePartial = new(
        id: "MPEP001",
        title: "Endpoint class must be partial",
        messageFormat: "Endpoint class '{0}' implements a typed endpoint interface and must be declared as partial",
        category: "MintPlayer.Endpoints",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EndpointHasBaseClassConflict = new(
        id: "MPEP002",
        title: "Endpoint class has conflicting base class",
        messageFormat: "Endpoint class '{0}' already has a base class; the source generator cannot add the required endpoint base class",
        category: "MintPlayer.Endpoints",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EndpointHasMultipleGroups = new(
        id: "MPEP003",
        title: "Endpoint belongs to multiple groups",
        messageFormat: "Endpoint class '{0}' implements IMemberOf<T> for multiple groups; only one group is allowed",
        category: "MintPlayer.Endpoints",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
