namespace MintPlayer.AspNetCore.Endpoints.Generator;

internal enum EndpointLevel { Raw, Typed, TypedWithResponse }
internal enum HttpMethodKind { Custom, Get, Post, Put, Delete, Patch }

internal sealed class EndpointInfo : IEquatable<EndpointInfo>
{
    public EndpointInfo(string fqn, string ns, string className, bool isPartial, bool hasExistingBaseClass,
        EndpointLevel level, HttpMethodKind httpMethod,
        string? requestTypeFqn, string? responseTypeFqn,
        string? groupTypeFqn, bool hasMultipleGroups)
    {
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
        HasMultipleGroups = hasMultipleGroups;
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
    public bool HasMultipleGroups { get; }

    public string? GetBaseClassName()
    {
        if (Level == EndpointLevel.Raw) return null;
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
        HasMultipleGroups == other.HasMultipleGroups;

    public override bool Equals(object? obj) => Equals(obj as EndpointInfo);
    public override int GetHashCode() => FullyQualifiedName?.GetHashCode() ?? 0;
}

internal sealed class GroupInfo : IEquatable<GroupInfo>
{
    public GroupInfo(string fullyQualifiedName, string? parentGroupFqn, bool hasMultipleParents)
    {
        FullyQualifiedName = fullyQualifiedName;
        ParentGroupFqn = parentGroupFqn;
        HasMultipleParents = hasMultipleParents;
    }

    public string FullyQualifiedName { get; }
    public string? ParentGroupFqn { get; }
    public bool HasMultipleParents { get; }

    public bool Equals(GroupInfo? other) =>
        other is not null &&
        FullyQualifiedName == other.FullyQualifiedName &&
        ParentGroupFqn == other.ParentGroupFqn &&
        HasMultipleParents == other.HasMultipleParents;

    public override bool Equals(object? obj) => Equals(obj as GroupInfo);
    public override int GetHashCode() => FullyQualifiedName?.GetHashCode() ?? 0;
}

internal sealed class AssemblyInfo : IEquatable<AssemblyInfo>
{
    public AssemblyInfo(string assemblyName, string? methodNameOverride)
    {
        AssemblyName = assemblyName;
        MethodNameOverride = methodNameOverride;
    }

    public string AssemblyName { get; }
    public string? MethodNameOverride { get; }

    public string GetMethodName()
    {
        if (MethodNameOverride is not null) return MethodNameOverride;
        var parts = AssemblyName.Split('.');
        return "Map" + string.Concat(parts.Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1))) + "Endpoints";
    }

    public string GetSafeClassName()
    {
        var method = GetMethodName();
        return method.StartsWith("Map") ? method.Substring(3) + "Extensions" : method + "Extensions";
    }

    public bool Equals(AssemblyInfo? other) =>
        other is not null && AssemblyName == other.AssemblyName && MethodNameOverride == other.MethodNameOverride;

    public override bool Equals(object? obj) => Equals(obj as AssemblyInfo);
    public override int GetHashCode() => AssemblyName?.GetHashCode() ?? 0;
}
