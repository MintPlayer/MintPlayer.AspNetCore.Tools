using System.Collections.Immutable;
using System.Text;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>How much the endpoint declares, which decides its generated base class.</summary>
/// <remarks>
/// <see cref="ResponseOnly"/> is <c>IGetEndpoint&lt;TResponse&gt;</c>/<c>IDeleteEndpoint&lt;TResponse&gt;</c>:
/// a typed response and no request body. It is recognised from <c>IResponseEndpoint&lt;T&gt;</c>
/// rather than from arity, because its single type argument is the <i>response</i> — reading it by
/// arity would take the response type for a request and try to bind a body into it.
/// </remarks>
internal enum EndpointLevel { Raw, Typed, TypedWithResponse, ResponseOnly }
internal enum HttpMethodKind { Custom, Get, Post, Put, Delete, Patch }

internal sealed class EndpointInfo : IEquatable<EndpointInfo>
{
    public EndpointInfo(string fqn, string ns, string className, bool isPartial, bool hasExistingBaseClass,
        EndpointLevel level, HttpMethodKind httpMethod,
        string? requestTypeFqn, string? responseTypeFqn,
        string? groupTypeFqn,
        bool baseChainReachesEndpointBase = false,
        string? descriptorName = null, LocationKey? location = null,
        PathSpec? pathSpec = null, string? route = null,
        ImmutableArray<BoundProperty> boundProperties = default,
        string? knownMethods = null,
        RequestValidationGap validationGap = RequestValidationGap.None,
        string? inaccessibleReason = null,
        bool isInFileLocalType = false,
        OpenGenericInfo? open = null,
        ClosedGenericInfo? closed = null,
        ImmutableArray<GroupInfo> referencedGroups = default,
        bool hasIgnoredNewPath = false,
        bool hasIgnoredNewMethods = false)
    {
        Open = open;
        Closed = closed;
        ReferencedGroups = referencedGroups.IsDefault ? ImmutableArray<GroupInfo>.Empty : referencedGroups;
        HasIgnoredNewPath = hasIgnoredNewPath;
        HasIgnoredNewMethods = hasIgnoredNewMethods;
        InaccessibleReason = inaccessibleReason;
        IsInFileLocalType = isInFileLocalType;
        ValidationGap = validationGap;
        KnownMethods = knownMethods;
        BoundProperties = boundProperties.IsDefault ? ImmutableArray<BoundProperty>.Empty : boundProperties;
        PathSpec = pathSpec;
        Route = route;
        FullyQualifiedName = fqn;
        Namespace = ns;
        ClassName = className;
        IsPartial = isPartial;
        HasExistingBaseClass = hasExistingBaseClass;
        Level = level;
        HttpMethod = httpMethod;
        RequestTypeFqn = requestTypeFqn;
        ResponseTypeFqn = responseTypeFqn;
        GroupTypeFqn = groupTypeFqn;
        BaseChainReachesEndpointBase = baseChainReachesEndpointBase;
        DescriptorName = descriptorName;
        Location = location;
    }

    public string FullyQualifiedName { get; }
    public string Namespace { get; }
    public string ClassName { get; }
    public bool IsPartial { get; }
    public bool HasExistingBaseClass { get; }
    public EndpointLevel Level { get; }
    public HttpMethodKind HttpMethod { get; }
    public string? RequestTypeFqn { get; }
    public string? ResponseTypeFqn { get; }
    public string? GroupTypeFqn { get; }

    /// <summary>
    /// The chain of types this endpoint is nested inside, or null when it sits directly in its
    /// namespace.
    /// </summary>
    /// <remarks>
    /// Emitting a nested endpoint's partial into a flat <c>namespace { }</c> block produces code
    /// that does not compile, because the containing types are never reopened. Nothing in the
    /// fixture corpus was nested, which is why a 988-test suite did not catch it.
    /// <para>
    /// <see cref="MintPlayer.SourceGenerators.Tools.PathSpec.AllPartial"/> also answers the
    /// follow-up question the flat form could not even ask: whether every containing type is
    /// <c>partial</c>. If one is not, the endpoint cannot be extended from a generated file at
    /// all, and that is a diagnostic rather than a silent miscompile.
    /// </para>
    /// </remarks>
    public PathSpec? PathSpec { get; }

    /// <summary>
    /// The group-relative route this endpoint declares, recovered at compile time, or
    /// <see langword="null"/> when it could not be.
    /// </summary>
    /// <remarks>
    /// <b>Null means "unknown", never "empty".</b> It is unrecoverable for a non-constant
    /// expression and for any endpoint in a referenced assembly, both of which are legitimate, so
    /// every check built on this must stay silent rather than guess. See <see cref="RouteLiteral"/>.
    /// </remarks>
    public string? Route { get; }

    /// <summary>
    /// The endpoint's <c>[RouteParam]</c>/<c>[QueryParam]</c> properties, own and inherited, nearest
    /// declaration first and de-duplicated by name.
    /// </summary>
    public ImmutableArray<BoundProperty> BoundProperties { get; }

    /// <summary>
    /// The verbs this endpoint answers, encoded by <see cref="MethodsLiteral"/>, or
    /// <see langword="null"/> when they are not known at compile time.
    /// </summary>
    /// <remarks>
    /// Null is "unknown", and MPEP007 treats unknown as "no conflict" — never as a conflict.
    /// </remarks>
    public string? KnownMethods { get; }

    /// <summary>
    /// Whether the request type has validation rules but no <c>[ValidatableType]</c> — the MPEP015
    /// shape. Always <see cref="RequestValidationGap.None"/> for levels without a request body.
    /// </summary>
    public RequestValidationGap ValidationGap { get; }

    /// <summary>
    /// Why generated code outside the endpoint cannot name it (MPEP024), or <see langword="null"/>
    /// when it can. Such an endpoint is not mapped, linked or contracted.
    /// </summary>
    /// <remarks>See <see cref="GeneratedCodeAccess"/>.</remarks>
    public string? InaccessibleReason { get; }

    /// <summary>
    /// True when the endpoint or a type it is nested in is a <c>file</c> type, so no generated
    /// partial can join it.
    /// </summary>
    public bool IsInFileLocalType { get; }

    /// <summary>
    /// True when the user's own base class already derives from one of the library's endpoint bases.
    /// </summary>
    /// <remarks>
    /// This is what separates "the generator has nothing to add" from MPEP002. Both suppress
    /// emission; only the second is a mistake.
    /// </remarks>
    public bool BaseChainReachesEndpointBase { get; }

    /// <summary>The name from [EndpointDescriptorName], or null to fall back to the class name.</summary>
    public string? DescriptorName { get; }

    /// <summary>
    /// Where to point a diagnostic, as a value-equal key rather than a <c>Location</c>.
    /// </summary>
    /// <remarks>
    /// A <c>Location</c> holds a reference to its <c>SyntaxTree</c>, so storing one would make every
    /// model compare unequal after any edit to the file and defeat the incremental cache. The key is
    /// turned back into a <c>Location</c> against the current compilation when the diagnostic is
    /// reported.
    /// </remarks>
    public LocationKey? Location { get; }

    /// <summary>
    /// Set when the endpoint, or a type it is nested in, has type parameters (issue #34). Generated
    /// code outside the class cannot name it, so it is not mapped, linked or contracted in this
    /// assembly; an application closes it (PRD D6).
    /// </summary>
    public OpenGenericInfo? Open { get; }

    /// <summary>
    /// Set when this is a closed construction of an open endpoint, produced by the application's
    /// <c>[assembly: EndpointTypeArgument]</c> (PRD D4). It is mapped like any endpoint, but it has
    /// no declaration here: no partial is emitted and no per-declaration diagnostic runs on it.
    /// </summary>
    public ClosedGenericInfo? Closed { get; }

    /// <summary>
    /// The constructed generic groups on this endpoint's chain (<c>[MemberOf&lt;Api&lt;string&gt;&gt;]</c>),
    /// which no group declaration describes: the declaration is the open <c>Api&lt;T&gt;</c> (PRD D7).
    /// </summary>
    public ImmutableArray<GroupInfo> ReferencedGroups { get; }

    /// <summary>
    /// True when a <c>new static Path</c> on the class (or a base between it and the interface
    /// implementation) is not the <c>Path</c> the runtime uses, because the endpoint interface is not
    /// re-implemented below it (PRD D7, MPEP032). <see cref="Route"/> is the runtime's.
    /// </summary>
    public bool HasIgnoredNewPath { get; }

    /// <summary>
    /// The same as <see cref="HasIgnoredNewPath"/>, for <c>Methods</c> (MPEP032).
    /// <see cref="KnownMethods"/> is the runtime's.
    /// </summary>
    public bool HasIgnoredNewMethods { get; }

    /// <summary>The name this endpoint is recorded under in the descriptor list.</summary>
    public string EffectiveDescriptorName => DescriptorName ?? ClassName;

    public string? GetBaseClassName()
    {
        if (Level == EndpointLevel.Raw) return null;
        if (Level == EndpointLevel.ResponseOnly) return "global::MintPlayer.AspNetCore.Endpoints.ResponseEndpoint";
        var name = HttpMethod switch
        {
            HttpMethodKind.Post => "PostEndpoint",
            HttpMethodKind.Put => "PutEndpoint",
            HttpMethodKind.Patch => "PatchEndpoint",
            HttpMethodKind.Get => "GetEndpoint",
            HttpMethodKind.Delete => "DeleteEndpoint",
            _ => "EndpointBase"
        };
        return $"global::MintPlayer.AspNetCore.Endpoints.{name}<{RequestTypeFqn}>";
    }

    public bool Equals(EndpointInfo? other) =>
        other is not null &&
        FullyQualifiedName == other.FullyQualifiedName &&
        Namespace == other.Namespace &&
        ClassName == other.ClassName &&
        IsPartial == other.IsPartial &&
        HasExistingBaseClass == other.HasExistingBaseClass &&
        Level == other.Level &&
        HttpMethod == other.HttpMethod &&
        RequestTypeFqn == other.RequestTypeFqn &&
        ResponseTypeFqn == other.ResponseTypeFqn &&
        GroupTypeFqn == other.GroupTypeFqn &&
        BaseChainReachesEndpointBase == other.BaseChainReachesEndpointBase &&
        DescriptorName == other.DescriptorName &&
        LocationKeys.AreEqual(Location, other.Location) &&
        PathSpecs.AreEqual(PathSpec, other.PathSpec) &&
        Route == other.Route &&
        KnownMethods == other.KnownMethods &&
        ValidationGap == other.ValidationGap &&
        InaccessibleReason == other.InaccessibleReason &&
        IsInFileLocalType == other.IsInFileLocalType &&
        HasIgnoredNewPath == other.HasIgnoredNewPath &&
        HasIgnoredNewMethods == other.HasIgnoredNewMethods &&
        Equals(Open, other.Open) &&
        Equals(Closed, other.Closed) &&
        SequenceComparer<GroupInfo>.Instance.Equals(ReferencedGroups, other.ReferencedGroups) &&
        // ImmutableArray's own equality compares the backing array by reference. Using it here
        // would make every run look like a change and kill incremental caching, silently.
        SequenceComparer<BoundProperty>.Instance.Equals(BoundProperties, other.BoundProperties);

    public override bool Equals(object? obj) => Equals(obj as EndpointInfo);
    public override int GetHashCode() => FullyQualifiedName?.GetHashCode() ?? 0;
}

/// <summary>What the declaring assembly knows about an open-generic endpoint (issue #34, PRD D6).</summary>
internal sealed class OpenGenericInfo : IEquatable<OpenGenericInfo>
{
    public OpenGenericInfo(string displayName, string metadataName, string unboundTypeOf, string? ownTypeParameters)
    {
        DisplayName = displayName;
        MetadataName = metadataName;
        UnboundTypeOf = unboundTypeOf;
        OwnTypeParameters = ownTypeParameters;
    }

    /// <summary>For messages: <c>Echo&lt;TPayload&gt;</c>, <c>Outer&lt;T&gt;.Inner</c>.</summary>
    public string DisplayName { get; }

    /// <summary>The metadata name the closing step re-resolves the symbol by: <c>Ns.Outer`1+Inner</c>.</summary>
    public string MetadataName { get; }

    /// <summary>The <c>typeof</c> operand of the unbound type: <c>global::Ns.Outer&lt;&gt;.Inner</c>.</summary>
    public string UnboundTypeOf { get; }

    /// <summary>The class's own type parameter list, <c>&lt;T&gt;</c>, repeated on its generated partial (PRD R3); null when only a container is generic.</summary>
    public string? OwnTypeParameters { get; }

    public bool Equals(OpenGenericInfo? other) =>
        other is not null &&
        DisplayName == other.DisplayName &&
        MetadataName == other.MetadataName &&
        UnboundTypeOf == other.UnboundTypeOf &&
        OwnTypeParameters == other.OwnTypeParameters;

    public override bool Equals(object? obj) => Equals(obj as OpenGenericInfo);
    public override int GetHashCode() => MetadataName.GetHashCode();
}

/// <summary>Where a closed construction came from (issue #34, PRD D4).</summary>
internal sealed class ClosedGenericInfo : IEquatable<ClosedGenericInfo>
{
    public ClosedGenericInfo(string openFullyQualifiedName, bool fromReference)
    {
        OpenFullyQualifiedName = openFullyQualifiedName;
        FromReference = fromReference;
    }

    /// <summary>The open definition's fully qualified name, <c>global::Lib.Passkeys&lt;TUser&gt;</c>.</summary>
    public string OpenFullyQualifiedName { get; }

    /// <summary>True when the open endpoint is declared in a referenced assembly rather than in this compilation.</summary>
    public bool FromReference { get; }

    public bool Equals(ClosedGenericInfo? other) =>
        other is not null &&
        OpenFullyQualifiedName == other.OpenFullyQualifiedName &&
        FromReference == other.FromReference;

    public override bool Equals(object? obj) => Equals(obj as ClosedGenericInfo);
    public override int GetHashCode() => OpenFullyQualifiedName.GetHashCode();
}

/// <summary>A diagnostic the closing step found, as value-equal strings (see <see cref="ClosingModel"/>).</summary>
internal sealed class ClosingProblem : IEquatable<ClosingProblem>
{
    public ClosingProblem(string id, ImmutableArray<string> arguments, LocationKey? location)
    {
        Id = id;
        Arguments = arguments;
        Location = location;
    }

    public string Id { get; }
    public ImmutableArray<string> Arguments { get; }
    public LocationKey? Location { get; }

    public bool Equals(ClosingProblem? other) =>
        other is not null &&
        Id == other.Id &&
        SequenceComparer<string>.Instance.Equals(Arguments, other.Arguments) &&
        LocationKeys.AreEqual(Location, other.Location);

    public override bool Equals(object? obj) => Equals(obj as ClosingProblem);
    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// What the application's <c>[assembly: EndpointTypeArgument]</c> attributes produce: the closed
/// endpoints, the groups on their chains that no declaration of this compilation describes, and the
/// problems (PRD D2, D6). Value-equal, because it is computed from the <c>Compilation</c> on every run
/// and only equality keeps everything downstream cached.
/// </summary>
internal sealed class ClosingModel : IEquatable<ClosingModel>
{
    public static readonly ClosingModel Empty = new(
        ImmutableArray<EndpointInfo>.Empty, ImmutableArray<GroupInfo>.Empty, ImmutableArray<ClosingProblem>.Empty);

    public ClosingModel(ImmutableArray<EndpointInfo> endpoints, ImmutableArray<GroupInfo> groups, ImmutableArray<ClosingProblem> problems)
    {
        Endpoints = endpoints;
        Groups = groups;
        Problems = problems;
    }

    public ImmutableArray<EndpointInfo> Endpoints { get; }
    public ImmutableArray<GroupInfo> Groups { get; }
    public ImmutableArray<ClosingProblem> Problems { get; }

    public bool Equals(ClosingModel? other) =>
        other is not null &&
        SequenceComparer<EndpointInfo>.Instance.Equals(Endpoints, other.Endpoints) &&
        SequenceComparer<GroupInfo>.Instance.Equals(Groups, other.Groups) &&
        SequenceComparer<ClosingProblem>.Instance.Equals(Problems, other.Problems);

    public override bool Equals(object? obj) => Equals(obj as ClosingModel);
    public override int GetHashCode() => unchecked((Endpoints.Length * 31) ^ Groups.Length ^ (Problems.Length * 7));
}

internal sealed class GroupInfo : IEquatable<GroupInfo>
{
    public GroupInfo(string fullyQualifiedName, string? parentGroupFqn,
        LocationKey? location = null, string? prefix = null, string? inaccessibleReason = null,
        bool isOpen = false, string? unboundTypeOf = null)
    {
        IsOpen = isOpen;
        UnboundTypeOf = unboundTypeOf;
        InaccessibleReason = inaccessibleReason;
        FullyQualifiedName = fullyQualifiedName;
        ParentGroupFqn = parentGroupFqn;
        Location = location;
        Prefix = prefix;
    }

    public string FullyQualifiedName { get; }
    public string? ParentGroupFqn { get; }

    /// <inheritdoc cref="EndpointInfo.Route"/>
    public string? Prefix { get; }

    /// <inheritdoc cref="EndpointInfo.Location"/>
    public LocationKey? Location { get; }

    /// <summary>
    /// Why generated code cannot name the group (MPEP024), or <see langword="null"/> when it can.
    /// Such a group is not mapped, and neither is anything that joins it, directly or through a
    /// nested group.
    /// </summary>
    public string? InaccessibleReason { get; }

    /// <summary>
    /// True for a group declaration with type parameters (its own or a container's). Generated code
    /// cannot name it; the constructions endpoints actually join are described instead (PRD D7).
    /// </summary>
    public bool IsOpen { get; }

    /// <summary>
    /// The <c>typeof</c> operand of the group's unbound declaration (<c>global::Ns.Api&lt;&gt;</c>), for a
    /// group declared in this compilation; recorded for applications that close this assembly's open
    /// endpoints (PRD D3a). Null for a group that is not a declaration of this compilation.
    /// </summary>
    public string? UnboundTypeOf { get; }

    public bool Equals(GroupInfo? other) =>
        other is not null &&
        FullyQualifiedName == other.FullyQualifiedName &&
        ParentGroupFqn == other.ParentGroupFqn &&
        LocationKeys.AreEqual(Location, other.Location) &&
        Prefix == other.Prefix &&
        InaccessibleReason == other.InaccessibleReason &&
        IsOpen == other.IsOpen &&
        UnboundTypeOf == other.UnboundTypeOf;

    public override bool Equals(object? obj) => Equals(obj as GroupInfo);
    public override int GetHashCode() => FullyQualifiedName?.GetHashCode() ?? 0;
}

internal sealed class AssemblyInfo : IEquatable<AssemblyInfo>
{
    public AssemblyInfo(string assemblyName, string? methodNameOverride, bool hasOpenApiTransformers = false, bool canMapEndpoints = true, bool canRecordOpenEndpoints = true)
    {
        CanRecordOpenEndpoints = canRecordOpenEndpoints;
        AssemblyName = assemblyName;
        MethodNameOverride = methodNameOverride;
        HasOpenApiTransformers = hasOpenApiTransformers;
        CanMapEndpoints = canMapEndpoints;
    }

    public string AssemblyName { get; }
    public string? MethodNameOverride { get; }

    /// <summary>
    /// True when the compilation can resolve <c>IEndpointRouteBuilder</c>, which every line of
    /// <c>EndpointMapping.g.cs</c> needs.
    /// </summary>
    /// <remarks>
    /// False in a typed-client project (M9): the client references the generator package for
    /// <see cref="EndpointClientGenerator"/>, but it has no ASP.NET Core — a Blazor WebAssembly
    /// client cannot have it (NETSDK1082, PRD R7.4). The mapping file would be nothing but errors
    /// there, so nothing is emitted and MPEP006 stays silent.
    /// </remarks>
    public bool CanMapEndpoints { get; }

    /// <summary>
    /// True when the compilation resolves <c>OpenEndpointAttribute</c> (Abstractions 11.2 or later),
    /// which the open-endpoint records are written with (issue #34). Against an older Abstractions the
    /// records are left out rather than emitted as a compile error.
    /// </summary>
    public bool CanRecordOpenEndpoints { get; }

    /// <summary>
    /// True when the consumer's compilation can register an OpenAPI operation transformer — it
    /// references a <c>Microsoft.AspNetCore.OpenApi</c> built on <c>Microsoft.OpenApi</c> 2.x or
    /// later.
    /// </summary>
    /// <remarks>
    /// The runtime library deliberately does not reference <c>Microsoft.AspNetCore.OpenApi</c>:
    /// <c>AddOpenApiOperationTransformer</c> is not in the shared framework, so taking it would force
    /// the package on every consumer, including those that never produce a document (PRD R4.7).
    /// The generated file is the one place that can depend on it conditionally — it is compiled
    /// in the consumer's project, against the consumer's references. So the schema transformer is
    /// emitted only when this is true, and a consumer without OpenAPI pays nothing.
    /// </remarks>
    public bool HasOpenApiTransformers { get; }

    /// <summary>
    /// The name of the generated mapping extension method — always a valid C# identifier.
    /// </summary>
    /// <remarks>
    /// Neither input is required to be one. An assembly name may contain hyphens or spaces, may
    /// start with a digit, and may contain empty dot-segments ("My..Api", "My."); the override is
    /// whatever string the consumer typed, including "". Characters that cannot appear in an
    /// identifier are dropped and an unusable result falls back to the assembly-derived name, then
    /// to a fixed one. <see cref="MethodNameWasSanitised"/> reports whether that happened, so the
    /// adjustment can be surfaced as MPEP006 instead of being silent.
    /// </remarks>
    public string GetMethodName()
    {
        if (MethodNameOverride is not null)
        {
            var fromOverride = KeepIdentifierCharacters(MethodNameOverride);
            if (fromOverride.Length > 0)
                return char.IsLetter(fromOverride[0]) || fromOverride[0] == '_'
                    ? fromOverride
                    : "_" + fromOverride;
        }

        return FromAssemblyName();
    }

    /// <summary>
    /// True when <see cref="GetMethodName"/> had to change or discard what it was given.
    /// </summary>
    public bool MethodNameWasSanitised =>
        MethodNameOverride is not null
            ? GetMethodName() != MethodNameOverride
            : GetMethodName() != NaiveAssemblyMethodName();

    /// <summary>The unsanitised name the original rule would have produced, for MPEP006's message.</summary>
    public string RequestedMethodName => MethodNameOverride ?? NaiveAssemblyMethodName();

    public string GetSafeClassName()
    {
        var method = GetMethodName();
        var stem = method.StartsWith("Map") ? method.Substring(3) : method;
        return stem.Length > 0 ? stem + "Extensions" : "EndpointMappingExtensions";
    }

    private string FromAssemblyName()
    {
        // No leading-character fixup: the "Map" prefix already starts the identifier, so a segment
        // beginning with a digit ("2Fast" -> Map2FastEndpoints) is fine as it stands.
        var core = KeepIdentifierCharacters(string.Concat(
            AssemblyName.Split('.').Where(part => part.Length > 0).Select(UcFirst)));

        return "Map" + (core.Length > 0 ? core : "Assembly") + "Endpoints";
    }

    private string NaiveAssemblyMethodName()
    {
        var segments = AssemblyName.Split('.');
        return segments.Any(segment => segment.Length == 0)
            // What the original rule did here was throw, so there is no "requested" name to report.
            ? "Map" + AssemblyName + "Endpoints"
            : "Map" + string.Concat(segments.Select(UcFirst)) + "Endpoints";
    }

    private static string UcFirst(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);

    /// <summary>Drops every character that cannot appear in a C# identifier.</summary>
    private static string KeepIdentifierCharacters(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
                builder.Append(character);
        }

        return builder.ToString();
    }

    public bool Equals(AssemblyInfo? other) =>
        other is not null &&
        AssemblyName == other.AssemblyName &&
        MethodNameOverride == other.MethodNameOverride &&
        HasOpenApiTransformers == other.HasOpenApiTransformers &&
        CanMapEndpoints == other.CanMapEndpoints &&
        CanRecordOpenEndpoints == other.CanRecordOpenEndpoints;

    public override bool Equals(object? obj) => Equals(obj as AssemblyInfo);
    public override int GetHashCode() => AssemblyName?.GetHashCode() ?? 0;
}

/// <summary>
/// Everything the producer and the diagnostic reporter need, as one value-equal unit.
/// </summary>
/// <remarks>
/// The source output is registered on <i>this</i> rather than on a <c>Producer</c> instance so the
/// incremental cache can actually hit: a node whose value is a freshly allocated object with no
/// value equality compares unequal on every compilation, which re-runs the whole generator.
/// </remarks>
internal sealed class EndpointModel : IEquatable<EndpointModel>
{
    public EndpointModel(ImmutableArray<EndpointInfo> endpoints, ImmutableArray<GroupInfo> groups, AssemblyInfo assembly, ClosingModel? closing = null)
    {
        Endpoints = endpoints;
        Groups = groups;
        Assembly = assembly;
        Closing = closing ?? ClosingModel.Empty;
    }

    public ImmutableArray<EndpointInfo> Endpoints { get; }
    public ImmutableArray<GroupInfo> Groups { get; }
    public AssemblyInfo Assembly { get; }

    /// <summary>The open endpoints this compilation closes, and what went wrong closing them.</summary>
    public ClosingModel Closing { get; }

    public bool Equals(EndpointModel? other) =>
        other is not null &&
        Assembly.Equals(other.Assembly) &&
        Closing.Equals(other.Closing) &&
        SequenceComparer<EndpointInfo>.Instance.Equals(Endpoints, other.Endpoints) &&
        SequenceComparer<GroupInfo>.Instance.Equals(Groups, other.Groups);

    public override bool Equals(object? obj) => Equals(obj as EndpointModel);

    public override int GetHashCode() =>
        unchecked((Assembly.GetHashCode() * 397) ^ (Endpoints.Length * 31) ^ Groups.Length);
}

/// <summary>
/// Element-wise equality for a collected provider's <see cref="ImmutableArray{T}"/>.
/// </summary>
/// <remarks>
/// <see cref="ImmutableArray{T}"/>'s own equality compares the underlying array by reference, so a
/// re-collected batch of equal elements still compares unequal. Attaching this with
/// <c>WithComparer</c> makes Roslyn recycle the previous instance, which is what lets every node
/// downstream of it report Cached.
/// </remarks>
internal sealed class SequenceComparer<T> : IEqualityComparer<ImmutableArray<T>>
{
    public static readonly SequenceComparer<T> Instance = new();

    public bool Equals(ImmutableArray<T> x, ImmutableArray<T> y)
    {
        if (x.IsDefault || y.IsDefault) return x.IsDefault && y.IsDefault;
        if (x.Length != y.Length) return false;

        for (var i = 0; i < x.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(x[i], y[i]))
                return false;
        }

        return true;
    }

    public int GetHashCode(ImmutableArray<T> obj) => obj.IsDefault ? 0 : obj.Length;
}

internal static class PathSpecs
{
    /// <summary>
    /// Field-wise equality for <see cref="PathSpec"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="PathSpec"/> carries a <c>[ValueComparer]</c> attribute, but a generated comparer is a
    /// separate object: Roslyn compares pipeline outputs with <c>EqualityComparer&lt;T&gt;.Default</c>,
    /// so a model is only value-equal at every step if the type itself implements equality. That is
    /// why every model here hand-writes <see cref="IEquatable{T}"/>, and why this helper exists for a
    /// type we do not own. MintPlayer/MintPlayer.Dotnet.Tools#184 proposes generating
    /// <c>IEquatable&lt;T&gt;</c> from <c>[AutoValueComparer]</c>; once it ships, these models can use it
    /// (packing its attributes assembly into this analyzer folder).
    /// </remarks>
    public static bool AreEqual(PathSpec? left, PathSpec? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left.ContainingNamespace != right.ContainingNamespace) return false;
        if (left.Parents.Length != right.Parents.Length) return false;

        for (var i = 0; i < left.Parents.Length; i++)
        {
            var a = left.Parents[i];
            var b = right.Parents[i];
            if (a.Name != b.Name
                || a.Type != b.Type
                || a.IsPartial != b.IsPartial
                || a.GenericTypeParameters != b.GenericTypeParameters)
            {
                return false;
            }
        }

        return true;
    }
}

internal static class LocationKeys
{
    /// <summary>
    /// Field-wise equality for <see cref="LocationKey"/>, which has no <c>Equals</c> of its own.
    /// </summary>
    public static bool AreEqual(LocationKey? left, LocationKey? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        return left.FilePath == right.FilePath
            && left.StartLine == right.StartLine
            && left.StartColumn == right.StartColumn
            && left.EndLine == right.EndLine
            && left.EndColumn == right.EndColumn;
    }
}
