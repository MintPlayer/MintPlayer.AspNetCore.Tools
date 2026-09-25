using Microsoft.CodeAnalysis;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>Name checks on symbols that do not build display strings.</summary>
internal static class SymbolNames
{
    /// <summary>
    /// True when <paramref name="ns"/> is the namespace <paramref name="dotted"/>; the same answer as
    /// <c>ns?.ToDisplayString() == dotted</c>, without allocating the display string.
    /// </summary>
    /// <remarks>
    /// The discovery transform asks this several times per interface of every candidate class on every
    /// keystroke, so the string it used to build was a measurable share of its cost (PRD addendum 2,
    /// D19). A namespace name never contains a dot, so matching the segments from the end is exact.
    /// </remarks>
    public static bool IsNamespace(INamespaceSymbol? ns, string dotted)
    {
        var end = dotted.Length;
        for (var current = ns; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
        {
            var name = current.Name;
            var start = end - name.Length;
            if (start < 0 || string.CompareOrdinal(dotted, start, name, 0, name.Length) != 0) return false;
            if (start == 0) return current.ContainingNamespace is null or { IsGlobalNamespace: true };
            if (dotted[start - 1] != '.') return false;
            end = start - 1;
        }

        return false;
    }
}
