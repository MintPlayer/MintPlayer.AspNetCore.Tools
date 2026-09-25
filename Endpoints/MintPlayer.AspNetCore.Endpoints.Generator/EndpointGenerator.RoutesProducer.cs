using System.CodeDom.Compiler;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

partial class EndpointGenerator
{
    /// <summary>
    /// Emits <c>EndpointRoutes.g.cs</c>: the <c>internal static class Routes</c> of typed links
    /// (PRD R7.1), one method and one <c>…Template</c> constant per named endpoint, in nested classes
    /// mirroring the group tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A file of its own</b>, rather than more of <c>EndpointMapping.g.cs</c>, because it is the one
    /// generated file a consumer reads for its API — go-to-definition on <c>Routes.Api.Users.GetUser</c>
    /// lands in a file that contains only links, not a mapping method, factory fields and shadow
    /// types. It costs nothing in caching: it is registered on the same value-equal model as the
    /// other two outputs, and reads the same <see cref="EndpointMappingPlan"/>, so it cannot disagree
    /// with the mapping about which endpoints are named. Like the others, it writes nothing — and
    /// no file is added — when there is nothing to link to.
    /// </para>
    /// <para>
    /// <b><c>internal</c>, deliberately.</b> Every endpoint assembly emits a type with this fixed name
    /// in this fixed namespace, so a <c>public</c> one would be <c>CS0433</c> (the type exists in two
    /// assemblies) the moment one endpoint project references another.
    /// </para>
    /// <para>
    /// No <c>using</c> directives and every type <c>global::</c>-qualified, as in the other files.
    /// The call site needs no import: the mapping file's <c>global using</c> of the generated
    /// namespace already covers it.
    /// </para>
    /// </remarks>
    internal sealed class EndpointRoutesProducer : EndpointsProducer
    {
        public const string FileName = "EndpointRoutes.g.cs";

        private const string EndpointRoute = "global::MintPlayer.AspNetCore.Endpoints.EndpointRoute";
        private const string RouteValueDictionary = "global::Microsoft.AspNetCore.Routing.RouteValueDictionary";

        public EndpointRoutesProducer(EndpointModel model) : base(model, FileName) { }

        protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            var root = TypedLinks.Build(EndpointMappingPlan.From(Model));
            if (TypedLinks.IsEmpty(root)) return;

            writer.WriteLine(Header);
            writer.WriteLine("#nullable enable");
            writer.WriteLine("#pragma warning disable CS1591");
            writer.WriteLine();

            using (writer.OpenBlock($"namespace {GeneratedNamespace}"))
            {
                writer.WriteLine("/// <summary>");
                writer.WriteLine("/// Typed links to this assembly's endpoints, one method per endpoint, in nested classes that");
                writer.WriteLine("/// mirror the endpoint groups. Each returns an <see cref=\"global::MintPlayer.AspNetCore.Endpoints.EndpointRoute\"/>,");
                writer.WriteLine("/// which converts to its path implicitly.");
                writer.WriteLine("/// </summary>");
                writer.WriteLine("/// <remarks>");
                writer.WriteLine("/// Internal because every endpoint assembly generates this class: a public one would collide");
                writer.WriteLine("/// (CS0433) as soon as one endpoint project referenced another.");
                writer.WriteLine("/// </remarks>");
                WriteNode(writer, root, "internal", cancellationToken);
            }
        }

        private static void WriteNode(IndentedTextWriter writer, TypedLinkNode node, string accessibility, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (writer.OpenBlock($"{accessibility} static class {node.ClassName}"))
            {
                var first = true;
                foreach (var link in node.Links)
                {
                    if (!first) writer.WriteLine();
                    first = false;
                    WriteLink(writer, link);
                }

                foreach (var child in node.Children)
                {
                    if (TypedLinks.IsEmpty(child)) continue;

                    if (!first) writer.WriteLine();
                    first = false;
                    writer.WriteLine($"/// <summary>Links to the endpoints in <c>{Xml(child.GroupFqn!.Replace("global::", ""))}</c>.</summary>");
                    WriteNode(writer, child, "public", cancellationToken);
                }
            }
        }

        private static void WriteLink(IndentedTextWriter writer, TypedLink link)
        {
            var endpointName = link.Endpoint.FullyQualifiedName.Replace("global::", "");

            writer.WriteLine($"/// <summary>The route template of <c>{Xml(endpointName)}</c>, group prefixes included.</summary>");
            writer.WriteLine($"public const string {link.TemplateConstName} = {Literal(link.Template)};");
            writer.WriteLine();

            writer.WriteLine($"/// <summary>A link to <c>{Xml(endpointName)}</c>: <c>{Xml(link.Template)}</c>.</summary>");
            foreach (var parameter in link.Parameters)
            {
                var description = parameter.IsOptional
                    ? $"The <c>{Xml(parameter.Key)}</c> value, left out when null."
                    : $"The <c>{Xml(parameter.Key)}</c> route value.";
                writer.WriteLine($"/// <param name=\"{parameter.Identifier.TrimStart('@')}\">{description}</param>");
            }

            var parameterList = string.Join(", ", link.Parameters.Select(parameter =>
                parameter.HasDefault ? $"{parameter.Type} {parameter.Identifier} = null" : $"{parameter.Type} {parameter.Identifier}"));

            using (writer.OpenBlock($"public static {EndpointRoute} {link.MethodName}({parameterList})"))
            {
                writer.WriteLine($"var __values = new {RouteValueDictionary}();");
                foreach (var parameter in link.Parameters)
                {
                    var assignment = $"__values[{Literal(parameter.Key)}] = {parameter.Identifier};";
                    writer.WriteLine(parameter.IsOptional ? $"if ({parameter.Identifier} is not null) {assignment}" : assignment);
                }

                writer.WriteLine($"return new {EndpointRoute}({Literal(link.Endpoint.EffectiveDescriptorName)}, {link.TemplateConstName}, __values);");
            }
        }

        private static string Xml(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string Literal(string value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }
}
