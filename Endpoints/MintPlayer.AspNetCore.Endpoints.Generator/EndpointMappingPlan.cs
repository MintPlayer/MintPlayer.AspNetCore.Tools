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
        HashSet<string> cyclicGroups)
    {
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
            .Where(group => !group.HasMultipleParents)
            .ToDictionary(group => group.FullyQualifiedName, group => group.ParentGroupFqn, StringComparer.Ordinal);

        var cyclic = FindCyclicGroups(parentOf);

        // A group with two parents, or one inside a cycle, has no single prefix. It is left out of
        // the tree rather than quietly re-rooted at the top: a working route at the wrong URL is
        // harder to notice than a missing one, and MPEP004/MPEP005 say what happened.
        var unusable = new HashSet<string>(
            groups.Where(group => group.HasMultipleParents).Select(group => group.FullyQualifiedName),
            StringComparer.Ordinal);
        unusable.UnionWith(cyclic);

        var mappable = declared
            .Where(endpoint => !endpoint.HasMultipleGroups)
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

        return new EndpointMappingPlan(
            declared, mappable, groups, rootGroups, childGroups, endpointsByGroup,
            factoryIndex, groupChains, cyclic);
    }

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
