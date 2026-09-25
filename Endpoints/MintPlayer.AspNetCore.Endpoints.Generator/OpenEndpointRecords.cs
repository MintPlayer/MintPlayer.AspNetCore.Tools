using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>One <c>[assembly: OpenEndpoint(…)]</c> record, as strings (PRD D3a).</summary>
internal sealed class OpenEndpointRecord
{
    public OpenEndpointRecord(string metadataName, string? path, string? methods, bool hasBinder)
    {
        MetadataName = metadataName;
        Path = path;
        Methods = methods;
        HasBinder = hasBinder;
    }

    public string MetadataName { get; }
    public string? Path { get; }
    public string? Methods { get; }
    public bool HasBinder { get; }
}

/// <summary>What one referenced assembly records about its open endpoints and their groups.</summary>
internal sealed class OpenEndpointLibrary
{
    public static readonly OpenEndpointLibrary None = new(
        ImmutableArray<OpenEndpointRecord>.Empty, ImmutableDictionary<string, string?>.Empty, cacheable: true);

    public OpenEndpointLibrary(ImmutableArray<OpenEndpointRecord> endpoints, ImmutableDictionary<string, string?> groupPrefixes, bool cacheable)
    {
        Endpoints = endpoints;
        GroupPrefixes = groupPrefixes;
        IsCacheable = cacheable;
    }

    public ImmutableArray<OpenEndpointRecord> Endpoints { get; }

    /// <summary>Group definition (<c>OriginalDefinition</c>, fully qualified) to its recorded <c>Prefix</c>.</summary>
    public ImmutableDictionary<string, string?> GroupPrefixes { get; }

    public bool IsCacheable { get; }
}

/// <summary>
/// Reads the open-endpoint records of referenced assemblies (PRD D3, D3a).
/// </summary>
/// <remarks>
/// The M9 pattern of <see cref="ClientContractReader"/>: enumerated through
/// <see cref="Compilation.References"/>, memoised per <see cref="MetadataReference"/> in a
/// <see cref="ConditionalWeakTable{TKey, TValue}"/>, strings only — never a symbol, which would keep
/// its whole compilation alive — and not cached when a recorded type failed to resolve. Called only
/// when the compilation declares an <c>EndpointTypeArgument</c>, so an application that closes nothing
/// never reads a reference.
/// </remarks>
internal static class OpenEndpointRecords
{
    public const string EndpointAttributeName = "OpenEndpointAttribute";
    public const string GroupAttributeName = "OpenEndpointGroupAttribute";
    public const string AttributeNamespace = "MintPlayer.AspNetCore.Endpoints";

    /// <summary>The record format this generator writes and reads.</summary>
    public const int Version = 1;

    private static readonly ConditionalWeakTable<MetadataReference, OpenEndpointLibrary> Cache = new();

    public static List<(IAssemblySymbol Assembly, OpenEndpointLibrary Library)> Read(Compilation compilation, CancellationToken cancellationToken)
    {
        var libraries = new List<(IAssemblySymbol, OpenEndpointLibrary)>();

        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly) continue;

            var key = compilation.GetMetadataReference(assembly) ?? reference;
            if (!Cache.TryGetValue(key, out var library))
            {
                library = ReadAssembly(assembly, cancellationToken);
                if (library.IsCacheable)
                {
                    // Another thread may have added it meanwhile; either value is correct.
                    try { Cache.Add(key, library); }
                    catch (ArgumentException) { }
                }
            }

            if (library.Endpoints.Length > 0 || library.GroupPrefixes.Count > 0)
                libraries.Add((assembly, library));
        }

        return libraries
            .OrderBy(library => library.Item1.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static OpenEndpointLibrary ReadAssembly(IAssemblySymbol assembly, CancellationToken cancellationToken)
    {
        List<OpenEndpointRecord>? endpoints = null;
        ImmutableDictionary<string, string?>.Builder? groups = null;
        var cacheable = true;

        foreach (var attribute in assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace) continue;

            var isEndpoint = attributeClass.Name == EndpointAttributeName;
            if (!isEndpoint && attributeClass.Name != GroupAttributeName) continue;

            if (attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol type ||
                type.TypeKind == TypeKind.Error)
            {
                cacheable = false;
                continue;
            }

            string? path = null, methods = null, prefix = null;
            var hasBinder = false;
            var version = 0;
            foreach (var named in attribute.NamedArguments)
            {
                switch (named.Key)
                {
                    case "Path": path = named.Value.Value as string; break;
                    case "Methods": methods = named.Value.Value as string; break;
                    case "HasBinder": hasBinder = named.Value.Value is true; break;
                    case "Prefix": prefix = named.Value.Value as string; break;
                    case "Version": version = named.Value.Value is int value ? value : 0; break;
                }
            }

            // A newer generator's record may mean something this one would misread.
            if (version > Version) continue;

            if (isEndpoint)
                (endpoints ??= new List<OpenEndpointRecord>()).Add(new OpenEndpointRecord(GenericTypes.MetadataNameOf(type), path, methods, hasBinder));
            else
                (groups ??= ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal))[type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = prefix;
        }

        if (endpoints is null && groups is null) return cacheable ? OpenEndpointLibrary.None : new OpenEndpointLibrary(ImmutableArray<OpenEndpointRecord>.Empty, ImmutableDictionary<string, string?>.Empty, false);

        return new OpenEndpointLibrary(
            endpoints?.OrderBy(record => record.MetadataName, StringComparer.Ordinal).ToImmutableArray() ?? ImmutableArray<OpenEndpointRecord>.Empty,
            groups?.ToImmutable() ?? ImmutableDictionary<string, string?>.Empty,
            cacheable);
    }
}
