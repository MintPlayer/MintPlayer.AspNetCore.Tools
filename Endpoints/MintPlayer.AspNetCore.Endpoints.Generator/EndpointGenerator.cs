using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.Models;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

[Generator(LanguageNames.CSharp)]
public partial class EndpointGenerator : IncrementalGenerator
{
    private const string EndpointsNamespace = "MintPlayer.AspNetCore.Endpoints";

    public override void Initialize(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<Settings> settingsProvider,
        IncrementalValueProvider<ICompilationCache> cacheProvider)
    {
        var endpointsProvider = context.SyntaxProvider
            .CreateSyntaxProvider(IsEndpointCandidate, GetEndpointInfo)
            .Where(static info => info is not null)
            .Select(static (info, _) => info!)
            .Collect();

        var assemblyInfoProvider = context.CompilationProvider
            .Select(static (compilation, _) => GetAssemblyInfo(compilation));

        var producerProvider = endpointsProvider
            .Join(assemblyInfoProvider)
            .Select(static (tuple, _) => (Producer)new EndpointMappingProducer(tuple.Item1, tuple.Item2));

        context.ProduceCode(producerProvider);
    }

    private static bool IsEndpointCandidate(SyntaxNode node, CancellationToken _)
    {
        if (node is not ClassDeclarationSyntax classDecl || classDecl.BaseList is null)
            return false;

        foreach (var baseType in classDecl.BaseList.Types)
        {
            var name = baseType.Type switch
            {
                SimpleNameSyntax simple => simple.Identifier.Text,
                QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
                _ => (string?)null
            };

            if (name is not null && (
                name.StartsWith("IEndpoint") ||
                name.StartsWith("IGetEndpoint") ||
                name.StartsWith("IPostEndpoint") ||
                name.StartsWith("IPutEndpoint") ||
                name.StartsWith("IDeleteEndpoint") ||
                name.StartsWith("IPatchEndpoint") ||
                name.StartsWith("IMemberOf")))
                return true;
        }

        return false;
    }

    private static EndpointInfo? GetEndpointInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);
        if (symbol is null || symbol.IsAbstract) return null;

        if (!symbol.AllInterfaces.Any(i => i.Name == "IEndpointBase" && i.ContainingNamespace?.ToDisplayString() == EndpointsNamespace))
            return null;

        var level = EndpointLevel.Raw;
        var httpMethod = HttpMethodKind.Custom;
        string? requestTypeFqn = null;
        string? responseTypeFqn = null;
        string? groupTypeFqn = null;
        int groupCount = 0;

        foreach (var iface in symbol.Interfaces)
        {
            if (iface.ContainingNamespace?.ToDisplayString() != EndpointsNamespace) continue;

            var name = iface.Name;
            var arity = iface.TypeArguments.Length;

            if (name == "IMemberOf" && arity == 1)
            {
                groupCount++;
                groupTypeFqn = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                continue;
            }

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

        // Check for user-defined base class (skip our own generated base classes)
        bool hasExistingBaseClass = false;
        foreach (var baseType in classDecl.BaseList!.Types)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(baseType.Type, ct);
            if (typeInfo.Type is INamedTypeSymbol { TypeKind: TypeKind.Class } baseSymbol &&
                baseSymbol.SpecialType != SpecialType.System_Object)
            {
                var baseNs = baseSymbol.ContainingNamespace?.ToDisplayString();
                if (baseNs == EndpointsNamespace && IsOurBaseClass(baseSymbol.Name))
                    continue;
                hasExistingBaseClass = true;
                break;
            }
        }

        return new EndpointInfo(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.ContainingNamespace?.ToDisplayString() ?? "",
            symbol.Name,
            classDecl.Modifiers.Any(SyntaxKind.PartialKeyword),
            hasExistingBaseClass,
            level, httpMethod,
            requestTypeFqn, responseTypeFqn,
            groupTypeFqn, groupCount > 1);
    }

    private static bool IsOurBaseClass(string name) => name is
        "EndpointBase" or "BodyEndpoint" or "NonBodyEndpoint" or
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
