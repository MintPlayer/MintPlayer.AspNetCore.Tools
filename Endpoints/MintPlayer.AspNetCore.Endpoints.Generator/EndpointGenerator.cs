using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

[Generator]
public class EndpointGenerator : IIncrementalGenerator
{
    private const string EndpointsNamespace = "MintPlayer.AspNetCore.Endpoints";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Step 1: Find endpoint candidate classes
        var endpoints = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: IsEndpointCandidate,
                transform: GetEndpointInfo)
            .Where(info => info is not null)
            .Select((info, _) => info!);

        // Step 2: Get assembly info
        var assemblyInfo = context.CompilationProvider
            .Select((compilation, _) => GetAssemblyInfo(compilation));

        // Step 3: Collect endpoints
        var collectedEndpoints = endpoints.Collect();

        // Task A: Generate partial class base declarations
        context.RegisterSourceOutput(collectedEndpoints, GeneratePartialClasses);

        // Task B: Generate mapping extension method
        var combined = assemblyInfo.Combine(collectedEndpoints);
        context.RegisterSourceOutput(combined, GenerateMappingExtension);
    }

    private static bool IsEndpointCandidate(SyntaxNode node, CancellationToken _)
    {
        if (node is not ClassDeclarationSyntax classDecl || classDecl.BaseList is null)
            return false;

        // Quick syntax-only check: does the base list contain any of our endpoint interface names?
        foreach (var baseType in classDecl.BaseList.Types)
        {
            var name = GetBaseTypeName(baseType.Type);
            if (name is null) continue;

            if (name.StartsWith("IEndpoint") ||
                name.StartsWith("IGetEndpoint") ||
                name.StartsWith("IPostEndpoint") ||
                name.StartsWith("IPutEndpoint") ||
                name.StartsWith("IDeleteEndpoint") ||
                name.StartsWith("IPatchEndpoint") ||
                name.StartsWith("IMemberOf"))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetBaseTypeName(TypeSyntax type) => type switch
    {
        SimpleNameSyntax simple => simple.Identifier.Text,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
        _ => null
    };

    private static EndpointInfo? GetEndpointInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);
        if (symbol is null) return null;

        // Must implement IEndpointBase
        bool implementsEndpointBase = symbol.AllInterfaces.Any(i =>
            i.Name == "IEndpointBase" &&
            i.ContainingNamespace?.ToDisplayString() == EndpointsNamespace);

        if (!implementsEndpointBase) return null;

        // Skip abstract classes
        if (symbol.IsAbstract) return null;

        // Analyze directly declared interfaces to determine level, method, and group
        var level = EndpointLevel.Raw;
        var httpMethod = HttpMethodKind.Custom;
        string? requestTypeFqn = null;
        string? responseTypeFqn = null;
        string? groupTypeFqn = null;
        int groupCount = 0;

        foreach (var iface in symbol.Interfaces)
        {
            var ns = iface.ContainingNamespace?.ToDisplayString();
            if (ns != EndpointsNamespace) continue;

            var name = iface.Name;
            var arity = iface.TypeArguments.Length;

            // Check for IMemberOf<T>
            if (name == "IMemberOf" && arity == 1)
            {
                groupCount++;
                groupTypeFqn = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                continue;
            }

            // Check convenience interfaces
            var method = name switch
            {
                "IGetEndpoint" => HttpMethodKind.Get,
                "IPostEndpoint" => HttpMethodKind.Post,
                "IPutEndpoint" => HttpMethodKind.Put,
                "IDeleteEndpoint" => HttpMethodKind.Delete,
                "IPatchEndpoint" => HttpMethodKind.Patch,
                _ => (HttpMethodKind?)null
            };

            if (method.HasValue)
            {
                httpMethod = method.Value;
                if (arity >= 2)
                {
                    level = EndpointLevel.TypedWithResponse;
                    requestTypeFqn = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    responseTypeFqn = iface.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
                else if (arity == 1)
                {
                    level = EndpointLevel.Typed;
                    requestTypeFqn = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
                continue;
            }

            // Check generic IEndpoint variants
            if (name == "IEndpoint")
            {
                if (arity >= 2 && level != EndpointLevel.TypedWithResponse)
                {
                    level = EndpointLevel.TypedWithResponse;
                    requestTypeFqn = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    responseTypeFqn = iface.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
                else if (arity == 1 && level == EndpointLevel.Raw)
                {
                    level = EndpointLevel.Typed;
                    requestTypeFqn = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }
        }

        // Check if this specific partial declaration has a user-defined base class
        bool hasExistingBaseClass = false;
        if (classDecl.BaseList is not null)
        {
            foreach (var baseType in classDecl.BaseList.Types)
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(baseType.Type, ct);
                if (typeInfo.Type is INamedTypeSymbol { TypeKind: TypeKind.Class } baseSymbol &&
                    baseSymbol.SpecialType != SpecialType.System_Object)
                {
                    // Check if it's one of our generated base classes (ignore those)
                    var baseNs = baseSymbol.ContainingNamespace?.ToDisplayString();
                    if (baseNs == EndpointsNamespace && IsOurBaseClass(baseSymbol.Name))
                        continue;

                    hasExistingBaseClass = true;
                    break;
                }
            }
        }

        bool isPartial = classDecl.Modifiers.Any(SyntaxKind.PartialKeyword);

        return new EndpointInfo(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.ContainingNamespace?.ToDisplayString() ?? "",
            symbol.Name,
            isPartial,
            hasExistingBaseClass,
            level,
            httpMethod,
            requestTypeFqn,
            responseTypeFqn,
            groupTypeFqn,
            groupCount > 1);
    }

    private static bool IsOurBaseClass(string name)
    {
        return name is "EndpointBase" or "BodyEndpoint" or "NonBodyEndpoint" or
            "PostEndpoint" or "PutEndpoint" or "PatchEndpoint" or
            "GetEndpoint" or "DeleteEndpoint";
    }

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

    private static void GeneratePartialClasses(SourceProductionContext context, ImmutableArray<EndpointInfo> endpoints)
    {
        var seen = new System.Collections.Generic.HashSet<string>();

        foreach (var endpoint in endpoints)
        {
            // Skip duplicates (multiple partial declarations of the same class)
            if (!seen.Add(endpoint.FullyQualifiedName))
                continue;

            // Report diagnostics
            if (endpoint.HasMultipleGroups)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.EndpointHasMultipleGroups,
                    Location.None,
                    endpoint.ClassName));
            }

            if (!endpoint.NeedsPartialClass)
                continue;

            if (!endpoint.IsPartial)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.EndpointMustBePartial,
                    Location.None,
                    endpoint.ClassName));
                continue;
            }

            if (endpoint.HasExistingBaseClass)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.EndpointHasBaseClassConflict,
                    Location.None,
                    endpoint.ClassName));
                continue;
            }

            var source = Emitter.EmitPartialClass(endpoint);
            if (!string.IsNullOrEmpty(source))
            {
                context.AddSource($"{endpoint.ClassName}.g.cs", source);
            }
        }
    }

    private static void GenerateMappingExtension(
        SourceProductionContext context,
        (AssemblyInfo AssemblyInfo, ImmutableArray<EndpointInfo> Endpoints) source)
    {
        var (assemblyInfo, endpoints) = source;

        if (endpoints.IsDefaultOrEmpty)
            return;

        var mappingSource = Emitter.EmitMappingExtension(assemblyInfo, endpoints);
        if (!string.IsNullOrEmpty(mappingSource))
        {
            context.AddSource("EndpointMappingExtensions.g.cs", mappingSource);
        }
    }
}
