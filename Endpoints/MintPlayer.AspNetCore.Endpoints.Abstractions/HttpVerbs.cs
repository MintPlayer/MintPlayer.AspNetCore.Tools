using System.Collections.ObjectModel;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// The single-verb method lists the convenience endpoint interfaces contribute.
/// </summary>
/// <remarks>
/// These are cached because <c>Methods</c> is read once per registration <i>and</i> once per
/// descriptor, and a collection expression in an expression-bodied property allocates a fresh
/// array on every access. They are wrapped in a read-only view so a consumer cannot cast one back
/// to <c>string[]</c> and mutate the shared instance.
/// <para>
/// An endpoint that answers on more than one verb cannot use these — there is no combining
/// operation here on purpose. Such a class declares its own
/// <c>public static IEnumerable&lt;string&gt; Methods</c>, which is also what resolves the CS8705
/// the compiler would otherwise report; see <see cref="IEndpointBase.Methods"/>.
/// </para>
/// </remarks>
public static class HttpVerbs
{
    /// <summary>Just <c>GET</c>. Supplied by <see cref="IGetEndpoint"/> and its generic forms.</summary>
    public static readonly IReadOnlyList<string> Get = new ReadOnlyCollection<string>(["GET"]);

    /// <summary>Just <c>POST</c>. Supplied by <see cref="IPostEndpoint"/> and its generic forms.</summary>
    public static readonly IReadOnlyList<string> Post = new ReadOnlyCollection<string>(["POST"]);

    /// <summary>Just <c>PUT</c>. Supplied by <see cref="IPutEndpoint"/> and its generic forms.</summary>
    public static readonly IReadOnlyList<string> Put = new ReadOnlyCollection<string>(["PUT"]);

    /// <summary>Just <c>PATCH</c>. Supplied by <see cref="IPatchEndpoint"/> and its generic forms.</summary>
    public static readonly IReadOnlyList<string> Patch = new ReadOnlyCollection<string>(["PATCH"]);

    /// <summary>Just <c>DELETE</c>. Supplied by <see cref="IDeleteEndpoint"/> and its generic forms.</summary>
    public static readonly IReadOnlyList<string> Delete = new ReadOnlyCollection<string>(["DELETE"]);
}
