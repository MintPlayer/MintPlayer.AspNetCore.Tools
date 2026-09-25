using System.Collections.Immutable;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>One parameter of a client method: its wire name, emitted type and optionality.</summary>
[GenerateEquality]
internal sealed partial class ClientParameter
{
    public ClientParameter(string key, string typeFqn, bool isOptional)
    {
        Key = key;
        TypeFqn = typeFqn;
        IsOptional = isOptional;
    }

    /// <summary>The route token or query key.</summary>
    public string Key { get; }

    /// <summary>The type, <c>global::</c>-qualified and never already nullable.</summary>
    public string TypeFqn { get; }

    /// <summary>Emitted as <c>T?</c>; a null argument leaves the value out.</summary>
    public bool IsOptional { get; }
}

/// <summary>One endpoint contract, resolved against the client compilation and reduced to strings.</summary>
[GenerateEquality]
internal sealed partial class ClientEndpoint
{
    public ClientEndpoint(string name, string template, ImmutableArray<string> methods,
        string? requestTypeFqn, string? responseTypeFqn, bool responseIsNullableValueType,
        ImmutableArray<ClientParameter> routeParameters, ImmutableArray<ClientParameter> queryParameters)
    {
        Name = name;
        Template = template;
        Methods = methods;
        RequestTypeFqn = requestTypeFqn;
        ResponseTypeFqn = responseTypeFqn;
        ResponseIsNullableValueType = responseIsNullableValueType;
        RouteParameters = routeParameters;
        QueryParameters = queryParameters;
    }

    /// <summary>The endpoint's unique name on the server.</summary>
    public string Name { get; }

    public string Template { get; }
    public ImmutableArray<string> Methods { get; }
    public string? RequestTypeFqn { get; }
    public string? ResponseTypeFqn { get; }

    /// <summary>The response type is already <c>Nullable&lt;T&gt;</c>, so the return type adds no <c>?</c>.</summary>
    public bool ResponseIsNullableValueType { get; }

    public ImmutableArray<ClientParameter> RouteParameters { get; }
    public ImmutableArray<ClientParameter> QueryParameters { get; }
}

/// <summary>A diagnostic the client generator reports, as a value: descriptor id and message arguments.</summary>
[GenerateEquality]
internal sealed partial class ClientProblem
{
    public ClientProblem(string id, ImmutableArray<string> arguments)
    {
        Id = id;
        Arguments = arguments;
    }

    public string Id { get; }
    public ImmutableArray<string> Arguments { get; }
}

/// <summary>Everything read from one referenced server assembly.</summary>
[GenerateEquality]
internal sealed partial class ClientServer
{
    public ClientServer(string assemblyName, ImmutableArray<ClientEndpoint> endpoints, ImmutableArray<ClientProblem> problems, bool isCacheable)
    {
        AssemblyName = assemblyName;
        Endpoints = endpoints;
        Problems = problems;
        IsCacheable = isCacheable;
    }

    public string AssemblyName { get; }
    public ImmutableArray<ClientEndpoint> Endpoints { get; }
    public ImmutableArray<ClientProblem> Problems { get; }

    /// <summary>
    /// False when a contract type could not be resolved. Such a result depends on which assemblies
    /// the client references, which can change while the server's metadata reference stays the same,
    /// so it is never memoised: adding the missing reference must take effect.
    /// </summary>
    [EqualityIgnore]
    public bool IsCacheable { get; }

    [EqualityIgnore]
    public bool IsEmpty => Endpoints.IsEmpty && Problems.IsEmpty;
}

/// <summary>The whole input of the client source output, value-equal so the output caches.</summary>
[GenerateEquality]
internal sealed partial class ClientModel
{
    public static readonly ClientModel Disabled = new(false, null, false, ImmutableArray<ClientServer>.Empty);

    public ClientModel(bool enabled, string? missingPrerequisite, bool emitGlobalUsing, ImmutableArray<ClientServer> servers)
    {
        Enabled = enabled;
        MissingPrerequisite = missingPrerequisite;
        EmitGlobalUsing = emitGlobalUsing;
        Servers = servers;
    }

    /// <summary><c>GenerateEndpointsClient</c> is <c>true</c>.</summary>
    public bool Enabled { get; }

    /// <summary>The first type the client needs and the project lacks, or null.</summary>
    public string? MissingPrerequisite { get; }

    /// <summary>
    /// Whether the client file adds the <c>global using</c> of the generated namespace. Not when the
    /// project also maps endpoints: <c>EndpointMapping.g.cs</c> already emits it.
    /// </summary>
    public bool EmitGlobalUsing { get; }

    /// <summary>The referenced server assemblies that carry contracts, in ordinal name order.</summary>
    public ImmutableArray<ClientServer> Servers { get; }
}
