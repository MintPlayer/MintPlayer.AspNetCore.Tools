using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Closes open-generic endpoints with the application's <c>[assembly: EndpointTypeArgument]</c>
/// attributes (issue #34, PRD D1–D4, D3a), producing ordinary endpoints for the mapping plan.
/// </summary>
/// <remarks>
/// <para>
/// <b>Binding (D2).</b> A constraint-keyed attribute binds a type parameter when one of the
/// parameter's constraint types equals its <c>TConstraint</c>. An endpoint is closed only when every
/// type parameter, containing types' included, is bound. The explicit form binds one endpoint and
/// wins over constraint keys for it. A violated constraint, two attributes on one parameter and an
/// explicit arity mismatch are errors on the attribute; nothing is emitted for the endpoint, so none
/// of them can surface as a compile error in generated code.
/// </para>
/// <para>
/// <b>Open endpoints come from two places.</b> This compilation's own (found by metadata name, since
/// the syntax pipeline already knows them) and referenced assemblies' records (D3a). A constructed
/// symbol from source hands back its definition's syntax, so everything is read as usual; one from
/// metadata has no syntax, so its route, verbs and binder come from the record.
/// </para>
/// <para>
/// <b>Closed endpoints are not declarations here.</b> They get no partial, and the per-declaration
/// diagnostics never run on them: those belong to the declaring assembly. Their diagnostics are
/// located on the attribute that closed them.
/// </para>
/// </remarks>
internal static class EndpointClosing
{
    private const string EndpointsNamespace = "MintPlayer.AspNetCore.Endpoints";
    private const string AttributeName = "EndpointTypeArgumentAttribute";

    private sealed class KeyedArgument(AttributeData attribute, ITypeSymbol constraint, ITypeSymbol argument)
    {
        public AttributeData Attribute { get; } = attribute;
        public ITypeSymbol Constraint { get; } = constraint;
        public ITypeSymbol Argument { get; } = argument;
        public string Display => $"EndpointTypeArgument<{Constraint.ToDisplayString()}, {Argument.ToDisplayString()}>";
    }

    private sealed class ExplicitArgument(AttributeData attribute, INamedTypeSymbol? target, ImmutableArray<ITypeSymbol> arguments)
    {
        public AttributeData Attribute { get; } = attribute;
        public INamedTypeSymbol? Target { get; } = target;
        public ImmutableArray<ITypeSymbol> Arguments { get; } = arguments;
        public string Display => $"EndpointTypeArgument(typeof({Target?.ToDisplayString() ?? "?"}), ...)";
    }

    /// <summary>An open endpoint and the compile-time facts about it that metadata may not carry.</summary>
    private sealed class OpenCandidate(INamedTypeSymbol definition, bool fromReference, string? route, string? methods, bool hasBinder)
    {
        public INamedTypeSymbol Definition { get; } = definition;
        public bool FromReference { get; } = fromReference;
        public string? Route { get; } = route;
        public string? Methods { get; } = methods;
        public bool HasBinder { get; } = hasBinder;
        public string Display => Definition.ToDisplayString();
    }

    public static ClosingModel Build(Compilation compilation, ImmutableArray<string> ownOpenEndpoints, CancellationToken ct)
    {
        var keyed = new List<KeyedArgument>();
        var explicitForms = new List<ExplicitArgument>();

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null || attributeClass.Name != AttributeName ||
                attributeClass.ContainingNamespace?.ToDisplayString() != EndpointsNamespace)
                continue;

            // An argument the compiler cannot resolve already has its own error (CS0246); matching or
            // reporting on it would only add noise — and in the IDE, references can be missing briefly.
            if (attributeClass.IsGenericType && attributeClass.TypeArguments.Any(argument => argument.TypeKind == TypeKind.Error))
                continue;

            if (attributeClass.IsGenericType && attributeClass.TypeArguments.Length == 2)
            {
                keyed.Add(new KeyedArgument(attribute, attributeClass.TypeArguments[0], attributeClass.TypeArguments[1]));
            }
            else if (!attributeClass.IsGenericType && attribute.ConstructorArguments.Length == 2)
            {
                var target = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
                if (target is null || target.TypeKind == TypeKind.Error) continue;
                var arguments = attribute.ConstructorArguments[1] is { Kind: TypedConstantKind.Array, Values.IsDefault: false } array
                    ? array.Values.Select(value => value.Value as ITypeSymbol).Where(type => type is not null).Select(type => type!).ToImmutableArray()
                    : ImmutableArray<ITypeSymbol>.Empty;
                explicitForms.Add(new ExplicitArgument(attribute, target, arguments));
            }
        }

        // PRD D3: nothing to close, nothing read.
        if (keyed.Count == 0 && explicitForms.Count == 0) return ClosingModel.Empty;

        var candidates = new List<OpenCandidate>();
        foreach (var metadataName in ownOpenEndpoints)
        {
            ct.ThrowIfCancellationRequested();
            if (compilation.Assembly.GetTypeByMetadataName(metadataName) is not { IsAbstract: false } definition) continue;

            var declared = EndpointGenerator.DescribeDeclaredEndpoint(definition, compilation, ct);
            candidates.Add(new OpenCandidate(definition, false, declared.Route, declared.KnownMethods, ShadowParameters.EmitsBinder(declared)));
        }

        var groupPrefixes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (assembly, library) in OpenEndpointRecords.Read(compilation, ct))
        {
            foreach (var group in library.GroupPrefixes)
                groupPrefixes[group.Key] = group.Value;

            foreach (var record in library.Endpoints)
            {
                if (assembly.GetTypeByMetadataName(record.MetadataName) is not { } definition) continue;
                candidates.Add(new OpenCandidate(definition, true, record.Path, record.Methods, record.HasBinder));
            }
        }

        var used = new HashSet<AttributeData>();
        var problems = new List<ClosingProblem>();
        var endpoints = new List<EndpointInfo>();
        var groups = new List<GroupInfo>();

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            Close(candidate, keyed, explicitForms, groupPrefixes, compilation, used, problems, endpoints, groups, ct);
        }

        foreach (var form in keyed)
        {
            if (used.Contains(form.Attribute)) continue;
            problems.Add(Problem(DiagnosticDescriptors.TypeArgumentClosesNothing, form.Attribute,
                form.Display, $"no open endpoint has a type parameter constrained to '{form.Constraint.ToDisplayString()}'"));
        }

        foreach (var form in explicitForms)
        {
            if (used.Contains(form.Attribute)) continue;
            problems.Add(Problem(DiagnosticDescriptors.TypeArgumentClosesNothing, form.Attribute,
                form.Display, $"'{form.Target?.ToDisplayString() ?? "?"}' is not an open-generic endpoint of this compilation or of a referenced assembly that records its open endpoints"));
        }

        return new ClosingModel(
            endpoints
                .GroupBy(endpoint => endpoint.FullyQualifiedName, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(endpoint => endpoint.FullyQualifiedName, StringComparer.Ordinal)
                .ToImmutableArray(),
            groups
                .GroupBy(group => group.FullyQualifiedName, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(group => group.FullyQualifiedName, StringComparer.Ordinal)
                .ToImmutableArray(),
            problems.ToImmutableArray());
    }

    private static void Close(
        OpenCandidate candidate,
        List<KeyedArgument> keyed,
        List<ExplicitArgument> explicitForms,
        Dictionary<string, string?> groupPrefixes,
        Compilation compilation,
        HashSet<AttributeData> used,
        List<ClosingProblem> problems,
        List<EndpointInfo> endpoints,
        List<GroupInfo> groups,
        CancellationToken ct)
    {
        var definition = candidate.Definition;
        var parameters = GenericTypes.AllTypeParameters(definition);
        var closings = new List<(ITypeSymbol[] Arguments, AttributeData[] Sources)>();

        var explicitForThis = explicitForms
            .Where(form => form.Target is { } target && SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, definition.OriginalDefinition))
            .ToList();

        if (explicitForThis.Count > 0)
        {
            foreach (var form in explicitForThis)
            {
                used.Add(form.Attribute);
                if (form.Arguments.Length != parameters.Count)
                {
                    problems.Add(Problem(DiagnosticDescriptors.ExplicitTypeArgumentCountMismatch, form.Attribute,
                        definition.ToDisplayString(), form.Arguments.Length.ToString(), parameters.Count.ToString()));
                    continue;
                }

                closings.Add((form.Arguments.ToArray(), Enumerable.Repeat(form.Attribute, parameters.Count).ToArray()));
            }
        }
        else
        {
            var arguments = new ITypeSymbol?[parameters.Count];
            var sources = new AttributeData?[parameters.Count];
            var conflict = false;

            for (var i = 0; i < parameters.Count; i++)
            {
                var matches = keyed
                    .Where(form => parameters[i].ConstraintTypes.Any(constraint => SymbolEqualityComparer.Default.Equals(constraint, form.Constraint)))
                    .ToList();

                foreach (var match in matches) used.Add(match.Attribute);

                if (matches.Count > 1)
                {
                    problems.Add(Problem(DiagnosticDescriptors.TypeParameterBoundTwice, matches[1].Attribute,
                        parameters[i].Name, candidate.Display, matches[0].Display, matches[1].Display));
                    conflict = true;
                }
                else if (matches.Count == 1)
                {
                    arguments[i] = matches[0].Argument;
                    sources[i] = matches[0].Attribute;
                }
            }

            if (conflict) return;

            var bound = arguments.Count(argument => argument is not null);
            if (bound == 0) return;   // Not this application's to close.

            if (bound < parameters.Count)
            {
                var unbound = parameters.Where((_, index) => arguments[index] is null).Select(parameter => $"'{parameter.Name}'");
                problems.Add(Problem(DiagnosticDescriptors.EndpointPartiallyBound, sources.First(source => source is not null)!,
                    candidate.Display, string.Join(", ", unbound)));
                return;
            }

            closings.Add((arguments.Select(argument => argument!).ToArray(), sources.Select(source => source!).ToArray()));
        }

        foreach (var (arguments, sources) in closings)
        {
            if (Describe(candidate, parameters, arguments, sources, groupPrefixes, compilation, problems, groups, ct) is { } endpoint)
                endpoints.Add(endpoint);
        }
    }

    private static EndpointInfo? Describe(
        OpenCandidate candidate,
        List<ITypeParameterSymbol> parameters,
        ITypeSymbol[] arguments,
        AttributeData[] sources,
        Dictionary<string, string?> groupPrefixes,
        Compilation compilation,
        List<ClosingProblem> problems,
        List<GroupInfo> groups,
        CancellationToken ct)
    {
        var definition = candidate.Definition;

        var map = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        for (var i = 0; i < parameters.Count; i++) map[parameters[i]] = arguments[i];

        for (var i = 0; i < parameters.Count; i++)
        {
            if (Violation(parameters[i], arguments[i], map, compilation) is { } constraint)
            {
                problems.Add(Problem(DiagnosticDescriptors.TypeArgumentViolatesConstraint, sources[i],
                    arguments[i].ToDisplayString(), constraint, parameters[i].Name, candidate.Display));
                return null;
            }
        }

        if (candidate.FromReference && !IsPublic(definition))
        {
            problems.Add(Problem(DiagnosticDescriptors.ClosedEndpointNotAccessible, sources[0],
                candidate.Display, $"it is not public in '{definition.ContainingAssembly?.Name}'"));
            return null;
        }

        // An endpoint of this compilation that generated code cannot name already has MPEP024.
        if (!candidate.FromReference && GeneratedCodeAccess.WhyInaccessible(definition) is not null) return null;

        for (var i = 0; i < arguments.Length; i++)
        {
            if (WhyArgumentInaccessible(arguments[i]) is { } why)
            {
                problems.Add(Problem(DiagnosticDescriptors.ClosedEndpointNotAccessible, sources[i],
                    candidate.Display, $"its type argument '{arguments[i].ToDisplayString()}' {why}"));
                return null;
            }
        }

        var constructed = GenericTypes.Construct(definition, arguments);

        // The group chain, substituted through the construction, and the groups on it that no
        // declaration of this compilation describes: a library's (from its records) and constructions.
        var chainGroups = new List<GroupInfo>();
        var groupSymbol = GroupMembership.ResolveSymbol(constructed, EndpointsNamespace);
        if (groupSymbol is not null)
            groupSymbol = GenericTypes.Substitute(groupSymbol, GenericTypes.MapOf(constructed), compilation) as INamedTypeSymbol;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var current = groupSymbol; current is not null;)
        {
            var fqn = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!visited.Add(fqn)) break;

            var parent = EndpointGenerator.ParentGroupOf(current, compilation);
            var fromMetadata = !current.Locations.Any(location => location.IsInSource);

            if (fromMetadata)
            {
                if (!IsPublic(current))
                {
                    problems.Add(Problem(DiagnosticDescriptors.ClosedEndpointNotAccessible, sources[0],
                        candidate.Display, $"its group '{current.ToDisplayString()}' is not public in '{current.ContainingAssembly?.Name}'"));
                    return null;
                }

                groupPrefixes.TryGetValue(current.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), out var prefix);
                chainGroups.Add(EndpointGenerator.DescribeGroup(current, parent, compilation, prefix, useRecordedPrefix: true, ct));
            }
            else if (EndpointGenerator.IsConstruction(current))
            {
                chainGroups.Add(EndpointGenerator.DescribeGroup(current, parent, compilation, null, useRecordedPrefix: false, ct));
            }

            current = parent;
        }

        groups.AddRange(chainGroups);

        var shape = EndpointGenerator.ShapeOf(constructed);
        var attributeLocation = LocationOf(sources[0]);

        return new EndpointInfo(
            constructed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            definition.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "",
            definition.Name,
            // No partial is emitted for a closed endpoint; IsPartial carries "the declaring assembly
            // emitted a binder", which is what ShadowParameters.EmitsBinder asks (with no base class
            // and no path spec to veto it).
            isPartial: candidate.HasBinder,
            hasExistingBaseClass: false,
            shape.Level, shape.HttpMethod,
            shape.RequestTypeFqn, shape.ResponseTypeFqn,
            groupSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            baseChainReachesEndpointBase: false,
            descriptorName: (EndpointGenerator.GetDescriptorName(definition) ?? definition.Name) + GenericTypes.NameSuffix(arguments),
            location: attributeLocation,
            pathSpec: null,
            route: candidate.Route,
            boundProperties: BoundProperties.Collect(constructed, EndpointsNamespace, ct),
            knownMethods: candidate.Methods,
            closed: new ClosedGenericInfo(definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), candidate.FromReference));
    }

    /// <summary>The first constraint of <paramref name="parameter"/> that <paramref name="argument"/> violates, or null.</summary>
    private static string? Violation(ITypeParameterSymbol parameter, ITypeSymbol argument, Dictionary<ITypeParameterSymbol, ITypeSymbol> map, Compilation compilation)
    {
        var isNullableValue = argument is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

        if (parameter.HasReferenceTypeConstraint && !argument.IsReferenceType) return "class";
        if (parameter.HasUnmanagedTypeConstraint && !argument.IsUnmanagedType) return "unmanaged";
        if (parameter.HasValueTypeConstraint && (!argument.IsValueType || isNullableValue)) return "struct";
        if (parameter.HasNotNullConstraint && isNullableValue) return "notnull";
        if (parameter.HasConstructorConstraint && !HasPublicParameterlessConstructor(argument)) return "new()";

        foreach (var constraint in parameter.ConstraintTypes)
        {
            var substituted = GenericTypes.Substitute(constraint, map, compilation);
            if (!Satisfies(argument, substituted, compilation)) return substituted.ToDisplayString();
        }

        return null;
    }

    /// <summary>The conversions a constraint admits: identity, implicit reference, boxing.</summary>
    private static bool Satisfies(ITypeSymbol argument, ITypeSymbol constraint, Compilation compilation)
    {
        if (compilation is not CSharpCompilation csharp) return true;

        var conversion = csharp.ClassifyConversion(argument, constraint);
        return conversion.IsIdentity || (conversion.IsImplicit && !conversion.IsUserDefined && (conversion.IsReference || conversion.IsBoxing));
    }

    private static bool HasPublicParameterlessConstructor(ITypeSymbol type) => type switch
    {
        { IsValueType: true } => true,
        INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } named =>
            named.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public),
        _ => false,
    };

    /// <summary>Public all the way out. <c>internal</c> does not do for a type of another assembly.</summary>
    private static bool IsPublic(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public) return false;
        }

        return true;
    }

    /// <summary>Why the application's generated code cannot name <paramref name="type"/>, or null.</summary>
    private static string? WhyArgumentInaccessible(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return WhyArgumentInaccessible(array.ElementType);
            case INamedTypeSymbol named:
                if (GeneratedCodeAccess.WhyInaccessible(named) is { } why) return "cannot be named: " + why;
                foreach (var argument in GenericTypes.AllTypeArguments(named))
                {
                    if (WhyArgumentInaccessible(argument) is { } inner) return inner;
                }
                return null;
            default:
                return null;
        }
    }

    private static LocationKey? LocationOf(AttributeData attribute)
        => attribute.ApplicationSyntaxReference is { } reference
            ? Location.Create(reference.SyntaxTree, reference.Span).AsKey()
            : null;

    private static ClosingProblem Problem(DiagnosticDescriptor descriptor, AttributeData attribute, params string[] arguments)
        => new(descriptor.Id, arguments.ToImmutableArray(), LocationOf(attribute));
}
