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
/// </remarks>
public static class HttpVerbs
{
    public static readonly IReadOnlyList<string> Get = new ReadOnlyCollection<string>(["GET"]);
    public static readonly IReadOnlyList<string> Post = new ReadOnlyCollection<string>(["POST"]);
    public static readonly IReadOnlyList<string> Put = new ReadOnlyCollection<string>(["PUT"]);
    public static readonly IReadOnlyList<string> Patch = new ReadOnlyCollection<string>(["PATCH"]);
    public static readonly IReadOnlyList<string> Delete = new ReadOnlyCollection<string>(["DELETE"]);
}
