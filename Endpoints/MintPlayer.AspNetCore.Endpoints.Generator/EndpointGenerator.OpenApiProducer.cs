using System.CodeDom.Compiler;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

partial class EndpointGenerator
{
    /// <summary>
    /// Emits <c>EndpointOpenApi.g.cs</c>: the per-endpoint operation transformers that put the real
    /// parameter types back into the OpenAPI document (PRD R4.1, R4.4, R4.7; spike S1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emitted only when the consumer's compilation references <c>Microsoft.AspNetCore.OpenApi</c>
    /// (<see cref="AssemblyInfo.HasOpenApiTransformers"/>); otherwise this writes nothing and no file
    /// is added. It is the only generated file allowed to name an OpenAPI type.
    /// </para>
    /// <para>
    /// It plugs into the mapping file through the <c>static partial void OnEndpointMapped{n}</c>
    /// hooks that file declares and calls. Implementing a hook here is what makes the call real;
    /// without this file the compiler erases both. Nothing needs configuring beyond the consumer's
    /// own <c>AddOpenApi()</c>/<c>MapOpenApi()</c>: a transformer attached to the endpoint builder
    /// runs for every document that includes the endpoint, and an enum's component schema is
    /// registered from inside the operation transformer through <c>context.Document</c>, so there is
    /// no document-level transformer to add.
    /// </para>
    /// <para>
    /// No per-TFM code: one source compiles and runs unchanged against Microsoft.OpenApi 2.12
    /// (net10.0) and 3.10 (net11.0) — S1 corrected R4.6 on exactly this.
    /// </para>
    /// </remarks>
    internal sealed class EndpointOpenApiProducer : EndpointsProducer
    {
        public const string FileName = "EndpointOpenApi.g.cs";

        public EndpointOpenApiProducer(EndpointModel model) : base(model, FileName) { }

        protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            if (!Model.Assembly.HasOpenApiTransformers) return;

            var plan = EndpointMappingPlan.From(Model);
            var documented = new List<(int Index, List<ShadowMember> Members)>();
            foreach (var endpoint in plan.MappableEndpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var members = ShadowParameters
                    .For(endpoint, plan.ComposedRoutes[endpoint.FullyQualifiedName])
                    .Where(member => member.HasTypedSchema)
                    .ToList();
                if (members.Count > 0)
                    documented.Add((plan.FactoryIndex[endpoint.FullyQualifiedName], members));
            }

            var multiMethod = plan.MappableEndpoints
                .Where(endpoint => plan.IsNamed(endpoint) && TypedLinkHooks.NeedsOperationIdPerMethod(endpoint))
                .Select(endpoint => plan.FactoryIndex[endpoint.FullyQualifiedName])
                .ToList();

            // No hook to implement means nothing to say; an empty file would only be noise.
            if (documented.Count == 0 && multiMethod.Count == 0) return;

            writer.WriteLine(Header);
            writer.WriteLine("#nullable enable");
            writer.WriteLine("#pragma warning disable CS1591");
            writer.WriteLine();

            using (writer.OpenBlock($"namespace {GeneratedNamespace}"))
            using (writer.OpenBlock($"public static partial class {Model.Assembly.GetSafeClassName()}"))
            {
                foreach (var (index, members) in documented)
                {
                    using (writer.OpenBlock($"static partial void {ShadowParameters.HookName(index)}(global::Microsoft.AspNetCore.Builder.RouteHandlerBuilder builder)"))
                    {
                        writer.WriteLine("DocumentParameters(");
                        writer.Indent++;
                        writer.WriteLine("builder,");
                        for (var i = 0; i < members.Count; i++)
                        {
                            var member = members[i];
                            var schema = member.EnumTypeFqn is not null
                                ? $"context => EnumParameterSchema<{member.EnumTypeFqn}>(context)"
                                : $"context => ParameterSchema(\"{member.SchemaShape}\")";
                            var inPath = member.Source == BoundSource.Route ? "true" : "false";
                            writer.WriteLine($"(\"{member.Key.Replace("\\", "\\\\").Replace("\"", "\\\"")}\", {inPath}, {schema}){(i < members.Count - 1 ? "," : ");")}");
                        }
                        writer.Indent--;
                    }
                    writer.WriteLine();
                }

                foreach (var index in multiMethod)
                {
                    using (writer.OpenBlock($"static partial void {TypedLinkHooks.HookName(index)}(global::Microsoft.AspNetCore.Builder.RouteHandlerBuilder builder)"))
                    {
                        writer.WriteLine("OperationIdPerMethod(builder);");
                    }
                    writer.WriteLine();
                }

                foreach (var line in Helpers.Split('\n'))
                    writer.WriteLine(line.TrimEnd('\r'));
            }
        }

        /// <summary>
        /// The shared schema code — one copy, not one per endpoint. Every shape was read from the
        /// document ASP.NET Core 10 and 11 produce for the equivalent typed parameter.
        /// </summary>
        private const string Helpers = """
            /// <summary>
            /// Suffixes the operationId with the HTTP method when the endpoint answers several, so one
            /// endpoint name does not become one operationId on several operations — which OpenAPI forbids.
            /// </summary>
            /// <remarks>
            /// The method count is read from the endpoint's own metadata at run time, so an endpoint whose
            /// Methods the generator could not read keeps its plain name when it turns out to answer one.
            /// </remarks>
            private static void OperationIdPerMethod(global::Microsoft.AspNetCore.Builder.RouteHandlerBuilder builder)
            {
                global::Microsoft.AspNetCore.Builder.OpenApiEndpointConventionBuilderExtensions.AddOpenApiOperationTransformer(builder, (operation, context, cancellationToken) =>
                {
                    var methodCount = 0;
                    foreach (var metadata in context.Description.ActionDescriptor.EndpointMetadata)
                    {
                        if (metadata is global::Microsoft.AspNetCore.Routing.HttpMethodMetadata methods)
                            methodCount = methods.HttpMethods.Count;
                    }

                    if (methodCount > 1 && operation.OperationId is { Length: > 0 } id && context.Description.HttpMethod is { Length: > 0 } method)
                        operation.OperationId = id + char.ToUpperInvariant(method[0]) + method.Substring(1).ToLowerInvariant();

                    return global::System.Threading.Tasks.Task.CompletedTask;
                });
            }

            /// <summary>
            /// Replaces the string schema the framework documented for each shadow member with the schema of
            /// the type the endpoint really binds.
            /// </summary>
            /// <remarks>
            /// Matched by location and name, ignoring case: an unnamed route member is documented with the
            /// template's spelling of the token, which need not be the property's.
            /// </remarks>
            private static void DocumentParameters(
                global::Microsoft.AspNetCore.Builder.RouteHandlerBuilder builder,
                params (string Name, bool InPath, global::System.Func<global::Microsoft.AspNetCore.OpenApi.OpenApiOperationTransformerContext, global::Microsoft.OpenApi.IOpenApiSchema> Schema)[] parameters)
            {
                global::Microsoft.AspNetCore.Builder.OpenApiEndpointConventionBuilderExtensions.AddOpenApiOperationTransformer(builder, (operation, context, cancellationToken) =>
                {
                    if (operation.Parameters is { } declared)
                    {
                        foreach (var parameter in parameters)
                        {
                            var location = parameter.InPath ? global::Microsoft.OpenApi.ParameterLocation.Path : global::Microsoft.OpenApi.ParameterLocation.Query;
                            foreach (var candidate in declared)
                            {
                                if (candidate is global::Microsoft.OpenApi.OpenApiParameter concrete
                                    && concrete.In == location
                                    && string.Equals(concrete.Name, parameter.Name, global::System.StringComparison.OrdinalIgnoreCase))
                                {
                                    concrete.Schema = parameter.Schema(context);
                                }
                            }
                        }
                    }

                    return global::System.Threading.Tasks.Task.CompletedTask;
                });
            }

            /// <summary>The schema ASP.NET Core documents for a typed route or query parameter of the given shape.</summary>
            /// <remarks>
            /// Numbers are deliberately not plain <c>type: integer</c>: the framework documents a pattern plus
            /// <c>type: [integer, string]</c>, because a route or query value arrives as text.
            /// </remarks>
            private static global::Microsoft.OpenApi.IOpenApiSchema ParameterSchema(string shape)
            {
                const string IntegerPattern = @"^-?(?:0|[1-9]\d*)$";
                const string NumberPattern = @"^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$";

                var schema = new global::Microsoft.OpenApi.OpenApiSchema();
                switch (shape)
                {
                    case "int32": case "int64": case "int16": case "uint8": case "uint16": case "uint32": case "uint64":
                        schema.Pattern = IntegerPattern;
                        schema.Type = global::Microsoft.OpenApi.JsonSchemaType.Integer | global::Microsoft.OpenApi.JsonSchemaType.String;
                        schema.Format = shape;
                        break;
                    case "integer":
                        schema.Pattern = IntegerPattern;
                        schema.Type = global::Microsoft.OpenApi.JsonSchemaType.Integer | global::Microsoft.OpenApi.JsonSchemaType.String;
                        break;
                    case "float": case "double":
                        schema.Pattern = NumberPattern;
                        schema.Type = global::Microsoft.OpenApi.JsonSchemaType.Number | global::Microsoft.OpenApi.JsonSchemaType.String;
                        schema.Format = shape;
                        break;
                    case "decimal":
                        schema.Pattern = @"^-?(?:0|[1-9]\d*)(?:\.\d+)?$";
                        schema.Type = global::Microsoft.OpenApi.JsonSchemaType.Number | global::Microsoft.OpenApi.JsonSchemaType.String;
                        schema.Format = "double";
                        break;
                    case "number":
                        schema.Pattern = NumberPattern;
                        schema.Type = global::Microsoft.OpenApi.JsonSchemaType.Number | global::Microsoft.OpenApi.JsonSchemaType.String;
                        break;
                    case "boolean":
                        schema.Type = global::Microsoft.OpenApi.JsonSchemaType.Boolean;
                        break;
                    case "char":
                        schema.MinLength = 1;
                        schema.MaxLength = 1;
                        schema.Type = global::Microsoft.OpenApi.JsonSchemaType.String;
                        schema.Format = "char";
                        break;
                    case "timespan":
                        schema.Pattern = @"^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d{1,7})?$";
                        schema.Type = global::Microsoft.OpenApi.JsonSchemaType.String;
                        break;
                    default:
                        // uuid, date-time, date, time: a formatted string.
                        schema.Type = global::Microsoft.OpenApi.JsonSchemaType.String;
                        schema.Format = shape;
                        break;
                }

                return schema;
            }

            /// <summary>
            /// An enum parameter: a component schema listing the defined values, referenced by name — which
            /// is more than a typed parameter gets, since the framework documents a bare <c>type: integer</c>.
            /// </summary>
            private static global::Microsoft.OpenApi.IOpenApiSchema EnumParameterSchema<TEnum>(global::Microsoft.AspNetCore.OpenApi.OpenApiOperationTransformerContext context)
                where TEnum : struct, global::System.Enum
            {
                var values = new global::System.Collections.Generic.List<global::System.Text.Json.Nodes.JsonNode>();
                foreach (var value in global::System.Enum.GetValuesAsUnderlyingType<TEnum>())
                    values.Add(global::System.Text.Json.Nodes.JsonValue.Create(global::System.Convert.ToInt64(value, global::System.Globalization.CultureInfo.InvariantCulture)));

                var schema = new global::Microsoft.OpenApi.OpenApiSchema
                {
                    Type = global::Microsoft.OpenApi.JsonSchemaType.Integer,
                    Format = "int32",
                    Enum = values,
                };

                var name = typeof(TEnum).Name;
                if (context.Document is { } document)
                {
                    document.AddComponent(name, schema);
                    return new global::Microsoft.OpenApi.OpenApiSchemaReference(name, document);
                }

                return schema;
            }
            """;
    }
}
