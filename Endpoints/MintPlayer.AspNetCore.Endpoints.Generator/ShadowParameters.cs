using Microsoft.CodeAnalysis.CSharp;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// One member of an endpoint's shadow parameter type: a <c>string?</c> property the framework
/// binds and documents, and nothing reads.
/// </summary>
internal sealed class ShadowMember
{
    public ShadowMember(string identifier, BoundSource source, string key, string? nameOverride, string? schemaShape, string? enumTypeFqn)
    {
        Identifier = identifier;
        Source = source;
        Key = key;
        NameOverride = nameOverride;
        SchemaShape = schemaShape;
        EnumTypeFqn = enumTypeFqn;
    }

    /// <summary>The C# member name, without the <c>@</c> escape the emitter always adds.</summary>
    public string Identifier { get; }

    public BoundSource Source { get; }

    /// <summary>The route token or query key, as the library's binder reads it.</summary>
    public string Key { get; }

    /// <summary>The attribute's <c>Name</c>, or null to leave it unset.</summary>
    public string? NameOverride { get; }

    /// <summary>
    /// Which schema the transformer restores — one of the shapes the emitted
    /// <c>ParameterSchema</c> switch knows — or null to leave the framework's <c>string</c>.
    /// </summary>
    public string? SchemaShape { get; }

    /// <summary>The enum type, for an enum member; its schema is built by <c>EnumParameterSchema&lt;T&gt;</c>.</summary>
    public string? EnumTypeFqn { get; }

    /// <summary>True when the transformer has something to restore for this member.</summary>
    public bool HasTypedSchema => SchemaShape is not null || EnumTypeFqn is not null;
}

/// <summary>
/// Decides the members of an endpoint's shadow parameter (PRD R4.1, S1).
/// </summary>
/// <remarks>
/// <para>
/// The generated request delegate takes only <c>HttpContext</c>, and ApiExplorer builds
/// parameters from delegate parameters alone — so without this, every templated route is
/// documented with no path parameter, which makes the OpenAPI document invalid. The shadow is an
/// extra <c>[AsParameters]</c> delegate parameter that exists purely to be seen.
/// </para>
/// <para>
/// <b>Every member is <c>string?</c>, whatever the property's type.</b> A typed member makes the
/// framework parse first, so <c>/users/abc</c> became a 400 with a zero-length body and the
/// library's message never ran (spike S1). A string never fails to bind, so the library's own
/// binder still decides — and the typed schema is put back by the operation transformer instead.
/// Nullability is load-bearing too: a non-nullable member makes the framework 400 on a
/// <i>missing</i> value before the endpoint runs.
/// </para>
/// <para>
/// Two sources of members: the endpoint's bound properties — only those the generator actually
/// emits a binder for, since documenting a property nothing binds would be a lie — and then every
/// route token in the composed template that no bound property covers. The second matters as
/// much as the first: a raw <c>/api/{**path}</c> endpoint binds nothing, and its path key is just
/// as invalid without a parameter.
/// </para>
/// </remarks>
internal static class ShadowParameters
{
    /// <summary>The name of the shadow type emitted for the endpoint at factory index <paramref name="index"/>.</summary>
    /// <remarks>The index keeps two endpoints with the same simple name in different namespaces apart.</remarks>
    public static string TypeName(EndpointInfo endpoint, int index) => $"{endpoint.ClassName}_Parameters{index}";

    /// <summary>
    /// The name of the partial-method hook the mapping file calls for the endpoint at
    /// <paramref name="index"/>, and the OpenAPI file implements.
    /// </summary>
    public static string HookName(int index) => $"OnEndpointMapped{index}";

    /// <summary>
    /// The bound properties generated code can assign: a supported type and a usable setter.
    /// Everything else has a diagnostic and no emitted statement.
    /// </summary>
    public static List<BoundProperty> Bindable(EndpointInfo endpoint) =>
        endpoint.BoundProperties
            .Where(property => property.Kind != BoundKind.Unsupported && property.IsSettable)
            .ToList();

    /// <summary>
    /// True when the generator emits a binder for this endpoint at all — the same gates
    /// <c>EmitPartial</c> applies. A non-partial endpoint, one in a non-partial container, or one
    /// whose own base class never reaches the library's has a diagnostic instead of a binder.
    /// </summary>
    public static bool EmitsBinder(EndpointInfo endpoint) =>
        endpoint.IsPartial &&
        endpoint.PathSpec is not { AllPartial: false } &&
        (endpoint.Level == EndpointLevel.Raw || !endpoint.HasExistingBaseClass || endpoint.BaseChainReachesEndpointBase);

    /// <summary>The properties the library really binds for this endpoint, in declaration order.</summary>
    public static List<BoundProperty> Bound(EndpointInfo endpoint) =>
        EmitsBinder(endpoint) ? Bindable(endpoint) : new List<BoundProperty>();

    public static List<ShadowMember> For(EndpointInfo endpoint, string? composedRoute) =>
        For(Bound(endpoint), composedRoute);

    public static List<ShadowMember> For(IReadOnlyList<BoundProperty> bound, string? composedRoute)
    {
        var tokens = composedRoute is null ? new List<string>() : ComposedRoute.Parameters(composedRoute);
        var members = new List<ShadowMember>();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var coveredTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in bound)
        {
            // Route values are matched case-insensitively at run time, so is the token.
            string? token = null;
            if (property.Source == BoundSource.Route)
            {
                token = tokens.FirstOrDefault(t => string.Equals(t, property.Key, StringComparison.OrdinalIgnoreCase));
                if (token is not null) coveredTokens.Add(token);
            }

            // Name stays unset unless the consumer supplied one: measured, an unnamed [FromRoute]
            // member called Id against {id} is documented as "id" — ApiExplorer takes the
            // template's spelling. An explicit name is honoured, but corrected to the template's
            // spelling, since Name = "Id" against {id} documents "Id" and Swagger UI then cannot
            // match it to the path.
            var explicitName = !string.Equals(property.Key, property.Name, StringComparison.Ordinal);
            var nameOverride = explicitName ? token ?? property.Key : null;

            var identifier = Claim(property.Name, identifiers, ref nameOverride, token ?? property.Key);

            members.Add(new ShadowMember(
                identifier,
                property.Source,
                property.Key,
                nameOverride,
                property.Kind == BoundKind.Parsable ? SchemaShapeOf(property.ConversionTypeFqn) : null,
                property.Kind == BoundKind.Enum ? property.ConversionTypeFqn : null));
        }

        foreach (var token in tokens)
        {
            if (!coveredTokens.Add(token)) continue;

            string? nameOverride = null;
            var identifier = Claim(token, identifiers, ref nameOverride, token);
            members.Add(new ShadowMember(identifier, BoundSource.Route, token, nameOverride, null, null));
        }

        return members;
    }

    /// <summary>
    /// A unique, valid member identifier for <paramref name="wanted"/>. When the wanted name cannot
    /// be used — not an identifier, or already taken — a positional one is used instead and the
    /// documented name moves to <c>Name</c>, since the member name no longer carries it.
    /// </summary>
    private static string Claim(string wanted, HashSet<string> taken, ref string? nameOverride, string documentedName)
    {
        if (SyntaxFacts.IsValidIdentifier(wanted) && taken.Add(wanted))
            return wanted;

        var fallback = "_" + taken.Count;
        while (!taken.Add(fallback)) fallback = "_" + fallback;
        nameOverride ??= documentedName;
        return fallback;
    }

    /// <summary>
    /// The schema ASP.NET Core documents for a typed parameter of this type, as a shape the emitted
    /// <c>ParameterSchema</c> switch rebuilds — or null to leave the member documented as a string.
    /// </summary>
    /// <remarks>
    /// Every row was read from the document ASP.NET Core 10 and 11 produce for a typed
    /// <c>[AsParameters]</c> member, identical on both. Note the numeric rows are not plain
    /// <c>type: integer</c>: the framework emits a <c>pattern</c> plus <c>type: [integer, string]</c>,
    /// and a transformer that wrote the obvious shape was only caught by S1's byte-diff. A type not
    /// listed here — a consumer's own <c>IParsable</c>, say — is left as a string rather than
    /// guessed at.
    /// <para>
    /// Both spellings are accepted because <c>FullyQualifiedFormat</c> renders special types as
    /// keywords (<c>int</c>) and everything else qualified (<c>global::System.Guid</c>).
    /// </para>
    /// </remarks>
    internal static string? SchemaShapeOf(string conversionTypeFqn) => conversionTypeFqn switch
    {
        "int" or "global::System.Int32" => "int32",
        "long" or "global::System.Int64" => "int64",
        "short" or "global::System.Int16" => "int16",
        "byte" or "global::System.Byte" => "uint8",
        "ushort" or "global::System.UInt16" => "uint16",
        "uint" or "global::System.UInt32" => "uint32",
        "ulong" or "global::System.UInt64" => "uint64",
        "sbyte" or "global::System.SByte" or "global::System.Int128" => "integer",
        "float" or "global::System.Single" => "float",
        "double" or "global::System.Double" => "double",
        "decimal" or "global::System.Decimal" => "decimal",
        "global::System.Half" => "number",
        "bool" or "global::System.Boolean" => "boolean",
        "char" or "global::System.Char" => "char",
        "global::System.Guid" => "uuid",
        "global::System.DateTime" or "global::System.DateTimeOffset" => "date-time",
        "global::System.DateOnly" => "date",
        "global::System.TimeOnly" => "time",
        "global::System.TimeSpan" => "timespan",
        _ => null,
    };
}
