using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// One generated file of this generator: the house <see cref="Producer"/> shape — one class per
/// output, its own file name, <c>ProduceSource</c> — fed from the value-equal
/// <see cref="EndpointModel"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered through <c>GeneratorExtensions.ProduceCode</c>.</b> Up to
/// MintPlayer.SourceGenerators.Tools 10.x that helper combined every producer with
/// <c>CompilationProvider</c>, so the file could never be served from cache, and
/// <c>Producer.Produce</c> swallowed exceptions without a diagnostic; this class therefore carried
/// its own <c>Emit</c>. Tools 11.0.0 fixed both: <c>ProduceCode</c> registers one output per
/// provider with no compilation in it, and a producer that throws is reported as <c>MPSG001</c>
/// (an error naming the file and the exception) instead of vanishing; Tools 12 keeps both. The hand-rolled
/// <c>Emit</c> is gone.
/// </para>
/// <para>
/// <see cref="Producer.RootNamespace"/> carries the fixed generated namespace rather than the
/// consumer's root namespace — see <see cref="GeneratedNamespace"/>. The base class emits nothing
/// by itself (no header, no usings), so the no-<c>using</c> rule for generated files is unaffected.
/// </para>
/// </remarks>
internal abstract class EndpointsProducer : Producer
{
    /// <summary>
    /// The namespace every generated type is emitted into.
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
    public const string GeneratedNamespace = "MintPlayer.AspNetCore.Endpoints.Generated";

    protected EndpointsProducer(EndpointModel model, string fileName) : base(GeneratedNamespace, fileName)
    {
        Model = model;
    }

    protected EndpointModel Model { get; }
}
