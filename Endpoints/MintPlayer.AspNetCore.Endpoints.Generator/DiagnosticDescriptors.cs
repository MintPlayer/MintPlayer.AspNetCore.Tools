using Microsoft.CodeAnalysis;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

internal static class DiagnosticDescriptors
{
    private const string Category = "MintPlayer.Endpoints";

    public static readonly DiagnosticDescriptor EndpointMustBePartial = new(
        id: "MPEP001",
        title: "Endpoint class must be partial",
        messageFormat: "Endpoint class '{0}' implements a typed endpoint interface and must be declared as partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A typed endpoint needs the generated base class that carries request binding and the HttpContext bridge, which can only be added to a partial class.");

    public static readonly DiagnosticDescriptor EndpointHasBaseClassConflict = new(
        id: "MPEP002",
        title: "Endpoint class has conflicting base class",
        messageFormat: "Endpoint class '{0}' already has a base class; the source generator cannot add the required endpoint base class",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Derive the base class itself from the matching endpoint base (PostEndpoint<T>, GetEndpoint<T>, …) so the chain ends where the generator would have put it.");

    public static readonly DiagnosticDescriptor EndpointHasMultipleGroups = new(
        id: "MPEP003",
        title: "Endpoint belongs to multiple groups",
        messageFormat: "Endpoint class '{0}' implements IMemberOf<T> for multiple groups; only one group is allowed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An endpoint's route is its group's prefix plus its own path, so two groups mean two routes and no way to choose.");

    // MPEP003's message names an *endpoint* class and an endpoint's single route; a group with two
    // parents is a different shape with a different consequence (every endpoint beneath it moves),
    // and a consumer filtering warnings needs to be able to tell them apart. Hence its own id
    // rather than reusing MPEP003 with a vaguer message.
    public static readonly DiagnosticDescriptor GroupHasMultipleParents = new(
        id: "MPEP004",
        title: "Endpoint group belongs to multiple parent groups",
        messageFormat: "Endpoint group '{0}' implements IMemberOf<T> for multiple parent groups; only one parent is allowed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A group's prefix is its parent's prefix plus its own, so two parents mean every endpoint in the group has two routes.");

    public static readonly DiagnosticDescriptor GroupNestingIsCyclic = new(
        id: "MPEP005",
        title: "Endpoint group nesting is cyclic",
        messageFormat: "Endpoint group '{0}' is nested inside itself through IMemberOf<T>; the group and its endpoints cannot be mapped",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A cycle has no outermost group, so there is no prefix to compose. Without this diagnostic the groups and every endpoint in them are silently dropped.");

    public static readonly DiagnosticDescriptor MappingMethodNameWasSanitised = new(
        id: "MPEP006",
        title: "Generated mapping method name was adjusted",
        messageFormat: "The endpoint mapping method was named '{0}' because '{1}' is not a valid C# identifier",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The method name is derived from the assembly name, or from [assembly: EndpointsMethodName]. Characters that are legal in an assembly name but not in an identifier are dropped; without this warning the adjusted name is the only clue that anything happened.");
}
