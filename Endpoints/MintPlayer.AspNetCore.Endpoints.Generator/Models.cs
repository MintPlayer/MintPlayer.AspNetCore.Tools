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
        ImmutableArray<BoundProperty> boundProperties = default)
    {
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
        // ImmutableArray's own equality compares the backing array by reference. Using it here
        // would make every run look like a change and kill incremental caching, silently.
        SequenceComparer<BoundProperty>.Instance.Equals(BoundProperties, other.BoundProperties);

    public override bool Equals(object? obj) => Equals(obj as EndpointInfo);
    public override int GetHashCode() => FullyQualifiedName?.GetHashCode() ?? 0;
}

internal sealed class GroupInfo : IEquatable<GroupInfo>
{
    public GroupInfo(string fullyQualifiedName, string? parentGroupFqn,
        LocationKey? location = null, string? prefix = null)
    {
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

    public bool Equals(GroupInfo? other) =>
        other is not null &&
        FullyQualifiedName == other.FullyQualifiedName &&
        ParentGroupFqn == other.ParentGroupFqn &&
        LocationKeys.AreEqual(Location, other.Location) &&
        Prefix == other.Prefix;

    public override bool Equals(object? obj) => Equals(obj as GroupInfo);
    public override int GetHashCode() => FullyQualifiedName?.GetHashCode() ?? 0;
}

internal sealed class AssemblyInfo : IEquatable<AssemblyInfo>
{
    public AssemblyInfo(string assemblyName, string? methodNameOverride, bool hasOpenApiTransformers = false)
    {
        AssemblyName = assemblyName;
        MethodNameOverride = methodNameOverride;
        HasOpenApiTransformers = hasOpenApiTransformers;
    }

    public string AssemblyName { get; }
    public string? MethodNameOverride { get; }

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
        HasOpenApiTransformers == other.HasOpenApiTransformers;

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
    public EndpointModel(ImmutableArray<EndpointInfo> endpoints, ImmutableArray<GroupInfo> groups, AssemblyInfo assembly)
    {
        Endpoints = endpoints;
        Groups = groups;
        Assembly = assembly;
    }

    public ImmutableArray<EndpointInfo> Endpoints { get; }
    public ImmutableArray<GroupInfo> Groups { get; }
    public AssemblyInfo Assembly { get; }

    public bool Equals(EndpointModel? other) =>
        other is not null &&
        Assembly.Equals(other.Assembly) &&
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
    /// <see cref="PathSpec"/> carries a <c>[ValueComparer]</c> attribute, but using the generated
    /// comparer would put <c>MintPlayer.ValueComparerGenerator.Attributes.dll</c> on this
    /// generator's analyzer-load path — a dependency this package deliberately does not have, and
    /// one whose absence fails at load time with an error naming an assembly the consumer never
    /// referenced. Fifteen hand-written lines are the cheaper trade, and they match how
    /// <see cref="LocationKeys"/> already handles the same problem.
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
