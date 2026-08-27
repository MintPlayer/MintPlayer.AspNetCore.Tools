namespace MintPlayer.AspNetCore.LoggerProviders;

/// <summary>
/// The scope handed back when scopes are not being captured. Exists so <c>BeginScope</c> can keep
/// its promise of a non-null <see cref="IDisposable"/> without allocating.
/// </summary>
internal sealed class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new();

    private NullScope() { }

    public void Dispose() { }
}
