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

    // Route diagnostics (PRD R3.3, milestone M6). Every one of them is opportunistic: when the
    // route or prefix it needs could not be recovered at compile time it stays silent, because an
    // unreadable Path is legitimate (a computed value, an endpoint from a referenced assembly) and
    // hard-failing on it would break cross-assembly endpoints. None of them suppresses emission, so
    // none can leave a cascading CS error behind (R3.4).

    public static readonly DiagnosticDescriptor DuplicateRoute = new(
        id: "MPEP007",
        title: "Two endpoints answer the same verb and route",
        messageFormat: "Endpoint '{0}' answers {1} on '{2}', which endpoint '{3}' already answers; every such request fails at run time with an ambiguous-match 500",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ASP0022 cannot see these routes, because the generated call site passes TEndpoint.Path rather than a literal. Routes are compared the way the matcher compares them: case-insensitively, ignoring a trailing slash and parameter names, but keeping constraints. A literal segment beating a parameter (/users/me beside /users/{id}) is correct behaviour and is not reported. A Warning rather than an Error for now (R3.6): a wrong model of matcher precedence must not block a build.");

    public static readonly DiagnosticDescriptor RouteTokenNotBound = new(
        id: "MPEP008",
        title: "Route parameter is not bound to any property",
        messageFormat: "Route parameter '{{{0}}}' in the Path of endpoint '{1}' is not bound to any property; add [RouteParam] to a property named '{0}' (or [RouteParam(\"{0}\")] on another), otherwise the handler cannot read it{2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A typed endpoint's handler has no other way to reach a route value, so an unbound token is almost always a typo between the template and the property. It is a Warning because a token can exist purely for matching (/v{version}/...). Raw endpoints are not checked: they may read RouteValues by hand.");

    public static readonly DiagnosticDescriptor BoundPropertyNotInRoute = new(
        id: "MPEP009",
        title: "Route-bound property matches no route parameter",
        messageFormat: "Property '{0}' is bound to route parameter '{1}', but the route '{2}' of endpoint '{3}' has no such parameter{4}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The route value can never be present, so binding fails and the endpoint answers 400 on every request, forever — while compiling cleanly. The route checked is the composed one, so a parameter contributed by a group prefix counts.");

    public static readonly DiagnosticDescriptor PathRepeatsGroupPrefix = new(
        id: "MPEP010",
        title: "Path repeats its group's prefix",
        messageFormat: "The Path '{0}' of endpoint '{1}' already begins with its group prefix '{2}', so the endpoint is mapped at '{3}'; a Path is relative to its group",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A group-relative path written absolutely maps the endpoint at the prefix twice over. Every request to the intended URL then 404s, and nothing at build or start-up says why.");

    public static readonly DiagnosticDescriptor PathNotConstant = new(
        id: "MPEP011",
        title: "Path is not a compile-time constant",
        messageFormat: "The Path of endpoint '{0}' is not a compile-time constant, so its route checks (MPEP007-MPEP010) are skipped",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Nothing is wrong with a computed Path, which is why this is Info. It exists so that the absence of a duplicate-route or route-parameter diagnostic is not mistaken for proof that there is nothing to report.");

    /// <remarks>
    /// An Error because the alternative is a crash the build cannot see: <c>WithName</c> duplicates
    /// throw <c>InvalidOperationException: Duplicate endpoint name</c> on the <b>first request</b>,
    /// not at startup (measured; the ASP.NET Core documentation says otherwise). Only the generator
    /// sees every endpoint at once.
    /// </remarks>
    public static readonly DiagnosticDescriptor DuplicateEndpointName = new(
        id: "MPEP012",
        title: "Two endpoints have the same endpoint name",
        messageFormat: "Endpoint '{0}' has the endpoint name '{1}', which '{2}' already has; endpoint names must be unique. Give one of them a distinct name with [EndpointDescriptorName(\"...\")].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The endpoint name is the ASP.NET Core route name, the OpenAPI operationId and the typed-link method name, so it must be unique across the assembly. It is the class name unless [EndpointDescriptorName] overrides it, so two endpoints with the same class name in different namespaces collide. The later endpoint is mapped without a name and gets no typed link.");

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

    /// <remarks>
    /// Exactly the shape that silently returns 200 on invalid input (PRD R5.3). The library validates
    /// a body only through the resolver <c>Microsoft.Extensions.Validation</c> generates for types
    /// marked <c>[ValidatableType]</c> in hand-written code; the framework's own discovery can never
    /// reach the generated <c>Map</c> calls (P9.1), and .NET 10 says nothing about it. .NET 11's
    /// ASP0038 covers only a marked type with no <c>AddValidation()</c>, so the two never overlap.
    /// Attributes on a positional record parameter count: the validation generator honours them
    /// (measured on net10.0 and net11.0), so the fix is the same for both forms.
    /// </remarks>
    public static readonly DiagnosticDescriptor RequestNotValidatable = new(
        id: "MPEP015",
        title: "Request type has validation rules that will never run",
        messageFormat: "Request type '{0}' of endpoint '{1}' {2} but is not marked [ValidatableType], so its validation never runs and invalid requests reach HandleAsync; add [Microsoft.Extensions.Validation.ValidatableType] to '{0}' and call builder.Services.AddValidation() in the project that declares it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Microsoft.Extensions.Validation discovers types from hand-written Map calls, which a generated mapping is not, so an unmarked request type is never validated and nothing reports it. The generator cannot add the attribute itself: generators do not see each other's output, and .NET 11 forbids it in generated code (ASP0037).");

    // MPEP017 (generated and hand-written binder both present) stays reserved for its milestone in
    // PRD R3.3.

    public static readonly DiagnosticDescriptor GroupNeverJoined = new(
        id: "MPEP016",
        title: "Endpoint group is never joined",
        messageFormat: "Endpoint group '{0}' is not joined by any endpoint, directly or through a nested group, so it is not mapped",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A group nobody joins produces no MapGroup call, so its Prefix and Configure never take effect. Usually that is a forgotten [MemberOf<T>]; it is Info because a library may legitimately declare groups for its consumers to join.");

    public static readonly DiagnosticDescriptor ResponseTypeLooksLikeRequest = new(
        id: "MPEP018",
        title: "Single type argument looks like a request type",
        messageFormat: "Endpoint '{0}' uses '{1}' as the single type argument of {2}<T>, which is the RESPONSE type; if '{1}' is meant to be the request, use {2}<{1}, TResponse>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A body-less verb's arity-1 form (IGetEndpoint<T>, IDeleteEndpoint<T>) takes the response, not the request. A type named *Request, *Body or *Command in that position usually means the two-argument form was intended; the endpoint then compiles, binds nothing, and documents the request as its response.");

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

    /// <remarks>
    /// Reported on an endpoint and on a group. Without it the endpoint was mapped anyway and the
    /// generated mapping, typed-link and contract files failed with CS0122 — errors in files the
    /// consumer cannot edit, with nothing saying which declaration to change.
    /// </remarks>
    public static readonly DiagnosticDescriptor TypeNotAccessibleToGeneratedCode = new(
        id: "MPEP024",
        title: "Endpoint or group is not accessible to generated code",
        messageFormat: "{0} '{1}' cannot be mapped because {2}; the generated mapping code must be able to name it, so it and every type it is nested in must be declared at least 'internal'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated Map...Endpoints() method, the typed links and the endpoint contract live in their own types, so they reach an endpoint or group only by its name. A private, protected or private protected nested type, or a type nested inside one, is inaccessible there (CS0122), and a file-local type cannot be named from another file at all. The endpoint (or the group, with everything that joins it) is left out of the mapping, the typed links and the contract.");

    // MPEP021-MPEP023 are reported by EndpointClientGenerator, in the CLIENT project, and have no
    // source location: what they describe lives in a referenced assembly's metadata.

    public static readonly DiagnosticDescriptor ClientContractSkipped = new(
        id: "MPEP021",
        title: "Endpoint contract cannot become a client method",
        messageFormat: "No client method is generated for endpoint '{0}' of '{1}': {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The typed client leaves an endpoint out rather than generating a call that does not compile or cannot work: a type in its contract cannot be resolved or is not public in the client, or the contract was written by a newer generator than the client's.");

    public static readonly DiagnosticDescriptor ClientUsesServerAssemblyType = new(
        id: "MPEP022",
        title: "Client method uses a type declared in the server assembly",
        messageFormat: "Client method '{0}' uses '{1}', which is declared in the server assembly '{2}' itself; a metadata-only reference does not deploy that assembly, so the call fails at run time unless the client ships it too. Move the type to a contracts assembly both projects reference.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The recommended client shape references the server assembly for its contracts only (Private=false), so the server assembly is never copied next to the client. A request or response type declared there compiles in the client and throws FileNotFoundException the first time the method runs.");

    public static readonly DiagnosticDescriptor ClientPrerequisiteMissing = new(
        id: "MPEP023",
        title: "Typed client cannot be generated in this project",
        messageFormat: "GenerateEndpointsClient is set, but the typed client needs '{0}', which this project cannot resolve; no client is generated",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The generated client is built on HttpClient, System.Net.Http.Json and System.Text.Encodings.Web, all in the .NET shared framework from .NET 5 on. A netstandard2.0 or .NET Framework project needs the corresponding packages.");

    // MPEP025-MPEP032: open-generic endpoints (issue #34). MPEP025 and MPEP032 are reported on the
    // declaration; MPEP026-MPEP031 on the application's [assembly: EndpointTypeArgument] attribute,
    // since that is what closes the endpoint and what the application can change.

    public static readonly DiagnosticDescriptor OpenEndpointNotMapped = new(
        id: "MPEP025",
        title: "Open-generic endpoint is not mapped by this assembly",
        messageFormat: "Endpoint class '{0}' has type parameters, so this assembly's generated mapping does not map it; an application closes it with [assembly: EndpointTypeArgument<TConstraint, TArgument>] (or [assembly: EndpointTypeArgument(typeof(...), ...)]) and maps it from its own generated method",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Generated code outside the class cannot name its type parameters, so it gets no mapping, typed link or contract here. Its generated partial is still emitted, with the type parameters repeated, and it is recorded in this assembly's metadata. The application that knows the type arguments closes it: every type parameter constrained to TConstraint becomes TArgument, and the closed endpoint is mapped like any other, named '{Name}_{TypeArguments}'.");

    public static readonly DiagnosticDescriptor TypeArgumentViolatesConstraint = new(
        id: "MPEP026",
        title: "Type argument does not satisfy a constraint of the endpoint",
        messageFormat: "Type argument '{0}' does not satisfy the '{1}' constraint of type parameter '{2}' of endpoint '{3}'; the endpoint is not closed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The attribute's own constraint guarantees TArgument : TConstraint, but an endpoint's type parameter can carry more constraints (new(), class, struct, unmanaged, notnull, further constraint types). Closing it with an argument that violates one would emit code that does not compile, so nothing is emitted for that endpoint.");

    public static readonly DiagnosticDescriptor TypeParameterBoundTwice = new(
        id: "MPEP027",
        title: "Type parameter is bound by more than one EndpointTypeArgument",
        messageFormat: "Type parameter '{0}' of endpoint '{1}' is bound by both {2} and {3}; the endpoint is not closed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A constraint-keyed attribute binds every type parameter that has TConstraint among its constraint types. When two attributes match one parameter there is no rule to choose between them. Remove one, or close the endpoint explicitly with [assembly: EndpointTypeArgument(typeof(...), ...)], which wins over constraint keys for that endpoint.");

    public static readonly DiagnosticDescriptor ExplicitTypeArgumentCountMismatch = new(
        id: "MPEP028",
        title: "Explicit EndpointTypeArgument has the wrong number of type arguments",
        messageFormat: "EndpointTypeArgument(typeof({0}), ...) gives {1} type argument(s), but '{0}' has {2} type parameter(s), counting those of the types it is nested in; the endpoint is not closed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The explicit form lists one type argument per type parameter, outermost containing type first.");

    public static readonly DiagnosticDescriptor ClosedEndpointNotAccessible = new(
        id: "MPEP029",
        title: "Open endpoint cannot be closed by this application",
        messageFormat: "Endpoint '{0}' is not closed because {1}; this application's generated code must be able to name it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An endpoint declared in a referenced assembly, and every group on its chain, must be public for the application's generated mapping to name it; a type argument must be accessible from the application's generated code.");

    public static readonly DiagnosticDescriptor TypeArgumentClosesNothing = new(
        id: "MPEP030",
        title: "EndpointTypeArgument closes no endpoint",
        messageFormat: "{0} closes no endpoint: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Open endpoints are found in this compilation and in referenced assemblies built with a generator that records them (11.2 or later). An attribute that matches none of them is most likely keyed on the wrong type, or names a library that is not referenced.");

    public static readonly DiagnosticDescriptor EndpointPartiallyBound = new(
        id: "MPEP031",
        title: "Open endpoint is only partly closed",
        messageFormat: "Endpoint '{0}' is not closed: type parameter(s) {1} are not bound by any EndpointTypeArgument; add one keyed on a constraint type of theirs, or close the endpoint with [assembly: EndpointTypeArgument(typeof(...), ...)]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An endpoint is closed only when every type parameter, including those of the types it is nested in, is bound.");

    public static readonly DiagnosticDescriptor NewStaticPathIgnored = new(
        id: "MPEP032",
        title: "A 'new static Path' is not the route the endpoint answers on",
        messageFormat: "Endpoint class '{0}' hides the inherited Path with 'new static', but the runtime maps it at '{1}', the Path of the base class that implements the endpoint interface; list the endpoint interface on '{0}' again to use the new Path, or remove it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "TEndpoint.Path dispatches through the interface map, and a static member hidden with 'new' does not re-implement the interface. The generated links and contract use the route the endpoint really answers on.");
}
