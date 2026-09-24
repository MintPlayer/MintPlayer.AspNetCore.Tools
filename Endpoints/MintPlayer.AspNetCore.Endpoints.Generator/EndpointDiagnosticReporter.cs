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
        }

        foreach (var group in plan.Groups)
        {
            var location = group.Location.ToLocation(compilation);

            if (plan.CyclicGroups.Contains(group.FullyQualifiedName))
                yield return DiagnosticDescriptors.GroupNestingIsCyclic.Create(location, ShortNameOf(group.FullyQualifiedName));
        }

        if (model.Assembly.MethodNameWasSanitised)
            yield return DiagnosticDescriptors.MappingMethodNameWasSanitised.Create(
                Location.None,
                model.Assembly.GetMethodName(),
                model.Assembly.RequestedMethodName);
    }

    private static string ShortNameOf(string fullyQualifiedName)
    {
        var lastDot = fullyQualifiedName.LastIndexOf('.');
        return lastDot < 0 ? fullyQualifiedName : fullyQualifiedName.Substring(lastDot + 1);
    }
}
