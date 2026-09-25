using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Recovers the compile-time value of a <c>static abstract string</c> member — <c>Path</c> on an
/// endpoint, <c>Prefix</c> on a group — so the generator can reason about routes instead of
/// deferring every question about them to run time.
/// </summary>
/// <remarks>
/// Everything route-shaped this library can diagnose depends on this file. Without it the
/// generator emits <c>TEndpoint.Path</c> as an expression it never reads, which is why a typo'd
/// route parameter, two endpoints on one route, and a group-relative path written absolutely are
/// all invisible at build time today.
/// <para>
/// <b>Recovery is best-effort by design, and callers must treat <see langword="null"/> as "do not
/// know", never as "empty".</b> Two cases are permanently unrecoverable and both are legitimate:
/// a non-constant expression (an interpolated string, a <c>static readonly</c> field, a method
/// call), and any endpoint that lives in a <i>referenced assembly</i>, where the property body is
/// IL rather than syntax and <c>DeclaringSyntaxReferences</c> is empty. A generator that hard-fails
/// on either would break cross-assembly endpoints outright.
/// </para>
/// </remarks>
internal static class RouteLiteral
{
    private const string EndpointsNamespace = "MintPlayer.AspNetCore.Endpoints";

    /// <summary>
    /// Reads a static string member's constant value, walking the base chain so a member declared
    /// on a base class is still found.
    /// </summary>
    /// <param name="symbol">The endpoint or group type.</param>
    /// <param name="memberName">
    /// <c>Path</c> or <c>Prefix</c>. An explicit interface implementation is matched too, since it
    /// is named after the fully qualified interface (<c>…IEndpointBase.Path</c>).
    /// </param>
    /// <param name="model">
    /// Any semantic model from the same compilation. The member may be declared in a different
    /// syntax tree than the one being visited — a base class, or the other half of a partial — so
    /// the model for the expression's own tree is fetched from
    /// <see cref="SemanticModel.Compilation"/>. Going through the compilation here rather than
    /// combining the pipeline with <c>CompilationProvider</c> is what keeps the generator's
    /// caching intact.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The constant value, or <see langword="null"/> when it cannot be recovered.</returns>
    public static string? Read(
        INamedTypeSymbol symbol,
        string memberName,
        SemanticModel model,
        CancellationToken cancellationToken)
        => ValueOf(FindStaticStringProperty(symbol, memberName), model.Compilation, cancellationToken);

    /// <summary>
    /// Reads the <c>Path</c> (or <c>Prefix</c>) <b>the runtime uses</b>: the implementation of the
    /// interface member, which is what <c>TEndpoint.Path</c> dispatches to (PRD D7).
    /// </summary>
    /// <remarks>
    /// The nearest declaration and the implementation differ in one shape: a derived class that hides
    /// its base's <c>Path</c> with <c>new static</c> without listing the endpoint interface again. The
    /// interface map still points at the base's member, so that is the route the endpoint answers on,
    /// and <paramref name="newMemberIgnored"/> reports the discrepancy (MPEP032). Falls back to the
    /// nearest declaration when the implementation cannot be found.
    /// </remarks>
    /// <param name="symbol">The endpoint or group type; constructed types work.</param>
    /// <param name="interfaceName"><c>IEndpointBase</c> or <c>IEndpointGroup</c>.</param>
    /// <param name="memberName"><c>Path</c> or <c>Prefix</c>.</param>
    /// <param name="compilation">The compilation the symbol belongs to.</param>
    /// <param name="newMemberIgnored">True when a nearer <c>new static</c> member is not the one used.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <param name="model">
    /// Optional: the semantic model the caller already holds (the transform's
    /// <c>GeneratorSyntaxContext.SemanticModel</c>). Used when the value expression lives in its tree,
    /// instead of creating a new model per call.
    /// </param>
    /// <remarks>
    /// <b>The fast path (PRD addendum 2, D19).</b> The interface map
    /// (<c>FindImplementationForInterfaceMember</c>) and a fresh semantic model per endpoint were most of
    /// the generator's time on every keystroke. When the class declares exactly one candidate member
    /// itself and nothing above it can own the implementation — it has no base class, or it lists an
    /// endpoint interface again and so re-implements it — that member is both the nearest declaration
    /// and the implementation, and its value is read from syntax when it is a string literal or a
    /// concatenation of them. Every other shape takes the full path below, so the result is the same.
    /// </remarks>
    public static string? ReadImplementation(
        INamedTypeSymbol symbol,
        string interfaceName,
        string memberName,
        Compilation compilation,
        out bool newMemberIgnored,
        CancellationToken cancellationToken,
        SemanticModel? model = null)
    {
        bool IsCandidate(IPropertySymbol property) => IsStaticStringProperty(property, memberName);

        if (FindInterfaceMember(symbol, interfaceName, memberName) is { } interfaceMember &&
            OwnDeclarationCanImplement(symbol, interfaceMember))
        {
            // Syntax first: one `public static string Path` (with a getter) in the class's own
            // declarations is the only candidate, and a valid implementation, without binding the
            // class's member list.
            if (OwnPropertyDeclarations(symbol, memberName, cancellationToken) is { Count: 1 } declarations &&
                IsPublicStaticImplicit(declarations[0]) &&
                declarations[0].Type is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.StringKeyword))
            {
                newMemberIgnored = false;
                return ValueOfDeclaration(declarations[0], compilation, cancellationToken, model);
            }

            if (TryFindOwnImplementation(symbol, interfaceMember, IsCandidate, out var own) && own is not null)
            {
                newMemberIgnored = false;
                return ValueOf(own, compilation, cancellationToken, model);
            }
        }

        var nearest = FindStaticStringProperty(symbol, memberName);
        var implementation = FindImplementation(symbol, interfaceName, memberName);

        newMemberIgnored = implementation is not null && nearest is not null &&
                           !SymbolEqualityComparer.Default.Equals(implementation.OriginalDefinition, nearest.OriginalDefinition);

        return ValueOf(implementation ?? nearest, compilation, cancellationToken, model);
    }

    /// <summary>The library interface's static member <paramref name="memberName"/>, from the symbol's interfaces.</summary>
    internal static IPropertySymbol? FindInterfaceMember(INamedTypeSymbol symbol, string interfaceName, string memberName)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.Name != interfaceName || !SymbolNames.IsNamespace(iface.ContainingNamespace, EndpointsNamespace)) continue;

            foreach (var member in iface.GetMembers(memberName))
            {
                if (member is IPropertySymbol { IsStatic: true } property)
                    return property;
            }
        }

        return null;
    }

    /// <summary>
    /// Decides, without the interface map, whether the class's own declaration implements
    /// <paramref name="interfaceMember"/> (PRD addendum 2, D19).
    /// </summary>
    /// <returns>
    /// False when it cannot be decided this way, and the caller must use the interface map. True with
    /// <paramref name="own"/> set when the class's single own candidate is the implementation; true with
    /// <paramref name="own"/> null when the class has no base class and declares no candidate at all,
    /// so the implementation (if any) is an interface's default.
    /// </returns>
    /// <remarks>
    /// Exact because of the precondition. Without a base class, the class's own member is the only one
    /// the interface can map to ahead of a default implementation. With one, the class must list an
    /// interface that includes <paramref name="interfaceMember"/>'s interface again: that re-implements
    /// it, and the mapping then starts at this class. Otherwise a base class may own the implementation
    /// (the <c>new static</c> shape of PRD D7), and only the interface map knows. A single candidate that
    /// is not a valid implementation (not public, wrong type, an explicit implementation of another
    /// interface), or two candidates, also go to the interface map.
    /// </remarks>
    internal static bool TryFindOwnImplementation(
        INamedTypeSymbol symbol,
        IPropertySymbol interfaceMember,
        Func<IPropertySymbol, bool> isCandidate,
        out IPropertySymbol? own)
    {
        own = null;

        if (!OwnDeclarationCanImplement(symbol, interfaceMember))
            return false;
        var hasBaseClass = symbol.BaseType is { SpecialType: not SpecialType.System_Object };

        IPropertySymbol? found = null;
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol property || !isCandidate(property)) continue;
            if (found is not null) return false;
            found = property;
        }

        if (found is null) return !hasBaseClass;

        var implements = found.ExplicitInterfaceImplementations.IsEmpty
            ? found.DeclaredAccessibility == Accessibility.Public &&
              found.GetMethod is not null &&
              SymbolEqualityComparer.Default.Equals(found.Type, interfaceMember.Type)
            : found.ExplicitInterfaceImplementations.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, interfaceMember));
        if (!implements) return false;

        own = found;
        return true;
    }

    /// <summary>
    /// The precondition of every fast path: the class has no base class, or lists the member's
    /// interface again. Otherwise a base class may own the implementation, and only the interface map
    /// knows (see <see cref="TryFindOwnImplementation"/>).
    /// </summary>
    internal static bool OwnDeclarationCanImplement(INamedTypeSymbol symbol, IPropertySymbol interfaceMember) =>
        symbol.BaseType is null or { SpecialType: SpecialType.System_Object } ||
        ListsInterfaceAgain(symbol, interfaceMember.ContainingType);

    /// <summary>
    /// The property declarations named <paramref name="memberName"/> (implicit, or an explicit
    /// implementation) in the class's own declarations, every partial part included; or
    /// <see langword="null"/> when a declaration is not a plain class declaration, so the caller binds.
    /// </summary>
    /// <remarks>
    /// In a class, only a property declaration declares a property: nothing else can add a
    /// <c>Path</c> or a <c>Methods</c> to its member list. So an empty result means the class has no
    /// such property, and a single one is its only candidate.
    /// </remarks>
    internal static List<PropertyDeclarationSyntax>? OwnPropertyDeclarations(INamedTypeSymbol symbol, string memberName, CancellationToken cancellationToken)
    {
        var result = new List<PropertyDeclarationSyntax>();
        var references = symbol.OriginalDefinition.DeclaringSyntaxReferences;
        if (references.IsEmpty) return null;

        foreach (var reference in references)
        {
            if (reference.GetSyntax(cancellationToken) is not ClassDeclarationSyntax declaration) return null;

            foreach (var member in declaration.Members)
            {
                if (member is PropertyDeclarationSyntax property && property.Identifier.ValueText == memberName)
                    result.Add(property);
            }
        }

        return result;
    }

    /// <summary>
    /// <c>public static</c>, not an explicit implementation, and readable: an implicit implementation of
    /// a static interface property, given the right type.
    /// </summary>
    internal static bool IsPublicStaticImplicit(PropertyDeclarationSyntax property) =>
        property.ExplicitInterfaceSpecifier is null &&
        property.Modifiers.Any(SyntaxKind.PublicKeyword) &&
        property.Modifiers.Any(SyntaxKind.StaticKeyword) &&
        (property.ExpressionBody is not null ||
         property.AccessorList?.Accessors.Any(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) == true);

    /// <summary><see cref="ValueOf"/> for a property with exactly this one declaration.</summary>
    private static string? ValueOfDeclaration(PropertyDeclarationSyntax declaration, Compilation compilation, CancellationToken cancellationToken, SemanticModel? model)
    {
        var expression = ValueExpressionOf(declaration);
        if (expression is null) return null;

        if (FoldStringLiterals(expression) is { } literal)
            return literal;

        var constant = ModelFor(expression.SyntaxTree, compilation, model).GetConstantValue(expression, cancellationToken);
        return constant.HasValue && constant.Value is string value ? value : null;
    }

    /// <summary>True when the class's own base list names <paramref name="iface"/> or an interface that extends it.</summary>
    private static bool ListsInterfaceAgain(INamedTypeSymbol symbol, INamedTypeSymbol iface)
    {
        foreach (var direct in symbol.Interfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(direct, iface)) return true;
            if (direct.AllInterfaces.Contains(iface, SymbolEqualityComparer.Default)) return true;
        }

        return false;
    }

    private static IPropertySymbol? FindImplementation(INamedTypeSymbol symbol, string interfaceName, string memberName)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.Name != interfaceName || !SymbolNames.IsNamespace(iface.ContainingNamespace, EndpointsNamespace)) continue;

            foreach (var member in iface.GetMembers(memberName))
            {
                if (member is IPropertySymbol { IsStatic: true } property &&
                    symbol.FindImplementationForInterfaceMember(property) is IPropertySymbol implementation)
                    return implementation;
            }
        }

        return null;
    }

    private static string? ValueOf(IPropertySymbol? property, Compilation compilation, CancellationToken cancellationToken, SemanticModel? model = null)
    {
        if (property is null) return null;

        // DeclaringSyntaxReferences of a member of a constructed type are its definition's, so a
        // closed construction of a source endpoint reads the same literal as its definition.
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var expression = ValueExpressionOf(reference.GetSyntax(cancellationToken));
            if (expression is null) continue;

            // A literal, or literals concatenated, has its value in the syntax: no binding needed.
            if (FoldStringLiterals(expression) is { } literal)
                return literal;

            var constant = ModelFor(expression.SyntaxTree, compilation, model).GetConstantValue(expression, cancellationToken);
            if (constant.HasValue && constant.Value is string value)
                return value;
        }

        return null;
    }

    /// <summary>The caller's model when it is for <paramref name="tree"/>, otherwise a new one from the compilation.</summary>
    internal static SemanticModel ModelFor(SyntaxTree tree, Compilation compilation, SemanticModel? model)
        => model is not null && model.SyntaxTree == tree ? model : compilation.GetSemanticModel(tree);

    /// <summary>
    /// The value of a string literal, or of string literals joined with <c>+</c> (parentheses allowed),
    /// read from syntax; <see langword="null"/> for anything else, which the caller then binds.
    /// </summary>
    /// <remarks>
    /// Exactly what <c>GetConstantValue</c> returns for these shapes: a string literal token's
    /// <c>ValueText</c> is its constant value (regular, verbatim and raw alike), and <c>+</c> on two
    /// string constants is their concatenation.
    /// </remarks>
    internal static string? FoldStringLiterals(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                return literal.Token.ValueText;

            case ParenthesizedExpressionSyntax parenthesized:
                return FoldStringLiterals(parenthesized.Expression);

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                return FoldStringLiterals(binary.Left) is { } left && FoldStringLiterals(binary.Right) is { } right
                    ? left + right
                    : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Finds the static string property contributing <paramref name="memberName"/>, nearest
    /// declaration first.
    /// </summary>
    /// <remarks>
    /// The base walk matters: an endpoint may inherit <c>Path</c> from a shared base class, and the
    /// nearest declaration is the one that wins at run time, so it must be the one read here.
    /// </remarks>
    private static IPropertySymbol? FindStaticStringProperty(INamedTypeSymbol symbol, string memberName)
    {
        foreach (var type in symbol.GetAllBaseTypes())
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IPropertySymbol property && IsStaticStringProperty(property, memberName))
                    return property;
            }
        }

        return null;
    }

    /// <summary>A static string property named <paramref name="memberName"/>, or an explicit implementation named <c>Ns.IEndpointBase.Path</c>.</summary>
    private static bool IsStaticStringProperty(IPropertySymbol property, string memberName) =>
        property.IsStatic &&
        property.Type.SpecialType == SpecialType.System_String &&
        (property.Name == memberName || property.Name.EndsWith("." + memberName, StringComparison.Ordinal));

    /// <summary>
    /// Picks the expression a property's value comes from, across every spelling that can carry a
    /// constant.
    /// </summary>
    /// <remarks>
    /// Recoverable: <c>=&gt; "/x"</c>, <c>{ get; } = "/x";</c>, <c>{ get =&gt; "/x"; }</c> and
    /// <c>{ get { return "/x"; } }</c> — each of which then folds through
    /// <c>GetConstantValue</c>, so a <c>const</c>, a concatenation of <c>const</c>s, a
    /// <c>nameof</c> and a raw string literal all work.
    /// <para>
    /// Deliberately not handled: a getter with more than one statement. There is no single
    /// expression to fold, and guessing at the first <c>return</c> of a branching getter would
    /// produce a route the endpoint does not actually answer on — worse than admitting ignorance.
    /// </para>
    /// </remarks>
    internal static ExpressionSyntax? ValueExpressionOf(SyntaxNode node)
    {
        if (node is not PropertyDeclarationSyntax declaration) return null;

        if (declaration.ExpressionBody?.Expression is { } arrowBody) return arrowBody;
        if (declaration.Initializer?.Value is { } initializer) return initializer;

        var getter = declaration.AccessorList?.Accessors
            .FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));

        if (getter is null) return null;
        if (getter.ExpressionBody?.Expression is { } getterArrow) return getterArrow;
        if (getter.Body is null) return null;

        // Not a list pattern: this assembly targets netstandard2.0, where System.Index does not
        // exist, so `is [X]` fails to compile with CS0518 rather than anything that names the
        // real cause.
        var statements = getter.Body.Statements;
        if (statements.Count != 1) return null;

        return statements[0] is ReturnStatementSyntax { Expression: { } returned }
            ? returned
            : null;
    }
}
