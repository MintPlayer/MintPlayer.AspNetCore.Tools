using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Decides whether an endpoint overrides <c>IEndpointBase.GetPath</c> (PRD R2.4, D5; PLAN spike S1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Syntax when it is exact, the interface map otherwise.</b> A class with no base class of its own
/// and no user interface between it and <c>IEndpointBase</c> can only implement <c>GetPath</c> with a
/// member it declares: a <c>public static</c> one, or an explicit <c>IEndpointBase.GetPath</c>. Both are
/// visible in its declarations without binding anything, and the class's own <c>internal static
/// GetPath</c> is correctly excluded — measured in S1, it does not implement the member, and the
/// compiler says nothing. The return type is not filtered on: a non-nullable <c>string</c> implements
/// the member too.
/// </para>
/// <para>
/// Anything else — a base class, which may own the implementation, or an intermediate interface such as
/// <c>IMyEndpoint : IEndpoint</c>, which may supply <c>static string? IEndpointBase.GetPath</c> as a
/// default — asks <c>FindImplementationForInterfaceMember</c>. The endpoint overrides when the
/// implementation is anything but <c>IEndpointBase</c>'s own default. A null implementation is a
/// diamond (CS8705): the consumer already has an error, and treating it as an override keeps every
/// route-shaped output from presenting a guess.
/// </para>
/// <para>
/// A class that overrides nothing (nearly all of them) takes the syntactic path, so the incremental
/// cost is a scan of its member declarations.
/// </para>
/// </remarks>
internal static class PathOverride
{
    private const string EndpointsNamespace = "MintPlayer.AspNetCore.Endpoints";
    private const string MemberName = "GetPath";

    /// <summary>True when <paramref name="symbol"/> overrides <c>IEndpointBase.GetPath</c>.</summary>
    /// <param name="symbol">The endpoint; a constructed type works.</param>
    /// <param name="newGetPathIgnored">
    /// True when the nearest static <c>GetPath</c> on the class chain is not the implementation the
    /// runtime calls — the <c>new static GetPath</c> shape of MPEP032.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static bool Detect(INamedTypeSymbol symbol, out bool newGetPathIgnored, CancellationToken cancellationToken)
    {
        newGetPathIgnored = false;

        // An Abstractions older than 11.4 has no GetPath, and so nothing to override.
        if (FindInterfaceMember(symbol) is not { } interfaceMember) return false;

        var hasBaseClass = symbol.BaseType is { SpecialType: not SpecialType.System_Object };
        if (!hasBaseClass && !HasUserEndpointInterface(symbol, interfaceMember.ContainingType) &&
            OwnDeclarationsOverride(symbol, cancellationToken) is { } declared)
            return declared;

        var implementation = symbol.FindImplementationForInterfaceMember(interfaceMember);

        if (implementation is not null && NearestStaticGetPath(symbol) is { } nearest)
            newGetPathIgnored = !SymbolEqualityComparer.Default.Equals(implementation.OriginalDefinition, nearest.OriginalDefinition);

        return implementation is null ||
               !SymbolEqualityComparer.Default.Equals(implementation.ContainingType.OriginalDefinition, interfaceMember.ContainingType.OriginalDefinition);
    }

    /// <summary>
    /// Whether the class's own declarations, every partial part included, declare a <c>GetPath</c> that
    /// can implement the member; null when a declaration is not a plain class declaration.
    /// </summary>
    private static bool? OwnDeclarationsOverride(INamedTypeSymbol symbol, CancellationToken cancellationToken)
    {
        var references = symbol.OriginalDefinition.DeclaringSyntaxReferences;
        if (references.IsEmpty) return null;

        var found = false;
        foreach (var reference in references)
        {
            if (reference.GetSyntax(cancellationToken) is not ClassDeclarationSyntax declaration) return null;

            foreach (var member in declaration.Members)
            {
                if (member is MethodDeclarationSyntax { Identifier.ValueText: MemberName, ParameterList.Parameters.Count: 1 } method &&
                    (IsPublicStaticImplicit(method) || IsExplicitEndpointBase(method)))
                    found = true;
            }
        }

        return found;
    }

    private static bool IsPublicStaticImplicit(MethodDeclarationSyntax method) =>
        method.ExplicitInterfaceSpecifier is null &&
        method.Modifiers.Any(SyntaxKind.PublicKeyword) &&
        method.Modifiers.Any(SyntaxKind.StaticKeyword);

    private static bool IsExplicitEndpointBase(MethodDeclarationSyntax method) =>
        method.ExplicitInterfaceSpecifier?.Name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText == "IEndpointBase",
            AliasQualifiedNameSyntax aliased => aliased.Name.Identifier.ValueText == "IEndpointBase",
            SimpleNameSyntax simple => simple.Identifier.ValueText == "IEndpointBase",
            _ => false,
        };

    /// <summary><c>IEndpointBase.GetPath</c>, from the symbol's interfaces.</summary>
    private static IMethodSymbol? FindInterfaceMember(INamedTypeSymbol symbol)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.Name != "IEndpointBase" || !SymbolNames.IsNamespace(iface.ContainingNamespace, EndpointsNamespace)) continue;

            foreach (var member in iface.GetMembers(MemberName))
            {
                if (member is IMethodSymbol { IsStatic: true, Parameters.Length: 1 } method)
                    return method;
            }
        }

        return null;
    }

    /// <summary>
    /// True when the class implements an interface of its own (not one of the library's) that extends
    /// <c>IEndpointBase</c>, and so may supply a <c>GetPath</c> default.
    /// </summary>
    private static bool HasUserEndpointInterface(INamedTypeSymbol symbol, INamedTypeSymbol endpointBase)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (SymbolNames.IsNamespace(iface.ContainingNamespace, EndpointsNamespace)) continue;
            if (iface.AllInterfaces.Contains(endpointBase, SymbolEqualityComparer.Default)) return true;
        }

        return false;
    }

    /// <summary>The nearest implicit static <c>GetPath(x)</c> on the class or its base classes.</summary>
    private static IMethodSymbol? NearestStaticGetPath(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(MemberName))
            {
                if (member is IMethodSymbol { IsStatic: true, Parameters.Length: 1, ExplicitInterfaceImplementations.IsEmpty: true } method)
                    return method;
            }
        }

        return null;
    }
}
