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

    // MPEP003 (endpoint in two groups) and MPEP004 (group with two parents) are retired, and their
    // ids are deliberately NOT reused: a shipped id must never change meaning. Both shapes are now
    // CS0579 - [MemberOf<T>] is AllowMultiple = false, which the compiler enforces even across
    // partial declarations. The generator still runs on that invalid compilation and takes the
    // first attribute, but the build fails on CS0579 either way, so a diagnostic of ours would only
    // repeat it.

    public static readonly DiagnosticDescriptor GroupNestingIsCyclic = new(
        id: "MPEP005",
        title: "Endpoint group nesting is cyclic",
        messageFormat: "Endpoint group '{0}' is nested inside itself through [MemberOf<T>]; the group and its endpoints cannot be mapped",
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

    // MPEP007-MPEP012 and MPEP015-MPEP018 are reserved for the route and validation diagnostics
    // in PRD R3.3; they are allocated there so the ids stay stable while they land.

    public static readonly DiagnosticDescriptor BoundPropertyTypeUnsupported = new(
        id: "MPEP013",
        title: "Bound property type cannot be converted",
        messageFormat: "Property '{0}' is bound from the {1} but its type '{2}' is not string, an enum, or a type implementing IParsable<{2}>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "There is no safe fallback conversion: Convert.ChangeType and TypeDescriptor are reflective, culture-sensitive and not trimming-safe. Implement IParsable<TSelf> on the type, which is also what ASP.NET Core's own binding asks for.");

    public static readonly DiagnosticDescriptor BoundPropertiesNeedPartial = new(
        id: "MPEP014",
        title: "Endpoint with bound properties must be partial",
        messageFormat: "Endpoint class '{0}' has [RouteParam]/[QueryParam] properties and must be declared as partial so they can be bound",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The binding code is generated into a partial declaration of the endpoint. Without 'partial' the properties would silently keep their default values on every request.");

    public static readonly DiagnosticDescriptor ContainingTypeNotPartial = new(
        id: "MPEP019",
        title: "Endpoint is nested inside a type that is not partial",
        messageFormat: "Endpoint class '{0}' is nested inside '{1}', which must also be declared as partial for the endpoint's generated code to reach it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Without this the compiler reports CS0260 against the containing type, citing a partial declaration the consumer never wrote and saying nothing about endpoints.");

    public static readonly DiagnosticDescriptor BoundPropertyNotSettable = new(
        id: "MPEP020",
        title: "Bound property cannot be assigned",
        messageFormat: "Property '{0}' is bound from the {1} but has no setter the endpoint's generated code can use; give it a 'set' accessor (not 'init', and not 'private' on a base class)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Binding assigns the property after construction, which an init-only or get-only property forbids. Without this diagnostic the property would silently never be bound.");
}
