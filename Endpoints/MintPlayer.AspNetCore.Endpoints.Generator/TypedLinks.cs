using Microsoft.CodeAnalysis.CSharp;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// The partial-method hook through which <c>EndpointOpenApi.g.cs</c> keeps <c>operationId</c>s
/// unique for an endpoint that answers several HTTP methods.
/// </summary>
/// <remarks>
/// The framework takes a named endpoint's <c>operationId</c> straight from its endpoint name, so a
/// <c>MapMethods</c> endpoint answering <c>HEAD</c> and <c>OPTIONS</c> would document two operations
/// with the same id — which makes the document invalid, and a client generator emits two methods
/// with one name. Such an endpoint gets <c>{Name}{Method}</c> instead (<c>PreflightEndpointHead</c>).
/// An endpoint whose <c>Methods</c> the generator cannot read gets the hook too; the transformer
/// checks the real method count at run time, so a single-method one keeps the plain name.
/// </remarks>
internal static class TypedLinkHooks
{
    public static string HookName(int index) => $"OnMultiMethodEndpointNamed{index}";

    public static bool NeedsOperationIdPerMethod(EndpointInfo endpoint) =>
        endpoint.KnownMethods is not { } known || MethodsLiteral.Decode(known).Length > 1;
}

/// <summary>One parameter of a typed-link method.</summary>
internal sealed class TypedLinkParameter
{
    public TypedLinkParameter(string identifier, string type, bool isOptional, bool hasDefault, string key)
    {
        Identifier = identifier;
        Type = type;
        IsOptional = isOptional;
        HasDefault = hasDefault;
        Key = key;
    }

    /// <summary>The C# parameter name, already <c>@</c>-escaped where it is a keyword.</summary>
    public string Identifier { get; }

    /// <summary>The parameter type as emitted, <c>?</c> included for an optional parameter.</summary>
    public string Type { get; }

    /// <summary>A null argument leaves the value out; the method adds it only when non-null.</summary>
    public bool IsOptional { get; }

    /// <summary>
    /// Emitted with <c>= null</c>. An optional parameter only gets a default when every parameter
    /// after it is optional too — C# requires it (CS1737) — so an optional token in the middle of the
    /// template, such as <c>/{lang=en}/items/{id}</c>, is nullable but must be passed explicitly.
    /// </summary>
    public bool HasDefault { get; }

    /// <summary>The route value or query key the argument is stored under.</summary>
    public string Key { get; }
}

/// <summary>One typed-link method, with its <c>…Template</c> constant.</summary>
internal sealed class TypedLink
{
    public TypedLink(EndpointInfo endpoint, string methodName, string template, List<TypedLinkParameter> parameters)
    {
        Endpoint = endpoint;
        MethodName = methodName;
        Template = template;
        Parameters = parameters;
    }

    public EndpointInfo Endpoint { get; }

    /// <summary>The endpoint's effective name, <c>@</c>-escaped if it is a keyword.</summary>
    public string MethodName { get; }

    /// <summary>The name of the constant holding <see cref="Template"/>.</summary>
    public string TemplateConstName => MethodName.TrimStart('@') + "Template";

    /// <summary>The composed route: every group prefix plus the endpoint's own <c>Path</c>.</summary>
    public string Template { get; }

    public List<TypedLinkParameter> Parameters { get; }
}

/// <summary>A static class of the <c>Routes</c> tree: <c>Routes</c> itself, or one per group.</summary>
internal sealed class TypedLinkNode
{
    public TypedLinkNode(string? groupFqn) => GroupFqn = groupFqn;

    /// <summary>The group this class mirrors, or null for <c>Routes</c>.</summary>
    public string? GroupFqn { get; }

    public string ClassName { get; set; } = TypedLinks.RootClassName;
    public List<TypedLinkNode> Children { get; } = new();
    public List<TypedLink> Links { get; } = new();
}

/// <summary>
/// Decides the shape of the generated <c>Routes</c> class (PRD R7.1): which endpoints get a link,
/// what every class, method and parameter is called, and the parameter types.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never a wrong link.</b> An endpoint is left out, rather than emitted with a guess, when its
/// composed route is unknown (a computed <c>Path</c> or <c>Prefix</c> — MPEP011 already says so),
/// when it is an MPEP012 duplicate (its name is not its route name), or when its name cannot be a
/// method name in its class (not an identifier, or taken by the class itself, a nested group class
/// or another link's <c>…Template</c> constant). Everything else about the file still compiles.
/// </para>
/// <para>
/// <b>Names are resolved per class, and deterministically.</b> A group's class is its type name
/// with one trailing <c>Api</c> or <c>Group</c> removed (<c>UsersApi</c> → <c>Users</c>). The full
/// name is kept when removing the suffix leaves nothing, or produces a name another sibling
/// strips to or is called, or a name a link in the same class uses. Should even the full name be
/// taken (two groups with the same name in different namespaces under one parent), a
/// <c>Routes</c> suffix, then a counter, makes it unique — ordinal order decides who keeps the
/// plain name, so the output is stable across runs.
/// </para>
/// </remarks>
internal static class TypedLinks
{
    public const string RootClassName = "Routes";

    public static TypedLinkNode Build(EndpointMappingPlan plan)
    {
        var root = new TypedLinkNode(null);
        var nodes = new Dictionary<string, TypedLinkNode>(StringComparer.Ordinal);

        foreach (var endpoint in plan.MappableEndpoints)
        {
            var template = plan.ComposedRoutes[endpoint.FullyQualifiedName];
            if (template is null || !plan.IsNamed(endpoint)) continue;

            var methodName = EscapeIdentifier(endpoint.EffectiveDescriptorName);
            if (methodName is null) continue;

            var container = root;
            foreach (var groupFqn in plan.GroupChains[endpoint.FullyQualifiedName])
            {
                if (!nodes.TryGetValue(groupFqn, out var node))
                {
                    nodes[groupFqn] = node = new TypedLinkNode(groupFqn);
                    container.Children.Add(node);
                }
                container = node;
            }

            container.Links.Add(new TypedLink(endpoint, methodName, template, ParametersOf(endpoint, template)));
        }

        ResolveNames(root);
        return root;
    }

    /// <summary>True when the tree has anything to emit.</summary>
    public static bool IsEmpty(TypedLinkNode node) =>
        node.Links.Count == 0 && node.Children.All(IsEmpty);

    private static void ResolveNames(TypedLinkNode node)
    {
        // Links first: a link that cannot be a member of this class is dropped here.
        var taken = new HashSet<string>(StringComparer.Ordinal) { node.ClassName };
        node.Links.RemoveAll(link =>
        {
            var name = link.MethodName.TrimStart('@');
            if (taken.Contains(name) || taken.Contains(link.TemplateConstName)) return true;

            taken.Add(name);
            taken.Add(link.TemplateConstName);
            return false;
        });

        node.Children.Sort((a, b) => string.CompareOrdinal(a.GroupFqn, b.GroupFqn));

        var simpleNames = node.Children.Select(child => SimpleNameOf(child.GroupFqn!)).ToList();
        var strippedNames = simpleNames.Select(Strip).ToList();

        for (var i = 0; i < node.Children.Count; i++)
        {
            var simple = simpleNames[i];
            var stripped = strippedNames[i];

            var strippedIsShared = false;
            for (var j = 0; j < node.Children.Count; j++)
            {
                if (j != i && (strippedNames[j] == stripped || simpleNames[j] == stripped))
                    strippedIsShared = true;
            }

            var preferred = stripped.Length > 0 && !strippedIsShared ? stripped : simple;

            var candidate = preferred;
            if (!IsUsableTypeName(candidate) || taken.Contains(candidate)) candidate = simple;
            if (!IsUsableTypeName(candidate) || taken.Contains(candidate)) candidate = simple + RootClassName;
            for (var counter = 2; taken.Contains(candidate); counter++)
                candidate = simple + RootClassName + counter;

            taken.Add(candidate);
            node.Children[i].ClassName = candidate;
        }

        foreach (var child in node.Children)
            ResolveNames(child);
    }

    /// <summary><c>UsersApi</c> → <c>Users</c>, <c>ApiGroup</c> → <c>Api</c>; one suffix, once.</summary>
    internal static string Strip(string simpleName)
    {
        if (simpleName.EndsWith("Group", StringComparison.Ordinal))
            return simpleName.Substring(0, simpleName.Length - "Group".Length);
        if (simpleName.EndsWith("Api", StringComparison.Ordinal))
            return simpleName.Substring(0, simpleName.Length - "Api".Length);
        return simpleName;
    }

    /// <summary><c>global::Ns.Outer.UsersApi</c> → <c>UsersApi</c>; generic arguments are cut first.</summary>
    internal static string SimpleNameOf(string fullyQualifiedName)
    {
        var generic = fullyQualifiedName.IndexOf('<');
        var name = generic < 0 ? fullyQualifiedName : fullyQualifiedName.Substring(0, generic);
        var cut = Math.Max(name.LastIndexOf('.'), name.LastIndexOf(':'));
        return cut < 0 ? name : name.Substring(cut + 1);
    }

    private static bool IsUsableTypeName(string name) =>
        name.Length > 0 && SyntaxFacts.IsValidIdentifier(name) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;

    /// <summary>The name as a C# identifier, <c>@</c>-escaped if it is a keyword, or null if it cannot be one.</summary>
    private static string? EscapeIdentifier(string name)
    {
        if (!SyntaxFacts.IsValidIdentifier(name)) return null;
        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
    }

    private static List<TypedLinkParameter> ParametersOf(EndpointInfo endpoint, string template)
    {
        var bound = ShadowParameters.Bound(endpoint);
        var tokens = RouteTokens(template);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var draft = new List<(string Identifier, string Type, bool IsOptional, string Key)>();

        foreach (var token in tokens)
        {
            var property = bound.FirstOrDefault(candidate =>
                candidate.Source == BoundSource.Route &&
                string.Equals(candidate.Key, token.Name, StringComparison.OrdinalIgnoreCase));

            // A catch-all is optional to the link generator too: an omitted {**path} generates the
            // route without it rather than failing.
            var isOptional = token.IsOptional || token.HasDefault || token.IsCatchAll;
            var type = property?.ConversionTypeFqn ?? "string";
            draft.Add((Claim(token.Name, identifiers), isOptional ? type + "?" : type, isOptional, token.Name));
        }

        foreach (var property in bound)
        {
            if (property.Source != BoundSource.Query) continue;

            // A query key that is also a route token would overwrite the route value.
            if (tokens.Any(token => string.Equals(token.Name, property.Key, StringComparison.OrdinalIgnoreCase))) continue;

            draft.Add((Claim(CamelCase(property.Name), identifiers), property.ConversionTypeFqn + "?", true, property.Key));
        }

        var parameters = new List<TypedLinkParameter>(draft.Count);
        for (var i = 0; i < draft.Count; i++)
        {
            var hasDefault = true;
            for (var j = i; j < draft.Count && hasDefault; j++)
                hasDefault = draft[j].IsOptional;

            parameters.Add(new TypedLinkParameter(draft[i].Identifier, draft[i].Type, draft[i].IsOptional, hasDefault, draft[i].Key));
        }

        return parameters;
    }

    private static string CamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    /// <summary>
    /// A unique parameter identifier for <paramref name="wanted"/>: invalid characters become
    /// <c>_</c>, a keyword is <c>@</c>-escaped, and a clash gets a numeric suffix.
    /// </summary>
    private static string Claim(string wanted, HashSet<string> taken)
    {
        var chars = wanted.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var name = new string(chars);
        if (name.Length == 0 || !SyntaxFacts.IsIdentifierStartCharacter(name[0])) name = "_" + name;

        // "__values" is the method's own local.
        if (name == "__values") name = "__values_";

        var candidate = name;
        for (var counter = 2; !taken.Add(candidate); counter++)
            candidate = name + counter;

        return SyntaxFacts.GetKeywordKind(candidate) == SyntaxKind.None ? candidate : "@" + candidate;
    }

    /// <summary>A route token: its name and the markers that make it optional.</summary>
    internal readonly struct RouteToken
    {
        public RouteToken(string name, bool isOptional, bool hasDefault, bool isCatchAll)
        {
            Name = name;
            IsOptional = isOptional;
            HasDefault = hasDefault;
            IsCatchAll = isCatchAll;
        }

        public string Name { get; }
        public bool IsOptional { get; }
        public bool HasDefault { get; }
        public bool IsCatchAll { get; }
    }

    /// <summary>
    /// The tokens of <paramref name="route"/> in template order, each once, with what makes it
    /// optional: a trailing <c>?</c>, a <c>=default</c>, or a catch-all star.
    /// </summary>
    /// <remarks>
    /// The same scan as <see cref="ComposedRoute.Parameters"/> — a doubled brace is a literal — but
    /// it keeps the markers that method discards. A <c>=</c> or <c>?</c> inside a constraint's
    /// parentheses (a regex) is not a marker.
    /// </remarks>
    internal static List<RouteToken> RouteTokens(string route)
    {
        var tokens = new List<RouteToken>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < route.Length; i++)
        {
            if (route[i] != '{') continue;

            if (i + 1 < route.Length && route[i + 1] == '{')
            {
                i++;
                continue;
            }

            var close = route.IndexOf('}', i + 1);
            if (close < 0) break;

            var body = route.Substring(i + 1, close - i - 1);
            i = close;

            var isCatchAll = body.StartsWith("*", StringComparison.Ordinal);
            body = body.TrimStart('*');

            var nameEnd = body.IndexOfAny(new[] { ':', '=', '?' });
            var name = nameEnd < 0 ? body : body.Substring(0, nameEnd);
            if (name.Length == 0 || !seen.Add(name)) continue;

            var rest = nameEnd < 0 ? "" : body.Substring(nameEnd);
            var hasDefault = false;
            var isOptional = false;
            var depth = 0;
            for (var j = 0; j < rest.Length; j++)
            {
                var c = rest[j];
                if (c == '(') depth++;
                else if (c == ')' && depth > 0) depth--;
                else if (c == '=' && depth == 0)
                {
                    hasDefault = true;
                    break;
                }
                else if (c == '?' && depth == 0 && j == rest.Length - 1)
                {
                    isOptional = true;
                }
            }

            tokens.Add(new RouteToken(name, isOptional, hasDefault, isCatchAll));
        }

        return tokens;
    }
}
