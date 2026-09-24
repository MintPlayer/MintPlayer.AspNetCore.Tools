using System.CodeDom.Compiler;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.AspNetCore.Endpoints.Generator;

/// <summary>
/// One generated file of this generator: the house <see cref="Producer"/> shape — one class per
/// output, its own file name, <c>ProduceSource</c> — fed from the value-equal
/// <see cref="EndpointModel"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered with <see cref="Emit"/>, not with <c>Producer.Produce</c> or
/// <c>GeneratorExtensions.ProduceCode</c>.</b> Both of those take a <c>Compilation</c>, and a
/// compilation is a fresh object with no value equality on every keystroke — combining the output
/// with it means the file can never be served from cache however well the model compares. There is
/// a second reason specific to the pinned MintPlayer.SourceGenerators.Tools 10.16.0: its
/// <c>Produce</c> catches every exception and discards it, so a producer bug would ship no file
/// and no diagnostic. Here an exception propagates, and Roslyn reports it as CS8785 naming this
/// generator.
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

    /// <summary>Writes the file, or nothing at all when <c>ProduceSource</c> wrote nothing.</summary>
    public void Emit(SourceProductionContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        using var buffer = new StringWriter();
        using var writer = new IndentedTextWriter(buffer);

        ProduceSource(writer, context.CancellationToken);

        var code = buffer.ToString();
        if (code.Length > 0)
            context.AddSource(Filename, SourceText.From(code, Encoding.UTF8));
    }
}
