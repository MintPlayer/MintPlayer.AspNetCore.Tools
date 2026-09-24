using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Reads the <c>[assembly: EndpointContract(…)]</c> attributes of every referenced assembly into
/// value-equal <see cref="ClientServer"/> models (PRD R7.4, R7.4a).
/// </summary>
/// <remarks>
/// <para>
/// <b>Enumerated through <see cref="Compilation.References"/> and
/// <see cref="Compilation.GetAssemblyOrModuleSymbol"/></b>, and memoised per
/// <see cref="MetadataReference"/> — the key <see cref="Compilation.GetMetadataReference"/> round-trips
/// to, which workspaces reuse across edits while assembly symbols come and go
/// (dotnet/roslyn#57997). A <see cref="ConditionalWeakTable{TKey, TValue}"/> holds the entries, so a
/// reference the host drops takes its entry with it. Only strings are cached, never symbols: a symbol
/// would keep its whole compilation alive for as long as the reference lives.
/// </para>
/// <para>
/// <b>Zero contracts is an empty result, never a diagnostic (R7.4a).</b> Under the IDE's analysis
/// service the referenced assemblies can come back empty, which a command-line build cannot detect or
/// reproduce; an error for "nothing found" would paint the IDE red while <c>dotnet build</c> stays
/// green.
/// </para>
/// <para>
/// <b>Matched by name, not by symbol.</b> Every server assembly declares its own internal copy of
/// the attribute (see <see cref="EndpointContracts"/>), so there is no single type to compare with.
/// </para>
/// </remarks>
internal static class ClientContractReader
{
    private static readonly ConditionalWeakTable<MetadataReference, ClientServer> Cache = new();

    public static ImmutableArray<ClientServer> Read(Compilation compilation, CancellationToken cancellationToken)
    {
        var servers = new List<ClientServer>();

        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly) continue;

            var key = compilation.GetMetadataReference(assembly) ?? reference;
            if (!Cache.TryGetValue(key, out var server))
            {
                server = ReadAssembly(assembly, cancellationToken);
                if (server.IsCacheable)
                {
                    // Another thread may have added it meanwhile; either value is correct.
                    try { Cache.Add(key, server); }
                    catch (ArgumentException) { }
                }
            }

            if (!server.IsEmpty) servers.Add(server);
        }

        return servers
            .OrderBy(server => server.AssemblyName, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal static ClientServer ReadAssembly(IAssemblySymbol assembly, CancellationToken cancellationToken)
    {
        var endpoints = ImmutableArray.CreateBuilder<ClientEndpoint>();
        var problems = ImmutableArray.CreateBuilder<ClientProblem>();
        var cacheable = true;

        foreach (var attribute in assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null ||
                attributeClass.Name != EndpointContracts.AttributeName ||
                attributeClass.ContainingNamespace?.ToDisplayString() != EndpointContracts.AttributeNamespace)
                continue;

            var contract = new ContractReading(assembly, attribute);
            if (contract.Name is null) continue; // Not a shape this generator ever wrote; nothing to name it by.

            var endpoint = contract.Resolve(out var skipReason, out var serverTypes, out var unresolved);
            if (unresolved) cacheable = false;

            if (endpoint is null)
            {
                problems.Add(new ClientProblem(
                    DiagnosticDescriptors.ClientContractSkipped.Id,
                    ImmutableArray.Create(contract.Name, assembly.Name, skipReason ?? "its contract could not be read")));
                continue;
            }

            endpoints.Add(endpoint);
            foreach (var type in serverTypes)
            {
                problems.Add(new ClientProblem(
                    DiagnosticDescriptors.ClientUsesServerAssemblyType.Id,
                    ImmutableArray.Create(contract.Name, type, assembly.Name)));
            }
        }

        return new ClientServer(
            assembly.Name,
            endpoints.OrderBy(endpoint => endpoint.Name, StringComparer.Ordinal).ToImmutableArray(),
            problems.ToImmutable(),
            cacheable);
    }

    /// <summary>The raw arguments of one contract attribute.</summary>
    private sealed class ContractReading
    {
        private readonly IAssemblySymbol server;

        public ContractReading(IAssemblySymbol server, AttributeData attribute)
        {
            this.server = server;

            var arguments = attribute.ConstructorArguments;
            if (arguments.Length != 4) return;

            Name = arguments[1].Value as string;
            Template = arguments[2].Value as string;
            Methods = Strings(arguments[3]);

            foreach (var named in attribute.NamedArguments)
            {
                switch (named.Key)
                {
                    case "Version": Version = named.Value.Value is int version ? version : 0; break;
                    case "RequestType": RequestType = named.Value.Value as ITypeSymbol; break;
                    case "ResponseType": ResponseType = named.Value.Value as ITypeSymbol; break;
                    case "RouteParameterNames": RouteNames = Strings(named.Value); break;
                    case "RouteParameterTypes": RouteTypes = Types(named.Value); break;
                    case "QueryParameterNames": QueryNames = Strings(named.Value); break;
                    case "QueryParameterTypes": QueryTypes = Types(named.Value); break;
                    // Unknown named arguments are a newer generator's additive fields: ignored.
                }
            }
        }

        public string? Name { get; }
        public string? Template { get; }
        public string?[]? Methods { get; }
        public int Version { get; }
        public ITypeSymbol? RequestType { get; }
        public ITypeSymbol? ResponseType { get; }
        public string?[]? RouteNames { get; }
        public ITypeSymbol?[]? RouteTypes { get; }
        public string?[]? QueryNames { get; }
        public ITypeSymbol?[]? QueryTypes { get; }

        public ClientEndpoint? Resolve(out string? skipReason, out List<string> serverTypes, out bool unresolved)
        {
            serverTypes = new List<string>();
            unresolved = false;

            if (Version > EndpointContracts.Version)
            {
                skipReason = $"its contract has format version {Version}, and this client generator understands up to {EndpointContracts.Version}; update the client's MintPlayer.AspNetCore.Endpoints.Generator package";
                return null;
            }

            if (Template is null || Methods is null || Methods.Length == 0 || Methods.Any(method => string.IsNullOrEmpty(method)))
            {
                skipReason = "its contract has no route template or no HTTP method";
                return null;
            }

            if (!Parallel(RouteNames, RouteTypes) || !Parallel(QueryNames, QueryTypes))
            {
                skipReason = "its contract lists a different number of parameter names and types";
                return null;
            }

            // Every type the method signature names must resolve and be public here.
            var types = new List<ITypeSymbol?> { RequestType, ResponseType };
            if (RouteTypes is not null) types.AddRange(RouteTypes);
            if (QueryTypes is not null) types.AddRange(QueryTypes);

            foreach (var type in types)
            {
                if (type is null) continue;

                if (ContainsErrorType(type))
                {
                    unresolved = true;
                    skipReason = $"type '{type.ToDisplayString()}' cannot be resolved in this project; reference the assembly that declares it";
                    return null;
                }

                if (!IsPublic(type))
                {
                    skipReason = $"type '{type.ToDisplayString()}' is not public, so a client cannot name it";
                    return null;
                }
            }

            foreach (var type in types)
            {
                if (type is null) continue;
                foreach (var declared in DeclaredIn(type, server))
                {
                    if (!serverTypes.Contains(declared)) serverTypes.Add(declared);
                }
            }

            var tokens = TypedLinks.RouteTokens(Template);
            var route = ImmutableArray.CreateBuilder<ClientParameter>();
            for (var i = 0; i < (RouteNames?.Length ?? 0); i++)
            {
                var name = RouteNames![i];
                if (string.IsNullOrEmpty(name))
                {
                    skipReason = "its contract has an unnamed route parameter";
                    return null;
                }

                var token = tokens.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                var optional = token.Name is not null && (token.IsOptional || token.HasDefault || token.IsCatchAll);
                route.Add(new ClientParameter(name!, Display(RouteTypes![i]), optional));
            }

            var query = ImmutableArray.CreateBuilder<ClientParameter>();
            for (var i = 0; i < (QueryNames?.Length ?? 0); i++)
            {
                var name = QueryNames![i];
                if (string.IsNullOrEmpty(name))
                {
                    skipReason = "its contract has an unnamed query parameter";
                    return null;
                }

                query.Add(new ClientParameter(name!, Display(QueryTypes![i]), true));
            }

            skipReason = null;
            return new ClientEndpoint(
                Name!,
                Template,
                Methods.Select(method => method!.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToImmutableArray(),
                RequestType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ResponseType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ResponseType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T },
                route.ToImmutable(),
                query.ToImmutable());
        }

        /// <summary>A route or query type as emitted: <c>Nullable&lt;T&gt;</c> is unwrapped, since the emitter adds its own <c>?</c>.</summary>
        private static string Display(ITypeSymbol? type)
        {
            if (type is null) return "string";
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
                type = nullable.TypeArguments[0];
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static bool Parallel(string?[]? names, ITypeSymbol?[]? types) =>
            (names?.Length ?? 0) == (types?.Length ?? 0);

        private static string?[]? Strings(TypedConstant constant) =>
            constant.Kind == TypedConstantKind.Array && !constant.Values.IsDefault
                ? constant.Values.Select(value => value.Value as string).ToArray()
                : null;

        private static ITypeSymbol?[]? Types(TypedConstant constant) =>
            constant.Kind == TypedConstantKind.Array && !constant.Values.IsDefault
                ? constant.Values.Select(value => value.Value as ITypeSymbol).ToArray()
                : null;
    }

    private static bool ContainsErrorType(ITypeSymbol type) => type switch
    {
        IErrorTypeSymbol => true,
        IArrayTypeSymbol array => ContainsErrorType(array.ElementType),
        INamedTypeSymbol named => named.TypeKind == TypeKind.Error || named.TypeArguments.Any(ContainsErrorType),
        _ => type.TypeKind == TypeKind.Error,
    };

    /// <summary>
    /// Public all the way out, type arguments included. <c>InternalsVisibleTo</c> is deliberately not
    /// consulted: the answer is memoised per server reference and must not depend on which client asks.
    /// </summary>
    private static bool IsPublic(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsPublic(array.ElementType);
            case ITypeParameterSymbol:
            case IPointerTypeSymbol:
            case IFunctionPointerTypeSymbol:
                return false;
            case INamedTypeSymbol named:
                for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
                {
                    if (current.DeclaredAccessibility != Accessibility.Public) return false;
                }

                return named.TypeArguments.All(IsPublic);
            default:
                return type.SpecialType != SpecialType.None;
        }
    }

    /// <summary>The named types within <paramref name="type"/> that <paramref name="server"/> declares.</summary>
    private static IEnumerable<string> DeclaredIn(ITypeSymbol type, IAssemblySymbol server)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                foreach (var inner in DeclaredIn(array.ElementType, server)) yield return inner;
                break;
            case INamedTypeSymbol named:
                if (SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, server))
                    yield return named.OriginalDefinition.ToDisplayString();
                foreach (var argument in named.TypeArguments)
                {
                    foreach (var inner in DeclaredIn(argument, server)) yield return inner;
                }
                break;
        }
    }
}
