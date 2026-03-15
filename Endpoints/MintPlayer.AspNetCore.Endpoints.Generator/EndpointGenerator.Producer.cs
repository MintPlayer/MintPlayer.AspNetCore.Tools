using System.CodeDom.Compiler;
using System.Collections.Immutable;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

partial class EndpointGenerator
{
    sealed class EndpointMappingProducer : Producer
    {
        private readonly ImmutableArray<EndpointInfo> endpoints;
        private readonly ImmutableArray<GroupInfo> groups;
        private readonly AssemblyInfo assemblyInfo;

        public EndpointMappingProducer(ImmutableArray<EndpointInfo> endpoints, ImmutableArray<GroupInfo> groups, AssemblyInfo assemblyInfo)
            : base("MintPlayer.AspNetCore.Endpoints", "EndpointMapping.g.cs")
        {
            this.endpoints = endpoints;
            this.groups = groups;
            this.assemblyInfo = assemblyInfo;
        }

        protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            var valid = endpoints
                .Where(e => !e.HasMultipleGroups)
                .GroupBy(e => e.FullyQualifiedName)
                .Select(g => g.First())
                .ToList();

            if (valid.Count == 0) return;

            writer.WriteLine(Header);
            writer.WriteLine("#nullable enable");
            writer.WriteLine();

            // --- Task A: Partial class declarations ---
            foreach (var ep in valid)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ep.Level == EndpointLevel.Raw) continue;
                if (!ep.IsPartial || ep.HasExistingBaseClass) continue;

                var baseClass = ep.GetBaseClassName();
                if (baseClass is null) continue;

                using (writer.OpenBlock($"namespace {ep.Namespace}"))
                {
                    writer.WriteLine($"partial class {ep.ClassName} : {baseClass} {{ }}");
                }
                writer.WriteLine();
            }

            // --- Task B: Mapping extension ---
            var methodName = assemblyInfo.GetMethodName();
            var className = assemblyInfo.GetSafeClassName();

            // Build group parent lookup from the groups provider
            var groupParentMap = groups
                .Where(g => !g.HasMultipleParents)
                .GroupBy(g => g.FullyQualifiedName)
                .ToDictionary(g => g.Key, g => g.First().ParentGroupFqn);

            // Build child groups lookup: parentFqn -> list of child FQNs
            var childGroups = groups
                .Where(g => g.ParentGroupFqn is not null && !g.HasMultipleParents)
                .GroupBy(g => g.ParentGroupFqn!)
                .ToDictionary(g => g.Key, g => g.Select(x => x.FullyQualifiedName).ToList());

            // Build endpoints-by-group lookup
            var endpointsByGroup = valid
                .Where(e => e.GroupTypeFqn is not null)
                .GroupBy(e => e.GroupTypeFqn!)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Find all referenced group FQNs (from endpoints + from group relationships + parent FQNs)
            var allGroupFqns = new HashSet<string>();
            foreach (var ep in valid)
                if (ep.GroupTypeFqn is not null)
                    allGroupFqns.Add(ep.GroupTypeFqn);
            foreach (var g in groups)
            {
                if (g.HasMultipleParents) continue;
                allGroupFqns.Add(g.FullyQualifiedName);
                if (g.ParentGroupFqn is not null)
                    allGroupFqns.Add(g.ParentGroupFqn);
            }

            // Root groups: those with no parent (not in groupParentMap, or parent is null)
            var rootGroups = allGroupFqns
                .Where(fqn => !groupParentMap.TryGetValue(fqn, out var parent) || parent is null)
                .ToList();

            var ungrouped = valid.Where(e => e.GroupTypeFqn is null).ToList();

            using (writer.OpenBlock("namespace MintPlayer.AspNetCore.Endpoints"))
            {
                using (writer.OpenBlock($"public static class {className}"))
                {
                    // Factory fields
                    for (int i = 0; i < valid.Count; i++)
                    {
                        writer.WriteLine($"private static readonly global::Microsoft.Extensions.DependencyInjection.ObjectFactory<{valid[i].FullyQualifiedName}> _f{i} =");
                        writer.IndentSingleLine($"global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateFactory<{valid[i].FullyQualifiedName}>(global::System.Type.EmptyTypes);");
                        writer.WriteLine();
                    }

                    // Map method
                    using (writer.OpenBlock($"public static global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder {methodName}(this global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)"))
                    {
                        foreach (var ep in ungrouped)
                        {
                            var idx = valid.IndexOf(ep);
                            EmitEndpointMapping(writer, ep, $"_f{idx}", "app");
                        }

                        int groupCounter = 0;
                        foreach (var rootGroupFqn in rootGroups)
                        {
                            EmitGroupTree(writer, rootGroupFqn, "app", ref groupCounter, valid, endpointsByGroup, childGroups);
                        }

                        writer.WriteLine("return app;");
                    }

                    writer.WriteLine();
                    EmitHelpers(writer);

                    // Metadata
                    writer.WriteLine($"public static global::System.Collections.Generic.IReadOnlyList<global::MintPlayer.AspNetCore.Endpoints.EndpointDescriptor> Endpoints {{ get; }} =");
                    writer.WriteLine("[");
                    writer.Indent++;
                    foreach (var ep in valid)
                        writer.WriteLine($"Describe<{ep.FullyQualifiedName}>(\"{ep.ClassName}\"),");
                    writer.Indent--;
                    writer.WriteLine("];");
                }
            }
        }

        private static void EmitGroupTree(
            IndentedTextWriter writer,
            string groupFqn,
            string parentVar,
            ref int groupCounter,
            List<EndpointInfo> valid,
            Dictionary<string, List<EndpointInfo>> endpointsByGroup,
            Dictionary<string, List<string>> childGroups)
        {
            var varName = $"grp{groupCounter++}";
            writer.WriteLine();
            using (writer.OpenBlock(""))
            {
                writer.WriteLine($"var {varName} = MapGroup<{groupFqn}>({parentVar});");

                // Emit endpoints directly in this group
                if (endpointsByGroup.TryGetValue(groupFqn, out var eps))
                {
                    foreach (var ep in eps)
                    {
                        var idx = valid.IndexOf(ep);
                        EmitEndpointMapping(writer, ep, $"_f{idx}", varName);
                    }
                }

                // Recurse into child groups
                if (childGroups.TryGetValue(groupFqn, out var children))
                {
                    foreach (var childFqn in children)
                    {
                        EmitGroupTree(writer, childFqn, varName, ref groupCounter, valid, endpointsByGroup, childGroups);
                    }
                }
            }
        }

        private static void EmitEndpointMapping(IndentedTextWriter writer, EndpointInfo ep, string factoryField, string routesVar)
        {
            if (ep.Level == EndpointLevel.TypedWithResponse)
            {
                using (writer.OpenBlock(""))
                {
                    writer.WriteLine($"var b = Map<{ep.FullyQualifiedName}>({routesVar}, {factoryField});");
                    writer.WriteLine($"Produces<{ep.FullyQualifiedName}, {ep.RequestTypeFqn}, {ep.ResponseTypeFqn}>(b);");
                }
            }
            else
            {
                writer.WriteLine($"Map<{ep.FullyQualifiedName}>({routesVar}, {factoryField});");
            }
        }

        private static void EmitHelpers(IndentedTextWriter writer)
        {
            // Map<TEndpoint>
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
                writer.WriteLine("builder.WithMetadata(typeof(TEndpoint).GetCustomAttributes(true));");
                writer.WriteLine("TEndpoint.Configure(builder);");
                writer.WriteLine("return builder;");
            }
            writer.WriteLine();

            // MapGroup<TGroup>
            using (writer.OpenBlock("private static global::Microsoft.AspNetCore.Routing.RouteGroupBuilder MapGroup<TGroup>(global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder routes) where TGroup : global::MintPlayer.AspNetCore.Endpoints.IEndpointGroup"))
            {
                writer.WriteLine("var group = routes.MapGroup(TGroup.Prefix);");
                writer.WriteLine("TGroup.Configure(group);");
                writer.WriteLine("return group;");
            }
            writer.WriteLine();

            // Produces<TEndpoint, TRequest, TResponse>
            using (writer.OpenBlock("private static void Produces<TEndpoint, TRequest, TResponse>(global::Microsoft.AspNetCore.Builder.RouteHandlerBuilder builder) where TEndpoint : global::MintPlayer.AspNetCore.Endpoints.IEndpoint<TRequest, TResponse>"))
            {
                writer.WriteLine("builder.Produces<TResponse>(TEndpoint.SuccessStatusCode);");
            }
            writer.WriteLine();

            // Describe<TEndpoint>
            using (writer.OpenBlock("private static global::MintPlayer.AspNetCore.Endpoints.EndpointDescriptor Describe<TEndpoint>(string name) where TEndpoint : global::MintPlayer.AspNetCore.Endpoints.IEndpointBase"))
            {
                writer.WriteLine("return new(name, TEndpoint.Path, TEndpoint.Methods, typeof(TEndpoint));");
            }
            writer.WriteLine();
        }
    }
}
