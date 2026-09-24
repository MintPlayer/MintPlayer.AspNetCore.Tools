using Microsoft.CodeAnalysis;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Why a request type would be validated if it were marked <c>[ValidatableType]</c> — and is not.
/// </summary>
/// <remarks>
/// A value, not a symbol, so <see cref="EndpointInfo"/> stays value-equal and the incremental cache
/// keeps hitting. <see cref="None"/> covers every case where MPEP015 must stay silent, including a
/// consumer whose compilation cannot resolve <c>ValidatableTypeAttribute</c> at all.
/// </remarks>
[Flags]
internal enum RequestValidationGap
{
    None = 0,

    /// <summary>A property or positional record parameter carries a <c>ValidationAttribute</c>.</summary>
    ValidationAttributes = 1,

    /// <summary>The type implements <c>IValidatableObject</c>.</summary>
    ValidatableObject = 2,
}

internal static class RequestValidationGaps
{
    private const string ValidatableTypeAttribute = "Microsoft.Extensions.Validation.ValidatableTypeAttribute";
    private const string ValidationAttribute = "System.ComponentModel.DataAnnotations.ValidationAttribute";
    private const string ValidatableObject = "System.ComponentModel.DataAnnotations.IValidatableObject";

    /// <summary>
    /// Inspects the request type of a typed endpoint for the MPEP015 shape: validation rules, but no
    /// <c>[ValidatableType]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the request type's own members, including inherited properties, are read. Nested complex
    /// members are not walked: the validation generator recurses into them from a marked root, so
    /// the one attribute that matters is always on the root.
    /// </para>
    /// <para>
    /// Positional record parameters are read as well as properties, because an attribute written
    /// without a target in <c>record R([Required] string Name)</c> lands on the parameter, not the
    /// property — and the validation generator honours it there (measured on net10.0 and net11.0).
    /// Missing it would leave the most common record spelling undiagnosed.
    /// </para>
    /// </remarks>
    public static RequestValidationGap Inspect(ITypeSymbol requestType, Compilation compilation)
    {
        if (requestType is not INamedTypeSymbol named)
            return RequestValidationGap.None;

        var marker = compilation.GetTypeByMetadataName(ValidatableTypeAttribute);
        if (marker is null)
            return RequestValidationGap.None;

        if (named.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker)))
            return RequestValidationGap.None;

        var validationAttribute = compilation.GetTypeByMetadataName(ValidationAttribute);
        var validatableObject = compilation.GetTypeByMetadataName(ValidatableObject);

        var gap = RequestValidationGap.None;

        if (validatableObject is not null &&
            named.AllInterfaces.Any(iface => SymbolEqualityComparer.Default.Equals(iface, validatableObject)))
        {
            gap |= RequestValidationGap.ValidatableObject;
        }

        if (validationAttribute is not null && HasValidationAttribute(named, validationAttribute))
            gap |= RequestValidationGap.ValidationAttributes;

        return gap;
    }

    private static bool HasValidationAttribute(INamedTypeSymbol type, INamedTypeSymbol validationAttribute)
    {
        for (var current = type; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { IsStatic: false } property && AnyIsValidation(property.GetAttributes(), validationAttribute))
                    return true;
            }

            if (!current.IsRecord)
                continue;

            foreach (var constructor in current.InstanceConstructors)
            {
                if (IsPositional(current, constructor) &&
                    constructor.Parameters.Any(parameter => AnyIsValidation(parameter.GetAttributes(), validationAttribute)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A record constructor whose every parameter names a property of the record — the primary
    /// constructor, recognised without syntax so a record from a referenced assembly works too.
    /// </summary>
    /// <remarks>
    /// The compiler-generated copy constructor takes the record itself and is excluded by that.
    /// </remarks>
    private static bool IsPositional(INamedTypeSymbol record, IMethodSymbol constructor) =>
        constructor.Parameters.Length > 0 &&
        constructor.Parameters.All(parameter =>
            !SymbolEqualityComparer.Default.Equals(parameter.Type, record) &&
            record.GetMembers(parameter.Name).OfType<IPropertySymbol>().Any());

    private static bool AnyIsValidation(IEnumerable<AttributeData> attributes, INamedTypeSymbol validationAttribute)
    {
        foreach (var attribute in attributes)
        {
            for (var type = attribute.AttributeClass; type is not null; type = type.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(type, validationAttribute))
                    return true;
            }
        }

        return false;
    }
}
