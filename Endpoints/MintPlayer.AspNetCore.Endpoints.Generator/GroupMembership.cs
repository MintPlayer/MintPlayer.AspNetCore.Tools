using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Resolves which group an endpoint or group belongs to, from <c>[MemberOf&lt;TGroup&gt;]</c>.
/// </summary>
/// <remarks>
/// <b>Nearest declaration wins, walking up the base chain.</b> Roslyn's
/// <c>ISymbol.GetAttributes()</c> never returns inherited attributes — <c>Inherited = true</c>
/// changes reflection, not the compiler's view — so the walk is explicit here. The runtime
/// <c>MapEndpoint&lt;T&gt;()</c> path walks <c>Type.BaseType</c> with
/// <c>GetCustomAttributes(inherit: false)</c> and takes the first hit, which is the identical rule;
/// the two registration paths must never disagree about which group an endpoint is in. It
/// deliberately does not use <c>inherit: true</c>: for a generic attribute that returns both the
/// derived and the base declaration whenever their closed types differ.
/// <para>
/// Multiple membership is no longer a valid program. <c>AllowMultiple = false</c> makes two
/// memberships on one type <c>CS0579</c> — enforced by the compiler even across partial
/// declarations — and a declaration on a derived type overriding one on its base is a
/// deliberate override, not a conflict. That is why MPEP003 and MPEP004 no longer exist. The
/// generator still runs on such an invalid compilation and simply takes the first attribute; the
/// build fails on CS0579 regardless, so there is nothing for a diagnostic of its own to add.
/// </para>
/// </remarks>
internal static class GroupMembership
{
    /// <summary>
    /// The arity-encoded metadata name. The backtick is load-bearing: <c>MemberOfAttribute</c>
    /// and <c>MemberOfAttribute&lt;TGroup&gt;</c> both match nothing, silently, which is
    /// indistinguishable from a project with no groups.
    /// </summary>
    public const string AttributeMetadataName = "MemberOfAttribute`1";

    /// <summary>
    /// Returns the fully qualified group type the symbol is a member of, or
    /// <see langword="null"/> when it belongs to no group.
    /// </summary>
    public static string? Resolve(INamedTypeSymbol symbol, string endpointsNamespace)
        => ResolveSymbol(symbol, endpointsNamespace)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// The group type itself, as the attribute names it.
    /// </summary>
    /// <remarks>
    /// Roslyn does not substitute an attribute's type arguments when the attribute is read through a
    /// constructed type: <c>[MemberOf&lt;Api&gt;]</c> inside <c>Outer&lt;T&gt;</c> comes back as
    /// <c>Outer&lt;T&gt;.Api</c> for <c>Outer&lt;AppUser&gt;.Inner</c> too. A caller holding a
    /// construction substitutes with <see cref="GenericTypes.Substitute"/>.
    /// </remarks>
    public static INamedTypeSymbol? ResolveSymbol(INamedTypeSymbol symbol, string endpointsNamespace)
    {
        // GetAllBaseTypes is self-inclusive and ends at System.Object, so the first hit is the
        // nearest declaration.
        foreach (var type in symbol.GetAllBaseTypes())
        {
            foreach (var attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass is not { IsGenericType: true } attributeClass) continue;
                if (attributeClass.OriginalDefinition.MetadataName != AttributeMetadataName) continue;
                if (attributeClass.ContainingNamespace?.ToDisplayString() != endpointsNamespace) continue;
                if (attributeClass.TypeArguments.Length != 1) continue;

                return attributeClass.TypeArguments[0] as INamedTypeSymbol;
            }
        }

        return null;
    }
}
