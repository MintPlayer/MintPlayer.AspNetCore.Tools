using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// Generates a typed <c>HttpClient</c> wrapper per referenced endpoint assembly, from the
/// <c>[assembly: EndpointContract(…)]</c> attributes that assembly's own build emitted (PRD R7.4, M9).
/// </summary>
/// <remarks>
/// <para>
/// <b>Opt-in, by MSBuild property:</b> <c>&lt;GenerateEndpointsClient&gt;true&lt;/GenerateEndpointsClient&gt;</c>,
/// made visible to the compiler by the package's <c>build</c> props. An endpoint project that
/// references another endpoint project (a host over its modules) would otherwise grow a client it
/// never asked for. A property rather than an assembly attribute because the client has nowhere to
/// get an attribute type from: it references no package with a <c>lib</c> folder of ours, and one
/// injected by this generator would appear in every server compilation as well.
/// </para>
/// <para>
/// <b>Shipped in the same analyzer assembly as <see cref="EndpointGenerator"/>.</b> A client references
/// <c>MintPlayer.AspNetCore.Endpoints.Generator</c>, the analyzer-only package, which carries no
/// <c>FrameworkReference</c> and so builds for browser-wasm. <see cref="EndpointGenerator"/> runs there
/// too and emits nothing, because it finds no ASP.NET Core (<see cref="AssemblyInfo.CanMapEndpoints"/>).
/// </para>
/// <para>
/// <b>Caching.</b> The compilation is read in one <c>Select</c> whose result is a value-equal
/// <see cref="ClientModel"/>, and the source output is registered on that model — never on the
/// compilation — so an edit that changes no contract re-runs the cheap, memoised read and serves every
/// file from cache.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class EndpointClientGenerator : IIncrementalGenerator
{
    /// <summary>The MSBuild property that turns the client on.</summary>
    public const string OptInProperty = "GenerateEndpointsClient";

    internal const string TrackingName = "EndpointClientModel";

    private static readonly string[] Prerequisites =
    [
        "System.Net.Http.HttpClient",
        "System.Net.Http.Json.JsonContent",
        "System.Net.Http.Json.HttpContentJsonExtensions",
        "System.Text.Json.JsonSerializerOptions",
        "System.Text.Encodings.Web.UrlEncoder",
    ];

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var enabled = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property." + OptInProperty, out var value) &&
                string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase));

        var model = context.CompilationProvider
            .Combine(enabled)
            .Select(static (pair, cancellationToken) => pair.Right ? BuildModel(pair.Left, cancellationToken) : ClientModel.Disabled)
            .WithTrackingName(TrackingName);

        context.RegisterSourceOutput(model, static (productionContext, clientModel) => Emit(productionContext, clientModel));
    }

    private static ClientModel BuildModel(Compilation compilation, CancellationToken cancellationToken)
    {
        var missing = Prerequisites.FirstOrDefault(name => compilation.GetTypeByMetadataName(name) is null);
        var emitGlobalUsing = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder") is null;

        return new ClientModel(true, missing, emitGlobalUsing, ClientContractReader.Read(compilation, cancellationToken));
    }

    private static void Emit(SourceProductionContext context, ClientModel model)
    {
        if (!model.Enabled) return;

        foreach (var server in model.Servers)
        {
            foreach (var problem in server.Problems)
            {
                var descriptor = problem.Id == DiagnosticDescriptors.ClientUsesServerAssemblyType.Id
                    ? DiagnosticDescriptors.ClientUsesServerAssemblyType
                    : DiagnosticDescriptors.ClientContractSkipped;
                context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, problem.Arguments.Cast<object>().ToArray()));
            }
        }

        var clients = model.Servers.Where(server => !server.Endpoints.IsEmpty).ToList();

        // R7.4a: no contracts, no client and no diagnostic.
        if (clients.Count == 0) return;

        if (model.MissingPrerequisite is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ClientPrerequisiteMissing, Location.None, model.MissingPrerequisite));
            return;
        }

        context.AddSource(EndpointClientWriter.UrlHelperFileName, SourceText.From(EndpointClientWriter.UrlHelperSource(), Encoding.UTF8));

        var classNames = EndpointClientWriter.ClassNames(clients.Select(server => server.AssemblyName).ToList());
        for (var i = 0; i < clients.Count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            context.AddSource(
                classNames[i] + ".g.cs",
                SourceText.From(EndpointClientWriter.ClientSource(clients[i], classNames[i], model.EmitGlobalUsing), Encoding.UTF8));
        }
    }
}
