// This text is ALSO embedded in MintPlayer.AspNetCore.Endpoints.Generator and emitted into every
// typed-client project as EndpointClientUrl.g.cs, with only the namespace line changed. Keep it free
// of using directives and extension-method calls, and keep the namespace declaration exactly as it
// is — the generator replaces that line verbatim and fails its tests if it cannot find it.
#nullable enable

namespace MintPlayer.AspNetCore.Endpoints
{
    /// <summary>
    /// Substitutes route values into a route template exactly as ASP.NET Core's
    /// <c>LinkGenerator</c> would with default routing options, without needing one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One source, two compilations.</b> <c>EndpointRoute.ToString()</c> in the runtime library
    /// calls this, and the typed-client generator embeds this very file and emits it into the client
    /// project with only its namespace changed. A client cannot reference the runtime library — it
    /// is a Web SDK package, and its <c>FrameworkReference</c> fails a Blazor WebAssembly build
    /// (NETSDK1082, PRD R7.4) — so sharing the text is the only way the server's links and the
    /// client's URLs come from the same code. The 43-case battery that pins <c>ToString()</c> against
    /// a real <c>LinkGenerator</c> therefore pins the client too.
    /// </para>
    /// <para>
    /// That is also why the file is written like generated code: no <c>using</c> directives, every
    /// type <c>global::</c>-qualified, no extension-method calls and nothing newer than C# 9, so it
    /// compiles unchanged in whatever project the client generator runs in.
    /// </para>
    /// <para>
    /// A port of the parts of ASP.NET Core's <c>TemplateBinder</c> and <c>UriBuildingContext</c>
    /// that apply to a named link with no ambient values, kept structurally identical to the
    /// originals — the segment/buffer state machine included — because each odd-looking rule is
    /// observable: a trailing value equal to its default is dropped, an omitted optional token takes
    /// its slash with it, a <c>{id}.{format?}</c> drops the dot, a <c>{**path}</c> keeps its slashes
    /// and a <c>{*path}</c> escapes them, and a value compared with a default is compared as routing
    /// compares it (strings case-insensitively, anything else by <see cref="object.Equals(object)"/>).
    /// Values are encoded with <see cref="global::System.Text.Encodings.Web.UrlEncoder.Default"/>,
    /// the encoder routing uses — never <see cref="global::System.Uri.EscapeDataString(string)"/>,
    /// which escapes <c>! $ ( ) * , ; @</c> where routing does not.
    /// </para>
    /// </remarks>
    internal static class EndpointTemplateBinder
    {
        /// <summary>
        /// The path and query string for <paramref name="template"/> filled from
        /// <paramref name="values"/>, or <see langword="null"/> where <c>LinkGenerator</c> would
        /// return null: a required token with no value or an empty one, or a template this port does
        /// not understand.
        /// </summary>
        /// <param name="template">The route template, e.g. <c>/api/users/{id}</c>.</param>
        /// <param name="values">
        /// Route values, matched to tokens case-insensitively; a later key replaces an earlier one
        /// with the same name, as a <c>RouteValueDictionary</c> indexer would. Values that are not
        /// tokens become the query string, in order.
        /// </param>
        public static string? Bind(string template, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object?>> values)
        {
            var segments = Parse(template);
            if (segments is null) return null;

            var supplied = new Values();
            foreach (var pair in values)
                supplied.Set(pair.Key, pair.Value);

            var parameters = new global::System.Collections.Generic.List<Part>();
            foreach (var segment in segments)
            {
                foreach (var part in segment)
                {
                    if (part.IsParameter) parameters.Add(part);
                }
            }

            var parameterNames = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.OrdinalIgnoreCase);
            var defaults = new global::System.Collections.Generic.Dictionary<string, object?>(global::System.StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in parameters)
            {
                parameterNames.Add(parameter.Name);
                if (parameter.Default is not null)
                    defaults[parameter.Name] = parameter.Default;
            }

            // TemplateBinder.GetValues, with no ambient values and no filters.
            var accepted = new Values();
            foreach (var parameter in parameters)
            {
                if (accepted.ContainsKey(parameter.Name)) continue;

                object? value;
                if (supplied.TryGetValue(parameter.Name, out value) && IsNonEmpty(value))
                    accepted.Set(parameter.Name, value);
                else if (parameter.IsOptional || parameter.IsCatchAll)
                    continue;
                else if (parameter.Default is not null)
                    accepted.Set(parameter.Name, parameter.Default);
                else
                    return null;
            }

            foreach (var pair in supplied.Pairs)
            {
                if (!defaults.ContainsKey(pair.Key) && !accepted.ContainsKey(pair.Key) && !parameterNames.Contains(pair.Key))
                    accepted.Set(pair.Key, pair.Value);
            }

            // TemplateBinder.TryBindValuesCore.
            var context = new UriBuilder();
            foreach (var segment in segments)
            {
                for (var j = 0; j < segment.Count; j++)
                {
                    var part = segment[j];
                    if (part.IsLiteral || part.IsSeparator)
                    {
                        if (!context.Accept(part.Text, true)) return null;
                        continue;
                    }

                    var value = accepted.Remove(part.Name);

                    object? defaultValue;
                    var isSameAsDefault = defaults.TryGetValue(part.Name, out defaultValue) && RoutePartsEqual(value, defaultValue);
                    var converted = global::System.Convert.ToString(value, global::System.Globalization.CultureInfo.InvariantCulture);

                    if (isSameAsDefault)
                    {
                        if (!context.Buffer(converted)) return null;
                    }
                    else if (!context.Accept(converted, part.EncodeSlashes))
                    {
                        if (j != 0 && part.IsOptional && segment[j - 1].IsSeparator)
                            context.Remove();
                        else
                            return null;
                    }
                }

                context.EndSegment();
            }

            var query = new global::System.Text.StringBuilder();
            foreach (var pair in accepted.Pairs)
            {
                if (defaults.ContainsKey(pair.Key)) continue;

                var sequence = pair.Value as global::System.Collections.IEnumerable;
                if (sequence is not null && !(pair.Value is string))
                {
                    foreach (var item in sequence)
                        AppendQuery(query, pair.Key, item);
                }
                else
                {
                    AppendQuery(query, pair.Key, pair.Value);
                }
            }

            // UriHelper.BuildRelative: an empty path is "/".
            var path = context.Path;
            if (path.Length > 0 && path[0] != '/') path = "/" + path;
            if (path.Length == 0) path = "/";

            return path + query.ToString();
        }

        private static void AppendQuery(global::System.Text.StringBuilder query, string key, object? value)
        {
            var converted = global::System.Convert.ToString(value, global::System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(converted)) return;

            query.Append(query.Length == 0 ? '?' : '&');
            query.Append(global::System.Text.Encodings.Web.UrlEncoder.Default.Encode(key));
            query.Append('=');
            query.Append(global::System.Text.Encodings.Web.UrlEncoder.Default.Encode(converted!));
        }

        private static bool IsNonEmpty(object? value) => value is not null && !(value is string text && text.Length == 0);

        private static bool RoutePartsEqual(object? a, object? b)
        {
            var sa = a as string;
            var sb = b as string;

            if ((sa == "" && sb is null) || (sb == "" && sa is null))
                return true;
            if (sa is not null && sb is not null)
                return string.Equals(sa, sb, global::System.StringComparison.OrdinalIgnoreCase);
            if (a is not null && b is not null)
                return a.Equals(b);
            return a == b;
        }

        /// <summary>
        /// Splits a template into segments of parts. Returns null for a template this port does not
        /// understand, rather than guessing.
        /// </summary>
        private static global::System.Collections.Generic.List<global::System.Collections.Generic.List<Part>>? Parse(string template)
        {
            if (template.StartsWith("~/", global::System.StringComparison.Ordinal)) template = template.Substring(1);

            var segments = new global::System.Collections.Generic.List<global::System.Collections.Generic.List<Part>>();
            var current = new global::System.Collections.Generic.List<Part>();
            var literal = new global::System.Text.StringBuilder();

            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];

                if (c == '/')
                {
                    FlushLiteral(literal, current);
                    if (current.Count > 0) segments.Add(current);
                    current = new global::System.Collections.Generic.List<Part>();
                }
                else if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    literal.Append('{');
                    i++;
                }
                else if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
                {
                    literal.Append('}');
                    i++;
                }
                else if (c == '{')
                {
                    // The body ends at the first '}' that is not doubled; "}}" inside a parameter is an
                    // escaped brace belonging to a regex constraint.
                    var body = new global::System.Text.StringBuilder();
                    var closed = false;
                    for (i++; i < template.Length; i++)
                    {
                        if (template[i] == '}')
                        {
                            if (i + 1 < template.Length && template[i + 1] == '}')
                            {
                                body.Append('}');
                                i++;
                                continue;
                            }

                            closed = true;
                            break;
                        }

                        body.Append(template[i]);
                    }

                    if (!closed) return null;

                    FlushLiteral(literal, current);
                    current.Add(Part.Parameter(body.ToString()));
                }
                else
                {
                    literal.Append(c);
                }
            }

            FlushLiteral(literal, current);
            if (current.Count > 0) segments.Add(current);

            // RoutePatternParser turns the '.' before a trailing optional parameter into a separator.
            foreach (var segment in segments)
            {
                var last = segment.Count - 1;
                if (last > 0 && segment[last].IsOptional && segment[last - 1].IsLiteral && segment[last - 1].Text == ".")
                    segment[last - 1] = Part.Separator(".");
            }

            return segments;
        }

        private static void FlushLiteral(global::System.Text.StringBuilder literal, global::System.Collections.Generic.List<Part> current)
        {
            if (literal.Length == 0) return;
            current.Add(Part.Literal(literal.ToString()));
            literal.Clear();
        }

        /// <summary>
        /// The part of <c>RouteValueDictionary</c> the binder relies on: case-insensitive keys,
        /// insertion order kept on overwrite and on removal.
        /// </summary>
        private sealed class Values
        {
            private readonly global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, object?>> pairs =
                new global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, object?>>();

            public global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, object?>> Pairs => pairs;

            public bool ContainsKey(string key) => IndexOf(key) >= 0;

            public bool TryGetValue(string key, out object? value)
            {
                var index = IndexOf(key);
                value = index >= 0 ? pairs[index].Value : null;
                return index >= 0;
            }

            public void Set(string key, object? value)
            {
                var index = IndexOf(key);
                if (index >= 0)
                    pairs[index] = new global::System.Collections.Generic.KeyValuePair<string, object?>(pairs[index].Key, value);
                else
                    pairs.Add(new global::System.Collections.Generic.KeyValuePair<string, object?>(key, value));
            }

            /// <summary>Removes the key and returns its value, or null when it was not there.</summary>
            public object? Remove(string key)
            {
                var index = IndexOf(key);
                if (index < 0) return null;

                var value = pairs[index].Value;
                pairs.RemoveAt(index);
                return value;
            }

            private int IndexOf(string key)
            {
                for (var i = 0; i < pairs.Count; i++)
                {
                    if (string.Equals(pairs[i].Key, key, global::System.StringComparison.OrdinalIgnoreCase))
                        return i;
                }

                return -1;
            }
        }

        private sealed class Part
        {
            private Part() { }

            public bool IsLiteral { get; private set; }
            public bool IsSeparator { get; private set; }
            public bool IsParameter { get; private set; }
            public string Text { get; private set; } = "";
            public string Name { get; private set; } = "";
            public bool IsOptional { get; private set; }
            public bool IsCatchAll { get; private set; }
            public bool EncodeSlashes { get; private set; } = true;
            public string? Default { get; private set; }

            public static Part Literal(string text) => new Part { IsLiteral = true, Text = text };
            public static Part Separator(string text) => new Part { IsSeparator = true, Text = text };

            public static Part Parameter(string body)
            {
                var catchAll = body.StartsWith("*", global::System.StringComparison.Ordinal);
                var encodeSlashes = !body.StartsWith("**", global::System.StringComparison.Ordinal);
                body = body.TrimStart('*');

                var nameEnd = body.IndexOfAny(new[] { ':', '=', '?' });
                var name = nameEnd < 0 ? body : body.Substring(0, nameEnd);
                var rest = nameEnd < 0 ? "" : body.Substring(nameEnd);

                // A default is the text after the first '=' outside a constraint's argument list; an
                // optional marker is a trailing '?' outside one.
                string? defaultValue = null;
                var optional = false;
                var depth = 0;
                for (var i = 0; i < rest.Length; i++)
                {
                    var c = rest[i];
                    if (c == '(') depth++;
                    else if (c == ')' && depth > 0) depth--;
                    else if (c == '=' && depth == 0)
                    {
                        defaultValue = rest.Substring(i + 1);
                        break;
                    }
                    else if (c == '?' && depth == 0 && i == rest.Length - 1)
                    {
                        optional = true;
                    }
                }

                return new Part
                {
                    IsParameter = true,
                    Name = name,
                    IsOptional = optional,
                    IsCatchAll = catchAll,
                    EncodeSlashes = encodeSlashes,
                    Default = defaultValue,
                };
            }
        }

        /// <summary>A port of <c>UriBuildingContext</c>'s path half, with default options.</summary>
        private sealed class UriBuilder
        {
            private readonly global::System.Text.StringBuilder path = new global::System.Text.StringBuilder();
            private readonly global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, bool>> buffer =
                new global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, bool>>();
            private bool uriInside;
            private bool bufferInside;
            private bool hasEmptySegment;
            private int lastValueOffset = -1;

            public string Path => path.ToString();

            public bool Accept(string? value, bool encodeSlashes)
            {
                if (string.IsNullOrEmpty(value))
                {
                    if (uriInside || bufferInside) return false;

                    hasEmptySegment = true;
                    return true;
                }

                if (hasEmptySegment) return false;

                // Key is the buffered text, Value whether it still needs encoding.
                foreach (var buffered in buffer)
                {
                    if (buffered.Value) Encode(buffered.Key, true);
                    else path.Append(buffered.Key);
                }
                buffer.Clear();

                if (!uriInside && !bufferInside && path.Length != 0)
                    path.Append('/');

                bufferInside = true;
                uriInside = true;
                lastValueOffset = path.Length;

                // The first segment may carry a leading slash, which is not encoded.
                if (path.Length == 0 && value![0] == '/')
                {
                    path.Append('/');
                    Encode(value.Substring(1), encodeSlashes);
                }
                else
                {
                    Encode(value!, encodeSlashes);
                }

                return true;
            }

            public bool Buffer(string? value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    if (bufferInside) return false;

                    hasEmptySegment = true;
                    return true;
                }

                if (hasEmptySegment) return false;

                if (uriInside) return Accept(value, true);

                if (!bufferInside)
                {
                    if (path.Length != 0 || buffer.Count != 0)
                        buffer.Add(new global::System.Collections.Generic.KeyValuePair<string, bool>("/", false));
                    bufferInside = true;
                }

                buffer.Add(new global::System.Collections.Generic.KeyValuePair<string, bool>(value!, true));
                return true;
            }

            public void Remove()
            {
                path.Length = lastValueOffset;
                lastValueOffset = -1;
            }

            public void EndSegment()
            {
                bufferInside = false;
                uriInside = false;
            }

            private void Encode(string value, bool encodeSlashes)
            {
                if (encodeSlashes)
                {
                    path.Append(global::System.Text.Encodings.Web.UrlEncoder.Default.Encode(value));
                    return;
                }

                var pieces = value.Split('/');
                for (var i = 0; i < pieces.Length; i++)
                {
                    if (i > 0) path.Append('/');
                    path.Append(global::System.Text.Encodings.Web.UrlEncoder.Default.Encode(pieces[i]));
                }
            }
        }
    }
}
