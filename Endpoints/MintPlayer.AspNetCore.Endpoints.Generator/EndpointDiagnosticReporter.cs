using System.Collections.Immutable;
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
/// <para>
/// An <see cref="IConditionalDiagnosticReporter"/>: <see cref="HasDiagnostics"/> runs the same checks
/// without a compilation (every location is left null), so a project with nothing to report keeps
/// this reporter out of the per-compilation combine altogether. One code path decides both
/// answers, so they cannot disagree.
/// </para>
/// </remarks>
internal sealed class EndpointDiagnosticReporter(EndpointModel model) : IConditionalDiagnosticReporter
{
    private bool? hasDiagnostics;

    /// <inheritdoc />
    public bool HasDiagnostics => hasDiagnostics ??= Collect(null).Any();

    public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation) => Collect(compilation);

    /// <summary>A stored location as an in-tree one, or null when only counting.</summary>
    private static Location? Locate(LocationKey? key, Compilation? compilation)
        => compilation is null ? null : key.ToLocation(compilation);

    private IEnumerable<Diagnostic> Collect(Compilation? compilation)
    {
        var plan = EndpointMappingPlan.From(model);

        // Open endpoints this compilation closes itself are mapped here, so MPEP025 would be wrong.
        var closedHere = new HashSet<string>(
            model.Closing.Endpoints.Where(endpoint => endpoint.Closed is { FromReference: false }).Select(endpoint => endpoint.Closed!.OpenFullyQualifiedName),
            StringComparer.Ordinal);

        foreach (var endpoint in plan.DeclaredEndpoints)
        {
            var location = Locate(endpoint.Location, compilation);

            if (endpoint.InaccessibleReason is { } whyEndpoint)
                yield return DiagnosticDescriptors.TypeNotAccessibleToGeneratedCode.Create(location, "Endpoint class", endpoint.ClassName, whyEndpoint);

            if (endpoint.Open is { } open && !closedHere.Contains(endpoint.FullyQualifiedName))
                yield return DiagnosticDescriptors.OpenEndpointNotMapped.Create(location, open.DisplayName);

            if (endpoint.HasIgnoredNewPath)
                yield return DiagnosticDescriptors.NewStaticPathIgnored.Create(location, endpoint.ClassName, "Path",
                    endpoint.Route is { } route ? $"'{route}'" : "a Path not known at compile time");

            if (endpoint.HasIgnoredNewMethods)
                yield return DiagnosticDescriptors.NewStaticPathIgnored.Create(location, endpoint.ClassName, "Methods",
                    endpoint.KnownMethods is { } known ? string.Join(", ", MethodsLiteral.Decode(known)) : "verbs not known at compile time");

            foreach (var property in endpoint.BoundProperties)
            {
                var source = property.Source == BoundSource.Route ? "route" : "query string";
                var propertyLocation = Locate(property.Location, compilation);

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
            if (plan.CyclicGroups.Contains(group.FullyQualifiedName))
                yield return DiagnosticDescriptors.GroupNestingIsCyclic.Create(Locate(group.Location, compilation), ShortNameOf(group.FullyQualifiedName));
        }

        // On the declarations, which is where the fix goes — an open group's too, although only its
        // constructions are in the plan (a construction of an inaccessible declaration is inaccessible).
        foreach (var group in plan.DeclaredGroups)
        {
            if (group.InaccessibleReason is { } whyGroup)
                yield return DiagnosticDescriptors.TypeNotAccessibleToGeneratedCode.Create(Locate(group.Location, compilation), "Endpoint group", ShortNameOf(group.FullyQualifiedName), whyGroup);
        }

        foreach (var groupFqn in plan.UnjoinedGroups)
        {
            var group = plan.Groups.First(candidate => candidate.FullyQualifiedName == groupFqn);
            yield return DiagnosticDescriptors.GroupNeverJoined.Create(Locate(group.Location, compilation), ShortNameOf(groupFqn));
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
                Locate(endpoint.Location, compilation),
                endpoint.FullyQualifiedName.Replace("global::", ""),
                endpoint.EffectiveDescriptorName,
                earlier.FullyQualifiedName.Replace("global::", ""));
        }

        // MPEP026-MPEP031 and MPEP033, found by the closing step and located on the application's attribute.
        foreach (var problem in model.Closing.Problems)
        {
            if (DescriptorFor(problem.Id) is { } descriptor)
                yield return descriptor.Create(Locate(problem.Location, compilation), problem.Arguments.Cast<object>().ToArray());
        }

        if (model.Assembly.CanMapEndpoints && model.Assembly.MethodNameWasSanitised)
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
    private static IEnumerable<Diagnostic> RouteDiagnostics(EndpointMappingPlan plan, Compilation? compilation)
    {
        foreach (var endpoint in plan.DeclaredEndpoints)
        {
            var location = Locate(endpoint.Location, compilation);

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
            var location = Locate(endpoint.Location, compilation);
            var composed = plan.ComposedRoutes[endpoint.FullyQualifiedName];
            if (composed is null) continue;

            // MPEP009 — checked against the composed route, so a parameter contributed by a group
            // prefix counts as present. Like MPEP010, it is about the declaration, so it is skipped
            // for a closed construction (issue #34): those diagnostics belong to the declaring assembly.
            var tokens = ComposedRoute.Parameters(composed);
            var isDeclaration = endpoint.Closed is null;
            foreach (var property in isDeclaration ? endpoint.BoundProperties : ImmutableArray<BoundProperty>.Empty)
            {
                if (property.Source != BoundSource.Route) continue;
                if (tokens.Contains(property.Key, StringComparer.OrdinalIgnoreCase)) continue;

                var available = tokens.Count == 0
                    ? "; it has no route parameters"
                    : "; its parameters are " + string.Join(", ", tokens.Select(token => "'{" + token + "}'"));
                yield return DiagnosticDescriptors.BoundPropertyNotInRoute.Create(
                    Locate(property.Location, compilation), property.Name, property.Key, composed, endpoint.ClassName, available);
            }

            // MPEP010 — the endpoint's own Path starts with the prefix its chain already supplies.
            var chain = plan.GroupChains[endpoint.FullyQualifiedName];
            if (isDeclaration && endpoint.Route is not null && chain.Count > 0)
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
                        NameOf(bucket[later].Endpoint),
                        string.Join(", ", shared),
                        bucket[later].ComposedRoute,
                        NameOf(bucket[earlier].Endpoint));
                }
            }
        }
    }

    private static DiagnosticDescriptor? DescriptorFor(string id) => id switch
    {
        "MPEP026" => DiagnosticDescriptors.TypeArgumentViolatesConstraint,
        "MPEP027" => DiagnosticDescriptors.TypeParameterBoundTwice,
        "MPEP028" => DiagnosticDescriptors.ExplicitTypeArgumentCountMismatch,
        "MPEP029" => DiagnosticDescriptors.ClosedEndpointNotAccessible,
        "MPEP030" => DiagnosticDescriptors.TypeArgumentClosesNothing,
        "MPEP031" => DiagnosticDescriptors.EndpointPartiallyBound,
        "MPEP033" => DiagnosticDescriptors.ConstraintDependsOnTypeParameter,
        _ => null,
    };

    /// <summary>
    /// How a diagnostic names an endpoint: its class name, or for a closed construction its name,
    /// since two closings of one class share the class name (<c>Echo_String</c>, <c>Echo_Int32</c>).
    /// </summary>
    private static string NameOf(EndpointInfo endpoint) =>
        endpoint.Closed is null ? endpoint.ClassName : endpoint.EffectiveDescriptorName;

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
