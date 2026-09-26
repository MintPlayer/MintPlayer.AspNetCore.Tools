using System.CodeDom.Compiler;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

partial class EndpointGenerator
{
    /// <summary>
    /// Emits <c>EndpointContracts.g.cs</c>: one <c>[assembly: EndpointContract(…)]</c> per linkable
    /// endpoint, and the internal attribute class they use (PRD R7.4, M9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what a typed client reads. The client references this assembly metadata-only, and
    /// assembly attributes are the one place compile-time facts about the endpoints — the composed
    /// route, which a <c>Path</c> property's body does not carry across the assembly boundary — are
    /// still readable from metadata (spike S6). See <see cref="EndpointContracts"/> for which
    /// endpoints are included and why the attribute class is generated here.
    /// </para>
    /// <para>
    /// A file of its own, like the typed links, and on the same value-equal model, so it caches with
    /// the other three and cannot disagree with the mapping. Assembly attributes must precede every
    /// namespace member in their file (CS1730), which is why they come first. Nothing is written —
    /// and no file added — when there is no endpoint to describe.
    /// </para>
    /// </remarks>
    internal sealed class EndpointContractsProducer : EndpointsProducer
    {
        public const string FileName = "EndpointContracts.g.cs";

        public EndpointContractsProducer(EndpointModel model) : base(model, FileName) { }

        protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            if (!Model.Assembly.CanMapEndpoints) return;

            var contracts = EndpointContracts.From(Model.GetPlan());
            if (contracts.Count == 0) return;

            writer.WriteLine(Header);
            writer.WriteLine("#nullable enable");
            writer.WriteLine("#pragma warning disable CS1591");
            writer.WriteLine();

            foreach (var contract in contracts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteLine(AttributeUsage(contract));
            }

            writer.WriteLine();

            using (writer.OpenBlock($"namespace {GeneratedNamespace}"))
            {
                writer.WriteLine("/// <summary>");
                writer.WriteLine("/// Describes one endpoint of this assembly for a typed client generated in another project:");
                writer.WriteLine("/// its name, verbs, composed route template, route and query parameters, and body types.");
                writer.WriteLine("/// </summary>");
                writer.WriteLine("/// <remarks>");
                writer.WriteLine("/// Generated into this assembly rather than shipped in a package because the client references");
                writer.WriteLine("/// only this assembly, and an attribute whose class it cannot resolve decodes to no arguments at all.");
                writer.WriteLine("/// Internal, so endpoint assemblies that reference each other do not see each other's copy.");
                writer.WriteLine("/// </remarks>");
                writer.WriteLine("[global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]");
                using (writer.OpenBlock($"internal sealed class {EndpointContracts.AttributeName} : global::System.Attribute"))
                {
                    using (writer.OpenBlock($"public {EndpointContracts.AttributeName}(global::System.Type endpoint, string name, string route, string[] methods)"))
                    {
                        writer.WriteLine("Endpoint = endpoint;");
                        writer.WriteLine("Name = name;");
                        writer.WriteLine("Route = route;");
                        writer.WriteLine("Methods = methods;");
                    }

                    writer.WriteLine();
                    writer.WriteLine("/// <summary>The endpoint class.</summary>");
                    writer.WriteLine("public global::System.Type Endpoint { get; }");
                    writer.WriteLine("/// <summary>The endpoint's unique name: its route name and OpenAPI operationId.</summary>");
                    writer.WriteLine("public string Name { get; }");
                    writer.WriteLine("/// <summary>The composed route template, group prefixes included.</summary>");
                    writer.WriteLine("public string Route { get; }");
                    writer.WriteLine("/// <summary>The HTTP methods, upper-cased.</summary>");
                    writer.WriteLine("public string[] Methods { get; }");
                    writer.WriteLine($"/// <summary>The contract format; {EndpointContracts.Version} for this generator.</summary>");
                    writer.WriteLine("public int Version { get; set; }");
                    writer.WriteLine("/// <summary>The JSON request body type, or null for an endpoint without a body.</summary>");
                    writer.WriteLine("public global::System.Type? RequestType { get; set; }");
                    writer.WriteLine("/// <summary>The JSON response type, or null when the endpoint does not declare one.</summary>");
                    writer.WriteLine("public global::System.Type? ResponseType { get; set; }");
                    writer.WriteLine("/// <summary>Every route token, in template order.</summary>");
                    writer.WriteLine("public string[]? RouteParameterNames { get; set; }");
                    writer.WriteLine("/// <summary>The type of each route token, parallel to <see cref=\"RouteParameterNames\"/>.</summary>");
                    writer.WriteLine("public global::System.Type[]? RouteParameterTypes { get; set; }");
                    writer.WriteLine("/// <summary>The query-string keys the endpoint binds.</summary>");
                    writer.WriteLine("public string[]? QueryParameterNames { get; set; }");
                    writer.WriteLine("/// <summary>The type of each query key, parallel to <see cref=\"QueryParameterNames\"/>.</summary>");
                    writer.WriteLine("public global::System.Type[]? QueryParameterTypes { get; set; }");
                }
            }
        }

        private static string AttributeUsage(EndpointContract contract)
        {
            var endpoint = contract.Endpoint;
            var arguments = new List<string>
            {
                $"typeof({endpoint.FullyQualifiedName})",
                Literal(endpoint.EffectiveDescriptorName),
                Literal(contract.Template),
                StringArray(contract.Methods),
                $"Version = {EndpointContracts.Version}",
            };

            // Set only for the Typed levels, whose single request type argument is the JSON body.
            if (endpoint.RequestTypeFqn is not null)
                arguments.Add($"RequestType = typeof({endpoint.RequestTypeFqn})");
            if (endpoint.ResponseTypeFqn is not null)
                arguments.Add($"ResponseType = typeof({endpoint.ResponseTypeFqn})");

            if (contract.RouteParameters.Count > 0)
            {
                arguments.Add($"RouteParameterNames = {StringArray(contract.RouteParameters.Select(parameter => parameter.Key))}");
                arguments.Add($"RouteParameterTypes = {TypeArray(contract.RouteParameters)}");
            }

            if (contract.QueryParameters.Count > 0)
            {
                arguments.Add($"QueryParameterNames = {StringArray(contract.QueryParameters.Select(parameter => parameter.Key))}");
                arguments.Add($"QueryParameterTypes = {TypeArray(contract.QueryParameters)}");
            }

            return $"[assembly: global::{GeneratedNamespace}.{EndpointContracts.AttributeName}({string.Join(", ", arguments)})]";
        }

        private static string StringArray(IEnumerable<string> values) =>
            $"new string[] {{ {string.Join(", ", values.Select(Literal))} }}";

        private static string TypeArray(IEnumerable<ContractParameter> parameters) =>
            $"new global::System.Type[] {{ {string.Join(", ", parameters.Select(parameter => $"typeof({parameter.TypeFqn})"))} }}";

        private static string Literal(string value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }
}
