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

        var modelProvider = endpointsProvider
            .Join(groupsProvider)
            .Join(assemblyInfoProvider)
            .Select(static (tuple, _) => new EndpointModel(tuple.Item1, tuple.Item2, tuple.Item3))
            .WithTrackingName(TrackingNames.Model);

        // Deliberately NOT GeneratorExtensions.ProduceCode: that helper combines the producer with
        // CompilationProvider, and a Compilation is a fresh object with no value equality on every
        // compilation — so the source output could never be cached however well the models compared.
        // Registering on the value-equal model instead is what makes the equality in Models.cs pay.
        context.RegisterSourceOutput(modelProvider, static (productionContext, model) =>
            EndpointMappingProducer.Emit(productionContext, model));

        // Diagnostics do go through the Tools pipeline, because turning a LocationKey back into a
        // Location needs the Compilation. They are recomputed per compilation; they are cheap, and
        // there is no correct way to hold a Location across one.
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

        if (!symbol.AllInterfaces.Any(i => i.Name == "IEndpointBase" && i.ContainingNamespace?.ToDisplayString() == EndpointsNamespace))
            return null;

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

        return new EndpointInfo(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.ContainingNamespace?.ToDisplayString() ?? "",
            symbol.Name,
            isPartial,
            hasExistingBaseClass,
            level, httpMethod,
            requestTypeFqn, responseTypeFqn,
            GroupMembership.Resolve(symbol, EndpointsNamespace),
            baseChainReachesEndpointBase,
            GetDescriptorName(symbol),
            symbol.FromSymbol().AsKey(),
            symbol.GetPathSpec(ct),
            RouteLiteral.Read(symbol, "Path", context.SemanticModel, ct),
            BoundProperties.Collect(symbol, EndpointsNamespace, ct));
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

    private static string? GetDescriptorName(INamedTypeSymbol symbol)
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
            RouteLiteral.Read(symbol, "Prefix", context.SemanticModel, ct));
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

        return new AssemblyInfo(assemblyName, methodNameOverride);
    }
}
