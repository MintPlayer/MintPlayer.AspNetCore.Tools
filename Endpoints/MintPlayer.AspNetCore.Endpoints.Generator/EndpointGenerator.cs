using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.Models;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>Tracking names for the incremental steps, so the cache can be asserted on.</summary>
internal static class TrackingNames
{
    public const string Endpoints = "Endpoints";
    public const string Groups = "Groups";
    public const string AssemblyInfo = "AssemblyInfo";
    public const string ClosedEndpoints = "ClosedEndpoints";
    public const string Model = "EndpointModel";
}

/// <summary>
/// Discovers endpoint and group classes and emits the assembly's <c>Map…Endpoints()</c> extension
/// method, the partial base-class declarations typed endpoints need, and the descriptor list.
/// </summary>
[Generator(LanguageNames.CSharp)]
public partial class EndpointGenerator : IncrementalGenerator
{
    private const string EndpointsNamespace = "MintPlayer.AspNetCore.Endpoints";

    /// <inheritdoc />
    public override void Initialize(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<Settings> settingsProvider,
        IncrementalValueProvider<ICompilationCache> cacheProvider)
    {
        var endpointsProvider = context.SyntaxProvider
            .CreateSyntaxProvider(IsEndpointCandidate, GetEndpointInfo)
            .Where(static info => info is not null)
            .Select(static (info, _) => info!)
            .Collect()
            .WithComparer(SequenceComparer<EndpointInfo>.Instance)
            .WithTrackingName(TrackingNames.Endpoints);

        var groupsProvider = context.SyntaxProvider
            .CreateSyntaxProvider(IsGroupCandidate, GetGroupInfo)
            .Where(static info => info is not null)
            .Select(static (info, _) => info!)
            .Collect()
            .WithComparer(SequenceComparer<GroupInfo>.Instance)
            .WithTrackingName(TrackingNames.Groups);

        var assemblyInfoProvider = context.CompilationProvider
            .Select(static (compilation, _) => GetAssemblyInfo(compilation))
            .WithTrackingName(TrackingNames.AssemblyInfo);

        // Issue #34: the open endpoints this compilation declares, by metadata name, so the closing
        // step can re-resolve them against each compilation without holding a symbol.
        var openEndpointNamesProvider = endpointsProvider
            .Select(static (endpoints, _) => endpoints
                .Where(endpoint => endpoint.Open is not null)
                .Select(endpoint => endpoint.Open!.MetadataName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToImmutableArray())
            .WithComparer(SequenceComparer<string>.Instance);

        // The one step that reads the Compilation for the model, because closing an endpoint needs
        // symbols: the application's [assembly: EndpointTypeArgument] attributes, the referenced
        // libraries' records, and constructed types. It runs on every compilation and returns a
        // value-equal ClosingModel, so an edit that closes nothing new leaves everything downstream
        // cached. References are read only when the compilation declares an EndpointTypeArgument, and
        // memoised per MetadataReference (PRD D3).
        var closingProvider = context.CompilationProvider
            .Combine(openEndpointNamesProvider)
            .Select(static (pair, cancellationToken) => EndpointClosing.Build(pair.Left, pair.Right, cancellationToken))
            .WithTrackingName(TrackingNames.ClosedEndpoints);

        var modelProvider = endpointsProvider
            .Join(groupsProvider)
            .Join(assemblyInfoProvider)
            .Combine(closingProvider)
            .Select(static (pair, _) => new EndpointModel(pair.Left.Item1, pair.Left.Item2, pair.Left.Item3, pair.Right))
            .WithTrackingName(TrackingNames.Model);

        // One file per producer, each fed from the value-equal model. Since MintPlayer.SourceGenerators.Tools
        // 11.0.0, ProduceCode registers each provider as its own output with no Compilation in the
        // combine (it used to combine every producer with CompilationProvider, which could never be
        // cached). An edit that leaves the model equal leaves each Select cached, the driver hands back
        // the same producer instance, and the output step is skipped.
        //   - EndpointMappingProducer: the Map…Endpoints() extension method.
        //   - EndpointOpenApiProducer: only for a consumer that references Microsoft.AspNetCore.OpenApi;
        //     it writes nothing otherwise, and Producer.Produce adds no source for an empty buffer, so
        //     the absent case is no file at all rather than an empty one.
        //   - EndpointRoutesProducer: typed links (EndpointRoutes.g.cs), from the same plan, so it names
        //     exactly the endpoints the mapping names.
        //   - EndpointContractsProducer: the cross-assembly contract (EndpointContracts.g.cs) a typed
        //     client reads from this assembly's metadata (M9).
        context.ProduceCode(
            modelProvider.Select(static (model, _) => (Producer)new EndpointMappingProducer(model)),
            modelProvider.Select(static (model, _) => (Producer)new EndpointOpenApiProducer(model)),
            modelProvider.Select(static (model, _) => (Producer)new EndpointRoutesProducer(model)),
            modelProvider.Select(static (model, _) => (Producer)new EndpointContractsProducer(model)));

        // Turning a LocationKey back into a Location needs the Compilation, so a reporter with something
        // to say is combined with it and re-runs per compilation. EndpointDiagnosticReporter is an
        // IConditionalDiagnosticReporter: when the model yields no diagnostics, ReportDiagnostics filters
        // it out before that combine, and an edit to a diagnostic-free project runs no reporting step.
        context.ReportDiagnostics(modelProvider
            .Select(static (model, _) => (IDiagnosticReporter)new EndpointDiagnosticReporter(model)));
    }

    /// <summary>
    /// Syntactic pre-filter: any non-abstract class with a base list is a candidate, and the
    /// transform's semantic check (<c>AllInterfaces</c> contains <c>IEndpointBase</c>) decides.
    /// </summary>
    /// <remarks>
    /// This used to match base-list names beginning with an endpoint interface, plus
    /// <c>IMemberOf</c>. That silently missed every endpoint inheriting its verb interface from a
    /// base class of its own: <c>partial class GetUser : UsersEndpointBase&lt;…&gt;</c> names no
    /// endpoint interface, so it never reached the semantic check and was never mapped. It only
    /// ever worked when an <c>IMemberOf&lt;T&gt;</c> happened to sit in the same base list. With
    /// membership now an attribute that inherits through base classes (PRD R1.4), that accident is
    /// gone and the shape would break outright, so the name match is dropped rather than patched.
    /// <para>
    /// The cost is one <c>GetDeclaredSymbol</c> and an interface scan per class with a base list.
    /// Abstract classes are excluded here because the transform would reject them anyway.
    /// </para>
    /// </remarks>
    private static bool IsEndpointCandidate(SyntaxNode node, CancellationToken _) =>
        node is ClassDeclarationSyntax { BaseList: not null } classDecl &&
        !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);

    private static string? NameOf(TypeSyntax type) => type switch
    {
        SimpleNameSyntax simple => simple.Identifier.Text,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        _ => null
    };

    private static EndpointInfo? GetEndpointInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);
        if (symbol is null || symbol.IsAbstract) return null;

        if (!IsEndpoint(symbol))
            return null;

        return DescribeDeclaredEndpoint(symbol, context.SemanticModel.Compilation, ct);
    }

    /// <summary>True when <paramref name="symbol"/> implements <c>IEndpointBase</c>.</summary>
    internal static bool IsEndpoint(INamedTypeSymbol symbol) =>
        symbol.AllInterfaces.Any(i => i.Name == "IEndpointBase" && i.ContainingNamespace?.ToDisplayString() == EndpointsNamespace);

    /// <summary>The level, verb, and request and response types an endpoint's interfaces declare.</summary>
    internal readonly struct EndpointShape
    {
        public EndpointShape(EndpointLevel level, HttpMethodKind httpMethod, string? requestTypeFqn, string? responseTypeFqn, ITypeSymbol? requestType)
        {
            Level = level;
            HttpMethod = httpMethod;
            RequestTypeFqn = requestTypeFqn;
            ResponseTypeFqn = responseTypeFqn;
            RequestType = requestType;
        }

        public EndpointLevel Level { get; }
        public HttpMethodKind HttpMethod { get; }
        public string? RequestTypeFqn { get; }
        public string? ResponseTypeFqn { get; }
        public ITypeSymbol? RequestType { get; }
    }

    /// <summary>
    /// Reads the endpoint's shape from its interfaces. Works on a constructed type too, where the
    /// interfaces come back substituted: <c>Create&lt;AppRequest&gt;</c> has request type <c>AppRequest</c>.
    /// </summary>
    internal static EndpointShape ShapeOf(INamedTypeSymbol symbol)
    {
        // AllInterfaces, not Interfaces: an endpoint that inherits its endpoint interfaces through a
        // base class of its own lists none of them directly, and reading only the direct set silently
        // degrades it to Raw/Custom — losing its verb, its request type and its Produces metadata,
        // even though the semantic gate above (which does use AllInterfaces) let it through.
        INamedTypeSymbol? verbInterface = null;
        INamedTypeSymbol? typedInterface = null;
        var httpMethod = HttpMethodKind.Custom;

        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.ContainingNamespace?.ToDisplayString() != EndpointsNamespace) continue;

            var name = iface.Name;
            var arity = iface.TypeArguments.Length;

            var method = name switch
            {
                "IGetEndpoint" => HttpMethodKind.Get,
                "IPostEndpoint" => HttpMethodKind.Post,
                "IPutEndpoint" => HttpMethodKind.Put,
                "IDeleteEndpoint" => HttpMethodKind.Delete,
                "IPatchEndpoint" => HttpMethodKind.Patch,
                _ => (HttpMethodKind?)null
            };

            // Most derived wins: the highest arity carries the most type information, and among
            // equal arities the ordinally first name keeps the choice reproducible.
            if (method.HasValue)
            {
                if (IsMoreDerived(iface, verbInterface))
                {
                    verbInterface = iface;
                    httpMethod = method.Value;
                }
                continue;
            }

            if (name == "IEndpoint" && IsMoreDerived(iface, typedInterface))
                typedInterface = iface;
        }

        // The verb interfaces derive from IEndpoint<,>/IEndpoint<>, so whichever of the two names the
        // request and response types has the same arguments; prefer the one that has any.
        var carrier =
            (verbInterface?.TypeArguments.Length ?? 0) >= (typedInterface?.TypeArguments.Length ?? 0)
                ? verbInterface ?? typedInterface
                : typedInterface;

        // IGetEndpoint<TResponse> / IDeleteEndpoint<TResponse> carry one type argument that is the
        // RESPONSE. Reading them by arity alone would classify them as Typed and try to bind a body
        // into the response type, so the response-only rung is recognised by its interface first.
        var responseOnly = symbol.AllInterfaces.FirstOrDefault(i =>
            i.Name == "IResponseEndpoint" &&
            i.TypeArguments.Length == 1 &&
            i.ContainingNamespace?.ToDisplayString() == EndpointsNamespace);

        var carrierArity = carrier?.TypeArguments.Length ?? 0;
        var level = carrierArity >= 2 ? EndpointLevel.TypedWithResponse
            : responseOnly is not null ? EndpointLevel.ResponseOnly
            : carrierArity == 1 ? EndpointLevel.Typed
            : EndpointLevel.Raw;

        var requestTypeFqn = level is EndpointLevel.Typed or EndpointLevel.TypedWithResponse
            ? carrier!.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : null;
        var responseTypeFqn = level switch
        {
            EndpointLevel.TypedWithResponse => carrier!.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EndpointLevel.ResponseOnly => responseOnly!.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            _ => null,
        };

        return new EndpointShape(
            level, httpMethod, requestTypeFqn, responseTypeFqn,
            level is EndpointLevel.Typed or EndpointLevel.TypedWithResponse ? carrier!.TypeArguments[0] : null);
    }

    /// <summary>Describes an endpoint declared in this compilation, exactly as the syntax pipeline does.</summary>
    internal static EndpointInfo DescribeDeclaredEndpoint(INamedTypeSymbol symbol, Compilation compilation, CancellationToken ct)
    {
        var shape = ShapeOf(symbol);
        var level = shape.Level;
        var httpMethod = shape.HttpMethod;

        // Both of these are properties of the *symbol*, not of one declaration. Reading them from
        // the single ClassDeclarationSyntax that triggered this callback makes a partial class split
        // across files produce two contradictory infos, and GroupBy().First() then keeps whichever
        // file the compiler happened to hand over first.
        var isPartial = symbol.DeclaringSyntaxReferences.Length > 0 && symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(ct))
            .OfType<ClassDeclarationSyntax>()
            .All(declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

        // Any base class at all blocks emission — a second base clause on the partial is a CS0263
        // whether or not the existing one is useful. Whether that is an *error* depends on something
        // else: if the chain already reaches one of the library's endpoint bases, the user has
        // supplied what the generator would have, and there is nothing to report.
        var hasExistingBaseClass =
            symbol.BaseType is { SpecialType: not SpecialType.System_Object };
        var baseChainReachesEndpointBase = ReachesEndpointBase(symbol.BaseType);

        // The route the runtime uses: the interface implementation, not merely the nearest Path
        // (PRD D7). They differ only under a 'new static Path', which MPEP032 reports.
        var route = RouteLiteral.ReadImplementation(symbol, "IEndpointBase", "Path", compilation, out var ignoredNewPath, ct);

        // The verbs, resolved the same way: a 'new static Methods' that does not re-implement the
        // interface is ignored by the runtime, so MPEP007 and the contract ignore it too (MPEP032).
        var knownMethods = MethodsLiteral.Read(symbol, httpMethod, compilation, out var ignoredNewMethods, ct);

        OpenGenericInfo? open = null;
        if (GenericTypes.IsOpen(symbol))
        {
            open = new OpenGenericInfo(
                symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                GenericTypes.MetadataNameOf(symbol),
                GenericTypes.UnboundTypeOf(symbol),
                symbol.TypeParameters.Length == 0 ? null : "<" + string.Join(", ", symbol.TypeParameters.Select(parameter => parameter.Name)) + ">");
        }

        var groupSymbol = GroupMembership.ResolveSymbol(symbol, EndpointsNamespace);

        return new EndpointInfo(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace ? containingNamespace.ToDisplayString() : "",
            symbol.Name,
            isPartial,
            hasExistingBaseClass,
            level, httpMethod,
            shape.RequestTypeFqn, shape.ResponseTypeFqn,
            groupSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            baseChainReachesEndpointBase,
            GetDescriptorName(symbol),
            symbol.FromSymbol().AsKey(),
            symbol.GetPathSpec(ct),
            route,
            BoundProperties.Collect(symbol, EndpointsNamespace, ct),
            knownMethods,
            shape.RequestType is { } requestType
                ? RequestValidationGaps.Inspect(requestType, compilation)
                : RequestValidationGap.None,
            GeneratedCodeAccess.WhyInaccessible(symbol),
            GeneratedCodeAccess.IsInFileLocalType(symbol),
            open,
            closed: null,
            referencedGroups: open is null ? ConstructedGroupChain(groupSymbol, compilation, ct) : default,
            hasIgnoredNewPath: ignoredNewPath,
            hasIgnoredNewMethods: ignoredNewMethods);
    }

    /// <summary>
    /// The constructed generic groups on the chain starting at <paramref name="group"/>
    /// (<c>[MemberOf&lt;Api&lt;string&gt;&gt;]</c>), described from their constructions (PRD D7).
    /// </summary>
    /// <remarks>
    /// The group declaration is the open <c>Api&lt;T&gt;</c>, which generated code cannot name and
    /// whose fully qualified name never matches the membership's <c>Api&lt;string&gt;</c>. That mismatch
    /// used to leave the endpoint without a composed route — no link, no contract — and reported the
    /// declaration as never joined (MPEP016).
    /// </remarks>
    internal static ImmutableArray<GroupInfo> ConstructedGroupChain(INamedTypeSymbol? group, Compilation compilation, CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<GroupInfo>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        for (var current = group; current is not null;)
        {
            ct.ThrowIfCancellationRequested();

            var fqn = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!visited.Add(fqn)) break;

            var parent = ParentGroupOf(current, compilation);
            if (IsConstruction(current))
                builder.Add(DescribeGroup(current, parent, compilation, prefix: null, useRecordedPrefix: false, ct));

            current = parent;
        }

        return builder.ToImmutable();
    }

    /// <summary>The parent group of <paramref name="group"/>, substituted through the group's own construction.</summary>
    internal static INamedTypeSymbol? ParentGroupOf(INamedTypeSymbol group, Compilation compilation)
    {
        var parent = GroupMembership.ResolveSymbol(group, EndpointsNamespace);
        if (parent is null || !IsConstruction(group)) return parent;

        return GenericTypes.Substitute(parent, GenericTypes.MapOf(group), compilation) as INamedTypeSymbol;
    }

    /// <summary>True for a constructed generic type (<c>Api&lt;string&gt;</c>), as opposed to a definition.</summary>
    internal static bool IsConstruction(INamedTypeSymbol type) =>
        !SymbolEqualityComparer.Default.Equals(type, type.OriginalDefinition);

    /// <summary>A group as generated code sees it: its name, parent, prefix and whether it can be named.</summary>
    internal static GroupInfo DescribeGroup(INamedTypeSymbol group, INamedTypeSymbol? parent, Compilation compilation, string? prefix, bool useRecordedPrefix, CancellationToken ct)
    {
        var location = group.Locations.FirstOrDefault() is { IsInSource: true } inSource ? inSource.AsKey() : null;

        return new GroupInfo(
            group.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            parent?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            location,
            useRecordedPrefix ? prefix : RouteLiteral.ReadImplementation(group, "IEndpointGroup", "Prefix", compilation, out _, ct),
            GeneratedCodeAccess.WhyInaccessible(group));
    }

    private static bool IsMoreDerived(INamedTypeSymbol candidate, INamedTypeSymbol? incumbent)
    {
        if (incumbent is null) return true;
        if (candidate.TypeArguments.Length != incumbent.TypeArguments.Length)
            return candidate.TypeArguments.Length > incumbent.TypeArguments.Length;

        return string.CompareOrdinal(
            candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            incumbent.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) < 0;
    }

    internal static string? GetDescriptorName(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "EndpointDescriptorNameAttribute" &&
                attribute.AttributeClass?.ContainingNamespace?.ToDisplayString() == EndpointsNamespace &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string name &&
                name.Length > 0)
                return name;
        }

        return null;
    }

    private static bool IsGroupCandidate(SyntaxNode node, CancellationToken _)
    {
        if (node is not ClassDeclarationSyntax classDecl || classDecl.BaseList is null)
            return false;

        foreach (var baseType in classDecl.BaseList.Types)
        {
            if (NameOf(baseType.Type) == "IEndpointGroup")
                return true;
        }

        return false;
    }

    private static GroupInfo? GetGroupInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);
        if (symbol is null || symbol.IsAbstract) return null;

        if (!symbol.AllInterfaces.Any(i => i.Name == "IEndpointGroup" && i.ContainingNamespace?.ToDisplayString() == EndpointsNamespace))
            return null;

        return new GroupInfo(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            GroupMembership.Resolve(symbol, EndpointsNamespace),
            symbol.FromSymbol().AsKey(),
            RouteLiteral.ReadImplementation(symbol, "IEndpointGroup", "Prefix", context.SemanticModel.Compilation, out _, ct),
            GeneratedCodeAccess.WhyInaccessible(symbol),
            GenericTypes.IsOpen(symbol),
            GenericTypes.UnboundTypeOf(symbol));
    }

    private static bool ReachesEndpointBase(INamedTypeSymbol? type)
    {
        for (var current = type; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType)
        {
            if (current.ContainingNamespace?.ToDisplayString() == EndpointsNamespace && IsOurBaseClass(current.Name))
                return true;
        }

        return false;
    }

    private static bool IsOurBaseClass(string name) => name is
        "EndpointBase" or "BodyEndpoint" or "ResponseEndpoint" or
        "PostEndpoint" or "PutEndpoint" or "PatchEndpoint" or
        "GetEndpoint" or "DeleteEndpoint";

    private static AssemblyInfo GetAssemblyInfo(Compilation compilation)
    {
        var assemblyName = compilation.AssemblyName ?? "Unknown";
        string? methodNameOverride = null;

        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "EndpointsMethodNameAttribute" &&
                attr.AttributeClass?.ContainingNamespace?.ToDisplayString() == EndpointsNamespace &&
                attr.ConstructorArguments.Length == 1 &&
                attr.ConstructorArguments[0].Value is string name)
            {
                methodNameOverride = name;
                break;
            }
        }

        return new AssemblyInfo(
            assemblyName,
            methodNameOverride,
            HasOpenApiTransformers(compilation),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder") is not null,
            compilation.GetTypeByMetadataName(OpenEndpointRecords.AttributeNamespace + "." + OpenEndpointRecords.EndpointAttributeName) is not null);
    }

    /// <summary>
    /// Whether the emitted schema transformer would compile against this consumer's references.
    /// </summary>
    /// <remarks>
    /// Three probes, because the context type on its own is not enough. It also exists in
    /// <c>Microsoft.AspNetCore.OpenApi</c> 9.x, which a net10.0 project can still reference, but
    /// that version builds on <c>Microsoft.OpenApi</c> 1.x — schemas in
    /// <c>Microsoft.OpenApi.Models</c>, no <c>JsonSchemaType</c> — and has no endpoint-level
    /// <c>AddOpenApiOperationTransformer</c>. Emitting against it would put compile errors in a
    /// file the consumer cannot edit. <c>JsonSchemaType</c> exists from 2.x on, and the extension
    /// is looked up on the type the call is emitted against, so a moved method fails closed.
    /// </remarks>
    private static bool HasOpenApiTransformers(Compilation compilation) =>
        compilation.GetTypeByMetadataName("Microsoft.AspNetCore.OpenApi.OpenApiOperationTransformerContext") is not null &&
        compilation.GetTypeByMetadataName("Microsoft.OpenApi.JsonSchemaType") is not null &&
        compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.OpenApiEndpointConventionBuilderExtensions") is { } extensions &&
        !extensions.GetMembers("AddOpenApiOperationTransformer").IsEmpty;
}
