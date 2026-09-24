namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>One route or query parameter of an endpoint contract: its wire name and its type.</summary>
internal sealed class ContractParameter
{
    public ContractParameter(string key, string typeFqn)
    {
        Key = key;
        TypeFqn = typeFqn;
    }

    /// <summary>The route token or query key, exactly as the server reads it.</summary>
    public string Key { get; }

    /// <summary>The annotation-free type, <c>global::</c>-qualified; <c>string</c> for an unbound token.</summary>
    public string TypeFqn { get; }
}

/// <summary>What the server writes into one <c>[assembly: EndpointContract(…)]</c> (PRD R7.4).</summary>
internal sealed class EndpointContract
{
    public EndpointContract(EndpointInfo endpoint, string template, string[] methods,
        List<ContractParameter> routeParameters, List<ContractParameter> queryParameters)
    {
        Endpoint = endpoint;
        Template = template;
        Methods = methods;
        RouteParameters = routeParameters;
        QueryParameters = queryParameters;
    }

    public EndpointInfo Endpoint { get; }

    /// <summary>The composed route, group prefixes included — what the client substitutes into.</summary>
    public string Template { get; }

    /// <summary>The verbs, upper-cased and ordinally sorted.</summary>
    public string[] Methods { get; }

    /// <summary>Every token of <see cref="Template"/>, in template order.</summary>
    public List<ContractParameter> RouteParameters { get; }

    /// <summary>The endpoint's <c>[QueryParam]</c> properties, minus any whose key is also a token.</summary>
    public List<ContractParameter> QueryParameters { get; }
}

/// <summary>
/// Decides which endpoints get an assembly-level contract, and what it says.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same endpoints as the typed links, for the same reason: never a wrong contract.</b> An
/// endpoint is left out when its composed route is unknown (MPEP011), when it is an MPEP012
/// duplicate (it has no route name of its own), or when its verbs are not known at compile time —
/// a client method has to name one. The route parameters are read exactly as
/// <see cref="TypedLinks"/> reads them, so a client call and a server-side link agree on names and
/// types.
/// </para>
/// <para>
/// <b>Why the attribute type is generated into the server rather than shipped in
/// Abstractions</b> (a deviation from the PLAN, measured in M9). A client references the server
/// assembly metadata-only and nothing else of the server's (PRD R7.4). An attribute whose class
/// lives in an assembly the client does not reference is an error type to Roslyn, and its
/// arguments decode to <b>nothing</b> — zero constructor arguments, zero named arguments — so the
/// contract would be invisible. Abstractions is a Web SDK package, so the client cannot reference it
/// either without NETSDK1082 on browser-wasm. Declaring the attribute in the one assembly the client
/// does reference makes the contract self-contained. It is <c>internal</c>, so two endpoint
/// assemblies that reference each other do not see each other's copy; an internal attribute still
/// survives into reference assemblies and still decodes in full from metadata (both measured).
/// </para>
/// </remarks>
internal static class EndpointContracts
{
    /// <summary>
    /// The contract format this generator writes and the client generator understands. Additive
    /// changes are new named arguments, which an older client ignores; a change an older client
    /// would misread must raise this number, and such a client then skips the contract with MPEP021
    /// instead of generating a wrong call.
    /// </summary>
    public const int Version = 1;

    public const string AttributeName = "EndpointContractAttribute";

    /// <summary>The namespace the attribute is declared in, in every server assembly.</summary>
    public const string AttributeNamespace = EndpointsProducer.GeneratedNamespace;

    public static List<EndpointContract> From(EndpointMappingPlan plan)
    {
        var contracts = new List<EndpointContract>();

        foreach (var endpoint in plan.MappableEndpoints)
        {
            var template = plan.ComposedRoutes[endpoint.FullyQualifiedName];
            if (template is null || !plan.IsNamed(endpoint) || endpoint.KnownMethods is null) continue;

            var methods = MethodsLiteral.Decode(endpoint.KnownMethods);
            if (methods.Length == 0) continue;

            var bound = ShadowParameters.Bound(endpoint);
            var tokens = TypedLinks.RouteTokens(template);

            var route = tokens
                .Select(token => new ContractParameter(
                    token.Name,
                    bound.FirstOrDefault(property =>
                        property.Source == BoundSource.Route &&
                        string.Equals(property.Key, token.Name, StringComparison.OrdinalIgnoreCase))?.ConversionTypeFqn ?? "string"))
                .ToList();

            var query = bound
                .Where(property => property.Source == BoundSource.Query)
                .Where(property => !tokens.Any(token => string.Equals(token.Name, property.Key, StringComparison.OrdinalIgnoreCase)))
                .Select(property => new ContractParameter(property.Key, property.ConversionTypeFqn))
                .ToList();

            contracts.Add(new EndpointContract(endpoint, template, methods, route, query));
        }

        return contracts;
    }
}
