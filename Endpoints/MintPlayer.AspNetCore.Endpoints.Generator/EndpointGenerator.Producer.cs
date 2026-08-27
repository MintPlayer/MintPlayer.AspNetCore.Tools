using System.CodeDom.Compiler;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

partial class EndpointGenerator
{
    internal static class EndpointMappingProducer
    {
        public const string FileName = "EndpointMapping.g.cs";

        /// <summary>
        /// The namespace the mapping extensions class is emitted into.
        /// </summary>
        /// <remarks>
        /// Not the library's own namespace. The class name is derived from the assembly name or from
        /// <c>[assembly: EndpointsMethodName]</c>, so emitting into
        /// <c>MintPlayer.AspNetCore.Endpoints</c> lets a perfectly reasonable choice — say
        /// <c>MapEndpointRouteBuilder</c> — collide with a type the runtime package ships. Source
        /// beats metadata for a name, so the collision is not even an error: the generated class
        /// silently shadows the shipped one. A namespace only this generator writes to defines that
        /// away, and the emitted <c>global using</c> keeps the call site unchanged.
        /// </remarks>
        private const string GeneratedNamespace = "MintPlayer.AspNetCore.Endpoints.Generated";

        public static void Emit(SourceProductionContext context, EndpointModel model)
        {
            var plan = EndpointMappingPlan.From(model);

            using var buffer = new StringWriter();
            using var writer = new IndentedTextWriter(buffer);

            Write(writer, plan, model.Assembly, context.CancellationToken);

            context.AddSource(FileName, SourceText.From(buffer.ToString(), Encoding.UTF8));
        }

        private static void Write(IndentedTextWriter writer, EndpointMappingPlan plan, AssemblyInfo assembly, CancellationToken cancellationToken)
        {
            var methodName = assembly.GetMethodName();
            var className = assembly.GetSafeClassName();

            writer.WriteLine(Producer.Header);

            // The call site keeps working without the consumer importing anything new.
            writer.WriteLine($"global using global::{GeneratedNamespace};");
            writer.WriteLine();

            // The emitted code fully qualifies every type, but MapMethods, MapGroup, Produces and
            // WithMetadata are extension methods — an extension invocation cannot be resolved from a
            // global:: type name, the declaring namespace has to be in scope. Without these the file
            // only compiles in a Microsoft.NET.Sdk.Web project with implicit usings on.
            writer.WriteLine("using Microsoft.AspNetCore.Builder;");
            writer.WriteLine("using Microsoft.AspNetCore.Http;");
            writer.WriteLine("using Microsoft.AspNetCore.Routing;");
            writer.WriteLine();
            writer.WriteLine("#nullable enable");
            writer.WriteLine();

            // --- Task A: Partial class declarations ---
            // Driven by every declared endpoint, not only the mappable ones: an endpoint dropped for
            // an ambiguous group still needs its base class, or the user's `override HandleAsync`
            // fails with a CS0115 that says nothing about groups.
            foreach (var endpoint in plan.DeclaredEndpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (endpoint.Level == EndpointLevel.Raw) continue;
                if (!endpoint.IsPartial || endpoint.HasExistingBaseClass) continue;

                var baseClass = endpoint.GetBaseClassName();
                if (baseClass is null) continue;

                using (writer.OpenBlock($"namespace {endpoint.Namespace}"))
                {
                    writer.WriteLine($"partial class {endpoint.ClassName} : {baseClass} {{ }}");
                }
                writer.WriteLine();
            }

            // --- Task B: Mapping extension ---
            using (writer.OpenBlock($"namespace {GeneratedNamespace}"))
            using (writer.OpenBlock($"public static class {className}"))
            {
                for (var i = 0; i < plan.MappableEndpoints.Count; i++)
                {
                    var fqn = plan.MappableEndpoints[i].FullyQualifiedName;
                    writer.WriteLine($"private static readonly global::Microsoft.Extensions.DependencyInjection.ObjectFactory<{fqn}> _f{i} =");
                    writer.IndentSingleLine($"global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateFactory<{fqn}>(global::System.Type.EmptyTypes);");
                    writer.WriteLine();
                }

                using (writer.OpenBlock($"public static global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder {methodName}(this global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)"))
                {
                    foreach (var endpoint in plan.MappableEndpoints.Where(e => e.GroupTypeFqn is null))
                        EmitEndpointMapping(writer, endpoint, plan, "app");

                    var groupCounter = 0;
                    var emitted = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var rootGroupFqn in plan.RootGroups)
                        EmitGroupTree(writer, rootGroupFqn, "app", ref groupCounter, plan, emitted);

                    writer.WriteLine("return app;");
                }

                writer.WriteLine();
                EmitHelpers(writer);

                writer.WriteLine("public static global::System.Collections.Generic.IReadOnlyList<global::MintPlayer.AspNetCore.Endpoints.EndpointDescriptor> Endpoints { get; } =");
                writer.WriteLine("[");
                writer.Indent++;
                foreach (var endpoint in plan.MappableEndpoints)
                {
                    var prefix = string.Join(
                        " + ",
                        plan.GroupChains[endpoint.FullyQualifiedName].Select(group => $"Prefix<{group}>()"));

                    writer.WriteLine(prefix.Length == 0
                        ? $"Describe<{endpoint.FullyQualifiedName}>({Literal(endpoint.EffectiveDescriptorName)}, \"\"),"
                        : $"Describe<{endpoint.FullyQualifiedName}>({Literal(endpoint.EffectiveDescriptorName)}, {prefix}),");
                }
                writer.Indent--;
                writer.WriteLine("];");
            }
        }

        /// <summary>
        /// Emits the <c>MapGroup</c> call for one group and recurses into its children.
        /// </summary>
        /// <remarks>
        /// <paramref name="emitted"/> bounds the recursion. A cycle is already filtered out of the
        /// plan before it gets here, so this is belt and braces — but it is the one place in the
        /// generator that could otherwise take the whole compiler down with an uncatchable
        /// StackOverflowException, which no test can survive either.
        /// </remarks>
        private static void EmitGroupTree(
            IndentedTextWriter writer,
            string groupFqn,
            string parentVar,
            ref int groupCounter,
            EndpointMappingPlan plan,
            HashSet<string> emitted)
        {
            if (!emitted.Add(groupFqn))
                return;

            var varName = $"grp{groupCounter++}";
            writer.WriteLine();
            using (writer.OpenBlock(""))
            {
                writer.WriteLine($"var {varName} = MapGroup<{groupFqn}>({parentVar});");

                if (plan.EndpointsByGroup.TryGetValue(groupFqn, out var endpoints))
                {
                    foreach (var endpoint in endpoints)
                        EmitEndpointMapping(writer, endpoint, plan, varName);
                }

                if (plan.ChildGroups.TryGetValue(groupFqn, out var children))
                {
                    foreach (var childFqn in children)
                        EmitGroupTree(writer, childFqn, varName, ref groupCounter, plan, emitted);
                }
            }
        }

        private static void EmitEndpointMapping(IndentedTextWriter writer, EndpointInfo endpoint, EndpointMappingPlan plan, string routesVar)
        {
            var factoryField = $"_f{plan.FactoryIndex[endpoint.FullyQualifiedName]}";

            if (endpoint.Level == EndpointLevel.TypedWithResponse)
            {
                using (writer.OpenBlock(""))
                {
                    writer.WriteLine($"var b = Map<{endpoint.FullyQualifiedName}>({routesVar}, {factoryField});");
                    writer.WriteLine($"Produces<{endpoint.FullyQualifiedName}, {endpoint.RequestTypeFqn}, {endpoint.ResponseTypeFqn}>(b);");
                }
            }
            else
            {
                writer.WriteLine($"Map<{endpoint.FullyQualifiedName}>({routesVar}, {factoryField});");
            }
        }

        private static void EmitHelpers(IndentedTextWriter writer)
        {
            using (writer.OpenBlock("private static global::Microsoft.AspNetCore.Builder.RouteHandlerBuilder Map<TEndpoint>(global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder routes, global::Microsoft.Extensions.DependencyInjection.ObjectFactory<TEndpoint> factory) where TEndpoint : class, global::MintPlayer.AspNetCore.Endpoints.IEndpoint"))
            {
                using (writer.OpenBlock("var builder = routes.MapMethods(TEndpoint.Path, TEndpoint.Methods, async (global::Microsoft.AspNetCore.Http.HttpContext ctx) =>"))
                {
                    writer.WriteLine("var ep = factory(ctx.RequestServices, null);");
                    using (writer.OpenBlock("try"))
                    {
                        writer.WriteLine("return await ep.HandleAsync(ctx);");
                    }
                    using (writer.OpenBlock("finally"))
                    {
                        writer.WriteLine("if (ep is global::System.IAsyncDisposable ad) await ad.DisposeAsync();");
                        writer.WriteLine("else if (ep is global::System.IDisposable d) d.Dispose();");
                    }
                }
                writer.WriteLine(");");
                writer.WriteLine("builder.WithMetadata(global::MintPlayer.AspNetCore.Endpoints.EndpointAttributes.ForMetadata(typeof(TEndpoint)));");
                writer.WriteLine("TEndpoint.Configure(builder);");
                writer.WriteLine("return builder;");
            }
            writer.WriteLine();

            using (writer.OpenBlock("private static global::Microsoft.AspNetCore.Routing.RouteGroupBuilder MapGroup<TGroup>(global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder routes) where TGroup : global::MintPlayer.AspNetCore.Endpoints.IEndpointGroup"))
            {
                writer.WriteLine("var group = routes.MapGroup(TGroup.Prefix);");
                writer.WriteLine("TGroup.Configure(group);");
                writer.WriteLine("return group;");
            }
            writer.WriteLine();

            using (writer.OpenBlock("private static void Produces<TEndpoint, TRequest, TResponse>(global::Microsoft.AspNetCore.Builder.RouteHandlerBuilder builder) where TEndpoint : global::MintPlayer.AspNetCore.Endpoints.IEndpoint<TRequest, TResponse>"))
            {
                writer.WriteLine("builder.Produces<TResponse>(TEndpoint.SuccessStatusCode);");
            }
            writer.WriteLine();

            using (writer.OpenBlock("private static string Prefix<TGroup>() where TGroup : global::MintPlayer.AspNetCore.Endpoints.IEndpointGroup"))
            {
                writer.WriteLine("return TGroup.Prefix;");
            }
            writer.WriteLine();

            // groupPrefix is baked in by the generator, which is the only component that knows the
            // group chain. TEndpoint.Path on its own is group-relative, so a descriptor built from it
            // describes an endpoint in /api/users as "/{id}" — useless for the diagnostics or
            // discovery page the descriptor list exists for.
            using (writer.OpenBlock("private static global::MintPlayer.AspNetCore.Endpoints.EndpointDescriptor Describe<TEndpoint>(string name, string groupPrefix) where TEndpoint : global::MintPlayer.AspNetCore.Endpoints.IEndpointBase"))
            {
                writer.WriteLine("return new(name, groupPrefix + TEndpoint.Path, [.. TEndpoint.Methods], typeof(TEndpoint));");
            }
            writer.WriteLine();
        }

        private static string Literal(string value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
