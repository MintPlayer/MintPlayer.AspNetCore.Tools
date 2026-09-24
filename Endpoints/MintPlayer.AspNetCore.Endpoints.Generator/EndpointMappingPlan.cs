namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Turns the discovered endpoints and groups into the exact, ordered set of things to emit — and the
/// exact set of things that cannot be emitted, with the reason why.
/// </summary>
/// <remarks>
/// The producer and the diagnostic reporter both build one of these, so they cannot disagree: every
/// endpoint the producer silently drops is an endpoint the reporter has a diagnostic for.
/// <para>
/// Every collection is ordered, and none of it comes out of a hash set. Route order, factory field
/// numbering and the descriptor list are all observable in the emitted text, so an unordered source
/// makes the generated file differ between processes (string hashing is per-process) and defeats
/// reproducible builds.
/// </para>
/// </remarks>
internal sealed class EndpointMappingPlan
{
    private EndpointMappingPlan(
        List<EndpointInfo> declared,
        List<EndpointInfo> mappable,
        List<GroupInfo> groups,
        List<string> rootGroups,
        Dictionary<string, List<string>> childGroups,
        Dictionary<string, List<EndpointInfo>> endpointsByGroup,
        Dictionary<string, int> factoryIndex,
        Dictionary<string, List<string>> groupChains,
        HashSet<string> cyclicGroups,
        Dictionary<string, string?> composedRoutes,
        List<string> unjoinedGroups,
        Dictionary<string, EndpointInfo> duplicateNames)
    {
        DuplicateNames = duplicateNames;
        ComposedRoutes = composedRoutes;
        UnjoinedGroups = unjoinedGroups;
        DeclaredEndpoints = declared;
        MappableEndpoints = mappable;
        Groups = groups;
        RootGroups = rootGroups;
        ChildGroups = childGroups;
        EndpointsByGroup = endpointsByGroup;
        FactoryIndex = factoryIndex;
        GroupChains = groupChains;
        CyclicGroups = cyclicGroups;
    }

    /// <summary>
    /// The full route each mappable endpoint answers on, keyed by fully qualified name, or
    /// <see langword="null"/> where it could not be recovered at compile time.
    /// </summary>
    /// <remarks>
    /// A null entry means "unknown", never "empty" — see <see cref="ComposedRoute"/>. An endpoint
    /// whose <c>Path</c> is computed, or whose group's <c>Prefix</c> is, appears here as null and
    /// must simply be skipped by anything reading this, rather than compared against.
    /// </remarks>
    public Dictionary<string, string?> ComposedRoutes { get; }

    /// <summary>Every discovered endpoint, deduplicated and ordered. Drives the partial base classes.</summary>
    public List<EndpointInfo> DeclaredEndpoints { get; }

    /// <summary>The endpoints that get a route, a factory field and a descriptor.</summary>
    public List<EndpointInfo> MappableEndpoints { get; }

    public List<GroupInfo> Groups { get; }
    public List<string> RootGroups { get; }
    public Dictionary<string, List<string>> ChildGroups { get; }
    public Dictionary<string, List<EndpointInfo>> EndpointsByGroup { get; }

    /// <summary>Fully qualified endpoint name to its <c>_f{n}</c> factory field index.</summary>
    public Dictionary<string, int> FactoryIndex { get; }

    /// <summary>Fully qualified endpoint name to its group chain, outermost group first.</summary>
    public Dictionary<string, List<string>> GroupChains { get; }

    /// <summary>
    /// Declared groups that no mappable endpoint uses, directly or through a nested group, in
    /// ordinal order. They get no <c>MapGroup</c> call.
    /// </summary>
    public List<string> UnjoinedGroups { get; }

    /// <summary>Groups that are nested inside themselves, so have no outermost prefix.</summary>
    public HashSet<string> CyclicGroups { get; }

    public static EndpointMappingPlan From(EndpointModel model)
    {
        var declared = model.Endpoints
            .GroupBy(endpoint => endpoint.FullyQualifiedName, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(endpoint => endpoint.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();

        var groups = model.Groups
            .GroupBy(group => group.FullyQualifiedName, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(group => group.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();

        var parentOf = groups
            .ToDictionary(group => group.FullyQualifiedName, group => group.ParentGroupFqn, StringComparer.Ordinal);

        var cyclic = FindCyclicGroups(parentOf);

        // A group inside a cycle has no single prefix. It is left out of the tree rather than
        // quietly re-rooted at the top: a working route at the wrong URL is harder to notice than a
        // missing one, and MPEP005 says what happened. (A group with two parents used to be the
        // other unusable shape; [MemberOf<T>] with AllowMultiple = false makes it CS0579 instead.)
        var unusable = new HashSet<string>(cyclic, StringComparer.Ordinal);

        var mappable = declared
            .Where(endpoint => endpoint.GroupTypeFqn is null || !unusable.Contains(endpoint.GroupTypeFqn))
            .ToList();

        // Only groups something actually needs get mapped: the groups endpoints join, plus every
        // ancestor of those. A declared group nobody references would otherwise produce a MapGroup
        // call with nothing in it.
        var needed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in mappable)
        {
            for (var current = endpoint.GroupTypeFqn; current is not null && needed.Add(current);)
                current = parentOf.TryGetValue(current, out var parent) ? parent : null;
        }
        needed.RemoveWhere(unusable.Contains);

        // The set difference MPEP016 reports. A cyclic group is excluded: it already has MPEP005,
        // which is the real reason nothing maps through it.
        var unjoined = groups
            .Select(group => group.FullyQualifiedName)
            .Where(fqn => !needed.Contains(fqn) && !unusable.Contains(fqn))
            .ToList();

        var childGroups = needed
            .Where(fqn => parentOf.TryGetValue(fqn, out var parent) && parent is not null && needed.Contains(parent))
            .GroupBy(fqn => parentOf[fqn]!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(fqn => fqn, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        var rootGroups = needed
            .Where(fqn => !parentOf.TryGetValue(fqn, out var parent) || parent is null || !needed.Contains(parent))
            .OrderBy(fqn => fqn, StringComparer.Ordinal)
            .ToList();

        var endpointsByGroup = mappable
            .Where(endpoint => endpoint.GroupTypeFqn is not null)
            .GroupBy(endpoint => endpoint.GroupTypeFqn!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var factoryIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < mappable.Count; i++)
            factoryIndex[mappable[i].FullyQualifiedName] = i;

        var groupChains = mappable.ToDictionary(
            endpoint => endpoint.FullyQualifiedName,
            endpoint => ChainOf(endpoint.GroupTypeFqn, parentOf),
            StringComparer.Ordinal);

        // Composed once here rather than per diagnostic: the route template is parsed and compared
        // by several consumers, and the framework's own route analyzer has a documented
        // 1.5-minute execution-time defect (dotnet/aspnetcore#53899) from re-parsing.
        var prefixOf = groups.ToDictionary(
            group => group.FullyQualifiedName,
            group => group.Prefix,
            StringComparer.Ordinal);

        var composedRoutes = mappable.ToDictionary(
            endpoint => endpoint.FullyQualifiedName,
            endpoint => ComposedRoute.Compose(
                groupChains[endpoint.FullyQualifiedName]
                    .Select(fqn => prefixOf.TryGetValue(fqn, out var prefix) ? prefix : null)
                    .ToList(),
                endpoint.Route),
            StringComparer.Ordinal);

        // Ordinal, like the framework's own name lookup and like C# method names — the endpoint name
        // is both. The first endpoint in plan order keeps the name.
        var firstWithName = new Dictionary<string, EndpointInfo>(StringComparer.Ordinal);
        var duplicateNames = new Dictionary<string, EndpointInfo>(StringComparer.Ordinal);
        foreach (var endpoint in mappable)
        {
            if (firstWithName.TryGetValue(endpoint.EffectiveDescriptorName, out var earlier))
                duplicateNames[endpoint.FullyQualifiedName] = earlier;
            else
                firstWithName[endpoint.EffectiveDescriptorName] = endpoint;
        }

        return new EndpointMappingPlan(
            declared, mappable, groups, rootGroups, childGroups, endpointsByGroup,
            factoryIndex, groupChains, cyclic, composedRoutes, unjoined, duplicateNames);
    }

    /// <summary>
    /// Mappable endpoints whose effective name an earlier endpoint in plan order already has, keyed
    /// by fully qualified name, to that earlier endpoint. MPEP012 is reported for each.
    /// </summary>
    /// <remarks>
    /// These endpoints are still mapped, but without <c>WithName</c> and without a typed link. The
    /// build already fails on MPEP012; the point is what happens if a consumer demotes it. Naming both
    /// would compile and then throw on the first request, and two link methods with one name in one
    /// class would bury MPEP012 under a CS0111 in a file the consumer cannot edit.
    /// </remarks>
    public Dictionary<string, EndpointInfo> DuplicateNames { get; }

    /// <summary>True when the endpoint is mapped with <c>WithName</c> — every mappable endpoint but an MPEP012 duplicate.</summary>
    public bool IsNamed(EndpointInfo endpoint) => !DuplicateNames.ContainsKey(endpoint.FullyQualifiedName);

    private static List<string> ChainOf(string? groupFqn, Dictionary<string, string?> parentOf)
    {
        var chain = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        for (var current = groupFqn; current is not null && visited.Add(current);)
        {
            chain.Add(current);
            current = parentOf.TryGetValue(current, out var parent) ? parent : null;
        }

        chain.Reverse();
        return chain;
    }

    private static HashSet<string> FindCyclicGroups(Dictionary<string, string?> parentOf)
    {
        var cyclic = new HashSet<string>(StringComparer.Ordinal);

        foreach (var start in parentOf.Keys)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { start };

            for (var current = parentOf[start]; current is not null;)
            {
                if (!visited.Add(current))
                {
                    cyclic.Add(start);
                    break;
                }

                current = parentOf.TryGetValue(current, out var parent) ? parent : null;
            }
        }

        return cyclic;
    }
}
