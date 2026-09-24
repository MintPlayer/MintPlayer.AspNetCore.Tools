using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Explains every case where the generator cannot emit what the consumer asked for.
/// </summary>
/// <remarks>
/// All of these conditions were already computed and used only to skip emission silently, which left
/// the consumer with a bare CS0115 or CS0263 in their own file — or, for an ambiguous group, with
/// nothing at all. The declared descriptors existed and were referenced from nowhere.
/// </remarks>
internal sealed class EndpointDiagnosticReporter(EndpointModel model) : IDiagnosticReporter
{
    public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
    {
        var plan = EndpointMappingPlan.From(model);

        foreach (var endpoint in plan.DeclaredEndpoints)
        {
            var location = endpoint.Location.ToLocation(compilation);

            foreach (var property in endpoint.BoundProperties)
            {
                var source = property.Source == BoundSource.Route ? "route" : "query string";
                var propertyLocation = property.Location.ToLocation(compilation);

                if (property.Kind == BoundKind.Unsupported)
                    yield return DiagnosticDescriptors.BoundPropertyTypeUnsupported.Create(propertyLocation, property.Name, source, property.DeclaredTypeDisplay);
                else if (!property.IsSettable)
                    yield return DiagnosticDescriptors.BoundPropertyNotSettable.Create(propertyLocation, property.Name, source);
            }

            if (endpoint.PathSpec is { AllPartial: false } pathSpec)
            {
                var offender = pathSpec.Parents.FirstOrDefault(parent => !parent.IsPartial)?.Name ?? "?";
                yield return DiagnosticDescriptors.ContainingTypeNotPartial.Create(location, endpoint.ClassName, offender);
            }

            // A typed endpoint with no base class of its own already gets MPEP001 for a missing
            // 'partial', which is the same fix; reporting MPEP014 as well would say it twice.
            var mpep001Applies = endpoint.Level != EndpointLevel.Raw && !endpoint.HasExistingBaseClass;
            if (!endpoint.IsPartial && endpoint.BoundProperties.Length > 0 && !mpep001Applies)
                yield return DiagnosticDescriptors.BoundPropertiesNeedPartial.Create(location, endpoint.ClassName);

            if (endpoint.Level == EndpointLevel.Raw)
                continue;

            if (endpoint.HasExistingBaseClass)
            {
                // A base class that already derives from one of the library's endpoint bases is the
                // supported way to share endpoint behaviour. Nothing is missing, so nothing is
                // reported — and MPEP001 must not fire either: the class does not need to be partial
                // when there is no base clause left to add.
                if (!endpoint.BaseChainReachesEndpointBase)
                    yield return DiagnosticDescriptors.EndpointHasBaseClassConflict.Create(location, endpoint.ClassName);
            }
            else if (!endpoint.IsPartial)
            {
                yield return DiagnosticDescriptors.EndpointMustBePartial.Create(location, endpoint.ClassName);
            }

            // MPEP015 — skipped under MPEP002, where the endpoint does not run through the library's
            // base class at all and a validation warning would bury the real error.
            var runsThroughEndpointBase = !endpoint.HasExistingBaseClass || endpoint.BaseChainReachesEndpointBase;
            if (endpoint.ValidationGap != RequestValidationGap.None && runsThroughEndpointBase && endpoint.RequestTypeFqn is { } requestFqn)
            {
                var reason = endpoint.ValidationGap switch
                {
                    RequestValidationGap.ValidatableObject => "implements IValidatableObject",
                    RequestValidationGap.ValidationAttributes => "carries validation attributes",
                    _ => "carries validation attributes and implements IValidatableObject",
                };
                yield return DiagnosticDescriptors.RequestNotValidatable.Create(location, requestFqn.Replace("global::", ""), endpoint.ClassName, reason);
            }
        }

        foreach (var group in plan.Groups)
        {
            var location = group.Location.ToLocation(compilation);

            if (plan.CyclicGroups.Contains(group.FullyQualifiedName))
                yield return DiagnosticDescriptors.GroupNestingIsCyclic.Create(location, ShortNameOf(group.FullyQualifiedName));
        }

        foreach (var groupFqn in plan.UnjoinedGroups)
        {
            var group = plan.Groups.First(candidate => candidate.FullyQualifiedName == groupFqn);
            yield return DiagnosticDescriptors.GroupNeverJoined.Create(group.Location.ToLocation(compilation), ShortNameOf(groupFqn));
        }

        foreach (var diagnostic in RouteDiagnostics(plan, compilation))
            yield return diagnostic;

        // MPEP012 — on the later endpoint in plan order, naming the earlier one. Fully qualified in
        // the message, because the common cause is the same class name in two namespaces, where the
        // bare names would read "GetUser has the name of GetUser".
        foreach (var endpoint in plan.MappableEndpoints)
        {
            if (!plan.DuplicateNames.TryGetValue(endpoint.FullyQualifiedName, out var earlier)) continue;

            yield return DiagnosticDescriptors.DuplicateEndpointName.Create(
                endpoint.Location.ToLocation(compilation),
                endpoint.FullyQualifiedName.Replace("global::", ""),
                endpoint.EffectiveDescriptorName,
                earlier.FullyQualifiedName.Replace("global::", ""));
        }

        if (model.Assembly.MethodNameWasSanitised)
            yield return DiagnosticDescriptors.MappingMethodNameWasSanitised.Create(
                Location.None,
                model.Assembly.GetMethodName(),
                model.Assembly.RequestedMethodName);
    }

    /// <summary>
    /// MPEP007-MPEP011 and MPEP018: everything that needs the route, the verbs or the type arguments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Opportunistic throughout.</b> A null route or prefix means "could not be recovered", and
    /// every check that needs it is skipped rather than run against a guess (R3.2). Only MPEP011
    /// speaks about that, at Info.
    /// </para>
    /// <para>
    /// <b>Parsed once.</b> Each endpoint's normalised route, token list and verb set are computed a
    /// single time up front and the duplicate search buckets by normalised route, so the cost is
    /// linear in endpoints plus the size of each collision bucket — not a re-parse per pair
    /// (R3.5; the framework's own route analyzer has a documented 1.5-minute defect from exactly
    /// that, dotnet/aspnetcore#53899).
    /// </para>
    /// <para>
    /// None of these suppresses emission, so none can leave the consumer reading a cascading
    /// <c>CS</c> error instead of the real message (R3.4).
    /// </para>
    /// </remarks>
    private static IEnumerable<Diagnostic> RouteDiagnostics(EndpointMappingPlan plan, Compilation compilation)
    {
        foreach (var endpoint in plan.DeclaredEndpoints)
        {
            var location = endpoint.Location.ToLocation(compilation);

            if (endpoint.Route is null)
            {
                yield return DiagnosticDescriptors.PathNotConstant.Create(location, endpoint.ClassName);
            }
            else if (endpoint.Level != EndpointLevel.Raw)
            {
                // The endpoint's OWN path, not the composed route: a token a group prefix contributes
                // is the group's business, and blaming the endpoint for it would be a false positive.
                var routeKeys = RouteKeysOf(endpoint);
                var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var token in ComposedRoute.Parameters(endpoint.Route))
                {
                    if (routeKeys.Contains(token, StringComparer.OrdinalIgnoreCase) || !reported.Add(token))
                        continue;

                    var known = routeKeys.Count == 0
                        ? ""
                        : "; the endpoint's [RouteParam] properties bind " + string.Join(", ", routeKeys.Select(key => $"'{key}'"));
                    yield return DiagnosticDescriptors.RouteTokenNotBound.Create(location, token, endpoint.ClassName, known);
                }
            }

            if (endpoint.Level == EndpointLevel.ResponseOnly && endpoint.ResponseTypeFqn is { } responseFqn)
            {
                var simpleName = SimpleNameOf(responseFqn);
                if (simpleName.EndsWith("Request", StringComparison.Ordinal) ||
                    simpleName.EndsWith("Body", StringComparison.Ordinal) ||
                    simpleName.EndsWith("Command", StringComparison.Ordinal))
                {
                    var verbInterface = endpoint.HttpMethod switch
                    {
                        HttpMethodKind.Get => "IGetEndpoint",
                        HttpMethodKind.Delete => "IDeleteEndpoint",
                        _ => "IEndpoint",
                    };
                    yield return DiagnosticDescriptors.ResponseTypeLooksLikeRequest.Create(location, endpoint.ClassName, simpleName, verbInterface);
                }
            }
        }

        var prefixOf = plan.Groups.ToDictionary(group => group.FullyQualifiedName, group => group.Prefix, StringComparer.Ordinal);
        var buckets = new Dictionary<string, List<RouteEntry>>(StringComparer.Ordinal);
        var bucketOrder = new List<string>();

        foreach (var endpoint in plan.MappableEndpoints)
        {
            var location = endpoint.Location.ToLocation(compilation);
            var composed = plan.ComposedRoutes[endpoint.FullyQualifiedName];
            if (composed is null) continue;

            // MPEP009 — checked against the composed route, so a parameter contributed by a group
            // prefix counts as present.
            var tokens = ComposedRoute.Parameters(composed);
            foreach (var property in endpoint.BoundProperties)
            {
                if (property.Source != BoundSource.Route) continue;
                if (tokens.Contains(property.Key, StringComparer.OrdinalIgnoreCase)) continue;

                var available = tokens.Count == 0
                    ? "; it has no route parameters"
                    : "; its parameters are " + string.Join(", ", tokens.Select(token => "'{" + token + "}'"));
                yield return DiagnosticDescriptors.BoundPropertyNotInRoute.Create(
                    property.Location.ToLocation(compilation), property.Name, property.Key, composed, endpoint.ClassName, available);
            }

            // MPEP010 — the endpoint's own Path starts with the prefix its chain already supplies.
            var chain = plan.GroupChains[endpoint.FullyQualifiedName];
            if (endpoint.Route is not null && chain.Count > 0)
            {
                var prefix = ComposedRoute.Compose(
                    chain.Select(fqn => prefixOf.TryGetValue(fqn, out var value) ? value : null).ToList(),
                    "");
                if (prefix is not null)
                {
                    var normalisedPrefix = ComposedRoute.Normalise(prefix);
                    var normalisedPath = ComposedRoute.Normalise(endpoint.Route);

                    // At a segment boundary only: a prefix of /api must not claim a path of /apiary.
                    if (normalisedPrefix != "/" &&
                        (normalisedPath == normalisedPrefix || normalisedPath.StartsWith(normalisedPrefix + "/", StringComparison.Ordinal)))
                    {
                        yield return DiagnosticDescriptors.PathRepeatsGroupPrefix.Create(location, endpoint.Route, endpoint.ClassName, prefix, composed);
                    }
                }
            }

            // Unknown verbs are "no conflict", so they never enter a bucket.
            if (endpoint.KnownMethods is { } known)
            {
                var normalised = ComposedRoute.Normalise(composed);
                if (!buckets.TryGetValue(normalised, out var bucket))
                {
                    buckets[normalised] = bucket = new List<RouteEntry>();
                    bucketOrder.Add(normalised);
                }

                bucket.Add(new RouteEntry(endpoint, composed, location, MethodsLiteral.Decode(known)));
            }
        }

        // MPEP007 — once per colliding pair, on the later endpoint in the plan's ordinal order.
        foreach (var key in bucketOrder)
        {
            var bucket = buckets[key];
            for (var later = 1; later < bucket.Count; later++)
            {
                for (var earlier = 0; earlier < later; earlier++)
                {
                    var shared = bucket[later].Verbs.Intersect(bucket[earlier].Verbs, StringComparer.Ordinal).ToList();
                    if (shared.Count == 0) continue;

                    yield return DiagnosticDescriptors.DuplicateRoute.Create(
                        bucket[later].Location,
                        bucket[later].Endpoint.ClassName,
                        string.Join(", ", shared),
                        bucket[later].ComposedRoute,
                        bucket[earlier].Endpoint.ClassName);
                }
            }
        }
    }

    private static List<string> RouteKeysOf(EndpointInfo endpoint) => endpoint.BoundProperties
        .Where(property => property.Source == BoundSource.Route)
        .Select(property => property.Key)
        .ToList();

    /// <summary>
    /// <c>global::Ns.Outer.CreateUserRequest</c> → <c>CreateUserRequest</c>. Generic arguments are
    /// cut first, so <c>List&lt;global::Ns.XRequest&gt;</c> reads as <c>List</c>, not as a request.
    /// </summary>
    private static string SimpleNameOf(string fullyQualifiedName)
    {
        var generic = fullyQualifiedName.IndexOf('<');
        var name = generic < 0 ? fullyQualifiedName : fullyQualifiedName.Substring(0, generic);
        name = ShortNameOf(name.TrimEnd('?', ']', '['));
        var alias = name.LastIndexOf("::", StringComparison.Ordinal);
        return alias < 0 ? name : name.Substring(alias + 2);
    }

    private sealed class RouteEntry(EndpointInfo endpoint, string composedRoute, Location? location, string[] verbs)
    {
        public EndpointInfo Endpoint { get; } = endpoint;
        public string ComposedRoute { get; } = composedRoute;
        public Location? Location { get; } = location;
        public string[] Verbs { get; } = verbs;
    }

    private static string ShortNameOf(string fullyQualifiedName)
    {
        var lastDot = fullyQualifiedName.LastIndexOf('.');
        return lastDot < 0 ? fullyQualifiedName : fullyQualifiedName.Substring(lastDot + 1);
    }
}
