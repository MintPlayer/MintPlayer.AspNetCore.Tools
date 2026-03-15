using System;
using System.Collections.Generic;
using System.Linq;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

internal enum EndpointLevel
{
    Raw,
    Typed,
    TypedWithResponse
}

internal enum HttpMethodKind
{
    Custom,
    Get,
    Post,
    Put,
    Delete,
    Patch
}

internal sealed class EndpointInfo : IEquatable<EndpointInfo>
{
    public EndpointInfo(
        string fullyQualifiedName,
        string ns,
        string className,
        bool isPartial,
        bool hasExistingBaseClass,
        EndpointLevel level,
        HttpMethodKind httpMethod,
        string? requestTypeFqn,
        string? responseTypeFqn,
        string? groupTypeFqn,
        bool hasMultipleGroups)
    {
        FullyQualifiedName = fullyQualifiedName;
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

    public bool NeedsPartialClass => Level != EndpointLevel.Raw;

    public string? GetBaseClassName()
    {
        if (Level == EndpointLevel.Raw) return null;

        var baseClass = HttpMethod switch
        {
            HttpMethodKind.Post => "PostEndpoint",
            HttpMethodKind.Put => "PutEndpoint",
            HttpMethodKind.Patch => "PatchEndpoint",
            HttpMethodKind.Get => "GetEndpoint",
            HttpMethodKind.Delete => "DeleteEndpoint",
            _ => "EndpointBase"
        };

        return $"global::MintPlayer.AspNetCore.Endpoints.{baseClass}<{RequestTypeFqn}>";
    }

    public bool Equals(EndpointInfo? other)
    {
        if (other is null) return false;
        return FullyQualifiedName == other.FullyQualifiedName &&
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
    }

    public override bool Equals(object? obj) => Equals(obj as EndpointInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (FullyQualifiedName?.GetHashCode() ?? 0);
            hash = hash * 31 + (Namespace?.GetHashCode() ?? 0);
            hash = hash * 31 + (ClassName?.GetHashCode() ?? 0);
            hash = hash * 31 + IsPartial.GetHashCode();
            hash = hash * 31 + HasExistingBaseClass.GetHashCode();
            hash = hash * 31 + Level.GetHashCode();
            hash = hash * 31 + HttpMethod.GetHashCode();
            hash = hash * 31 + (RequestTypeFqn?.GetHashCode() ?? 0);
            hash = hash * 31 + (ResponseTypeFqn?.GetHashCode() ?? 0);
            hash = hash * 31 + (GroupTypeFqn?.GetHashCode() ?? 0);
            hash = hash * 31 + HasMultipleGroups.GetHashCode();
            return hash;
        }
    }
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
        if (MethodNameOverride is not null)
            return MethodNameOverride;

        // Derive from assembly name: "MyApp.Api" → "MapMyAppApiEndpoints"
        var parts = AssemblyName.Split('.');
        var pascal = string.Concat(parts.Select(ToPascalCase));
        return $"Map{pascal}Endpoints";
    }

    public string GetSafeClassName()
    {
        if (MethodNameOverride is not null)
            return MethodNameOverride.Replace("Map", "") + "Extensions";

        var parts = AssemblyName.Split('.');
        var pascal = string.Concat(parts.Select(ToPascalCase));
        return $"{pascal}EndpointExtensions";
    }

    public string GetMetadataClassName()
    {
        if (MethodNameOverride is not null)
            return MethodNameOverride.Replace("Map", "") + "Metadata";

        var parts = AssemblyName.Split('.');
        var pascal = string.Concat(parts.Select(ToPascalCase));
        return $"{pascal}EndpointMetadata";
    }

    private static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    public bool Equals(AssemblyInfo? other)
    {
        if (other is null) return false;
        return AssemblyName == other.AssemblyName &&
               MethodNameOverride == other.MethodNameOverride;
    }

    public override bool Equals(object? obj) => Equals(obj as AssemblyInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            return (AssemblyName?.GetHashCode() ?? 0) * 31 +
                   (MethodNameOverride?.GetHashCode() ?? 0);
        }
    }
}
