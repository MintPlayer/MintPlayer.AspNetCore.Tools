using Microsoft.CodeAnalysis;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Whether the generated mapping, typed-link and contract files can name a type at all (MPEP024).
/// </summary>
/// <remarks>
/// Those files declare their own top-level types, so they reach an endpoint or group only through
/// its fully qualified name. That works for a type that is <c>public</c>, <c>internal</c> or
/// <c>protected internal</c> all the way out. A <c>private</c>, <c>protected</c> or
/// <c>private protected</c> nested type, or anything nested inside one, is CS0122 there; a
/// <c>file</c> type (or anything nested inside one) cannot be named from another file at all. Either
/// way the consumer used to read a compiler error inside generated code with no diagnostic of ours.
/// </remarks>
internal static class GeneratedCodeAccess
{
    /// <summary>
    /// Why generated code outside <paramref name="symbol"/> cannot name it, phrased to follow
    /// "because", or <see langword="null"/> when it can.
    /// </summary>
    public static string? WhyInaccessible(INamedTypeSymbol symbol)
    {
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType)
        {
            var self = SymbolEqualityComparer.Default.Equals(current, symbol);

            if (current.IsFileLocal)
                return self
                    ? "it is a file-local type"
                    : $"it is nested inside the file-local type '{current.Name}'";

            var keyword = current.DeclaredAccessibility switch
            {
                Accessibility.Private => "private",
                Accessibility.Protected => "protected",
                Accessibility.ProtectedAndInternal => "private protected",
                _ => null,
            };

            if (keyword is not null)
                return self
                    ? $"it is declared '{keyword}'"
                    : $"it is nested inside '{current.Name}', which is declared '{keyword}'";
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="symbol"/> or a type it is nested in is a <c>file</c> type.
    /// </summary>
    /// <remarks>
    /// Such an endpoint gets no generated partial either: a <c>partial class</c> in the generated file
    /// cannot join a file-local type, so it would declare a second, unrelated type of the same name —
    /// one that inherits an abstract endpoint base and fails to compile in generated code.
    /// </remarks>
    public static bool IsInFileLocalType(INamedTypeSymbol symbol)
    {
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal) return true;
        }

        return false;
    }
}
