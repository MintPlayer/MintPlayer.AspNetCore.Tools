using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

internal enum BoundSource { Route, Query }

/// <summary>Which <c>ParameterBinding</c> family converts the value.</summary>
/// <remarks>
/// A single overload set is impossible — generic constraints are not part of a C# signature, so
/// the <c>IParsable&lt;T&gt;</c> and <c>struct, Enum</c> helpers would be <c>CS0111</c>
/// duplicates. So the type is classified here, at compile time, and each kind names its own
/// runtime method.
/// </remarks>
internal enum BoundKind
{
    /// <summary><c>string</c>. Checked first: <c>string</c> also implements <c>IParsable&lt;string&gt;</c>.</summary>
    String,

    /// <summary>A value type implementing <c>IParsable&lt;T&gt;</c>, or a reference type when required.</summary>
    Parsable,

    /// <summary>An optional reference type implementing <c>IParsable&lt;T&gt;</c>.</summary>
    ParsableReference,

    /// <summary>Any enum.</summary>
    Enum,

    /// <summary>None of the above. MPEP013; nothing is emitted for it.</summary>
    Unsupported,
}

/// <summary>
/// One <c>[RouteParam]</c>/<c>[QueryParam]</c> property, reduced to strings and flags so the
/// incremental pipeline can compare it by value.
/// </summary>
[GenerateEquality]
internal sealed partial class BoundProperty
{
    public BoundProperty(string name, string key, BoundSource source, BoundKind kind, string conversionTypeFqn,
        string declaredTypeDisplay, bool isOptional, bool hasInitializer, bool isSettable, LocationKey? location)
    {
        Name = name;
        Key = key;
        Source = source;
        Kind = kind;
        ConversionTypeFqn = conversionTypeFqn;
        DeclaredTypeDisplay = declaredTypeDisplay;
        IsOptional = isOptional;
        HasInitializer = hasInitializer;
        IsSettable = isSettable;
        Location = location;
    }

    /// <summary>The property name — what generated code assigns.</summary>
    public string Name { get; }

    /// <summary>The route token or query key read — the attribute's name, else the property's.</summary>
    public string Key { get; }

    public BoundSource Source { get; }
    public BoundKind Kind { get; }

    /// <summary>
    /// The type argument handed to the conversion helper: the underlying type for a
    /// <c>Nullable&lt;T&gt;</c>, the annotation-free type otherwise.
    /// </summary>
    public string ConversionTypeFqn { get; }

    /// <summary>The type as the consumer wrote it, for diagnostic messages.</summary>
    public string DeclaredTypeDisplay { get; }

    /// <summary>A nullable property: absent binds to <see langword="null"/>.</summary>
    public bool IsOptional { get; }

    /// <summary>
    /// The property has an initializer, which is its default: generated code assigns only when a
    /// value was supplied. Read from syntax — an initializer is not visible on the symbol.
    /// </summary>
    public bool HasInitializer { get; }

    /// <summary>
    /// Generated code can assign it: a setter that is not <c>init</c>, and not <c>private</c> when the
    /// property is declared on a base class. MPEP020 otherwise.
    /// </summary>
    public bool IsSettable { get; }

    /// <inheritdoc cref="EndpointInfo.Location"/>
    public LocationKey? Location { get; }

    /// <summary>This property without its location, for the producers (PRD addendum 2, D20).</summary>
    public BoundProperty WithoutLocation() => Location is null
        ? this
        : new BoundProperty(Name, Key, Source, Kind, ConversionTypeFqn, DeclaredTypeDisplay, IsOptional, HasInitializer, IsSettable, location: null);
}

/// <summary>Collects an endpoint's bound properties from its symbol.</summary>
internal static class BoundProperties
{
    /// <summary>
    /// Every bound property the endpoint has, own and inherited, nearest declaration first.
    /// </summary>
    /// <remarks>
    /// Read from the <b>symbol</b>, walking the base chain, never from the syntax node that
    /// triggered the transform: a partial class split across files is one symbol with several
    /// declarations, and reading only the triggering one would produce contradictory models
    /// depending on which file changed. An inherited property matters too — a shared base class
    /// can carry <c>[RouteParam] public int Id</c>, and the generated binder in the concrete
    /// endpoint is what assigns it, since an abstract base never gets a partial of its own.
    /// </remarks>
    public static ImmutableArray<BoundProperty> Collect(INamedTypeSymbol endpoint, string endpointsNamespace, CancellationToken ct)
    {
        // Syntax first (PRD addendum 2, D19): with no base class, only the class's own property
        // declarations can carry [RouteParam]/[QueryParam], and a property declared without an attribute
        // list has no attributes at all. Then the member list need not be bound.
        if (endpoint.BaseType is null or { SpecialType: SpecialType.System_Object } && !MayHaveAttributedProperty(endpoint, ct))
            return ImmutableArray<BoundProperty>.Empty;

        var builder = ImmutableArray.CreateBuilder<BoundProperty>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in endpoint.GetAllBaseTypes())
        {
            if (type.SpecialType == SpecialType.System_Object) break;

            foreach (var member in type.GetMembers())
            {
                ct.ThrowIfCancellationRequested();

                if (member is not IPropertySymbol { IsStatic: false, IsIndexer: false } property) continue;
                if (!seen.Add(property.Name)) continue;   // nearest declaration wins

                var binding = ReadBinding(property, endpointsNamespace);
                if (binding is null) continue;

                builder.Add(Describe(property, binding.Value.Source, binding.Value.Name, declaredOnEndpoint: SymbolEqualityComparer.Default.Equals(type, endpoint), ct));
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// False only when every declaration of the class is a class declaration and none of its property
    /// declarations has an attribute list.
    /// </summary>
    private static bool MayHaveAttributedProperty(INamedTypeSymbol endpoint, CancellationToken ct)
    {
        var references = endpoint.OriginalDefinition.DeclaringSyntaxReferences;
        if (references.IsEmpty) return true;

        foreach (var reference in references)
        {
            if (reference.GetSyntax(ct) is not ClassDeclarationSyntax declaration) return true;

            foreach (var member in declaration.Members)
            {
                if (member is PropertyDeclarationSyntax { AttributeLists.Count: > 0 }) return true;
            }
        }

        return false;
    }

    private static (BoundSource Source, string? Name)? ReadBinding(IPropertySymbol property, string endpointsNamespace)
    {
        foreach (var attribute in property.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null || !SymbolNames.IsNamespace(attributeClass.ContainingNamespace, endpointsNamespace)) continue;

            BoundSource? source = attributeClass.Name switch
            {
                "RouteParamAttribute" => BoundSource.Route,
                "QueryParamAttribute" => BoundSource.Query,
                _ => null,
            };
            if (source is null) continue;

            var name = attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string explicitName
                ? explicitName
                : null;

            return (source.Value, name);
        }

        return null;
    }

    private static BoundProperty Describe(IPropertySymbol property, BoundSource source, string? explicitName, bool declaredOnEndpoint, CancellationToken ct)
    {
        var type = property.Type;

        // Nullable<T>: optional, and the helper converts the underlying T.
        var underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableValue
            ? nullableValue.TypeArguments[0]
            : null;

        var isOptional = underlying is not null || type.NullableAnnotation == NullableAnnotation.Annotated;
        var conversionType = underlying ?? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        var kind = Classify(conversionType, isReferenceOptional: underlying is null && isOptional);

        var setter = property.SetMethod;
        var isSettable = setter is { IsInitOnly: false } &&
                         (declaredOnEndpoint || setter.DeclaredAccessibility != Accessibility.Private);

        var hasInitializer = property.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(ct))
            .OfType<PropertyDeclarationSyntax>()
            .Any(declaration => declaration.Initializer is not null);

        return new BoundProperty(
            property.Name,
            explicitName ?? property.Name,
            source,
            kind,
            conversionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            isOptional,
            hasInitializer,
            isSettable,
            property.FromSymbol().AsKey());
    }

    private static BoundKind Classify(ITypeSymbol type, bool isReferenceOptional)
    {
        if (type.SpecialType == SpecialType.System_String) return BoundKind.String;
        if (type.TypeKind == TypeKind.Enum) return BoundKind.Enum;
        if (!IsSelfParsable(type)) return BoundKind.Unsupported;

        return isReferenceOptional ? BoundKind.ParsableReference : BoundKind.Parsable;
    }

    /// <summary>
    /// True when <paramref name="type"/> implements <c>System.IParsable&lt;T&gt;</c> of itself.
    /// </summary>
    /// <remarks>
    /// Enumerated rather than constructed: <c>IParsable&lt;TSelf&gt;</c> is self-constrained, so
    /// building the closed interface type to test assignability throws for any type that does not
    /// satisfy it — which is precisely the case being asked about.
    /// </remarks>
    private static bool IsSelfParsable(ITypeSymbol type) =>
        type.AllInterfaces.Any(i =>
            i.MetadataName == "IParsable`1" &&
            SymbolNames.IsNamespace(i.ContainingNamespace, "System") &&
            SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type));
}
