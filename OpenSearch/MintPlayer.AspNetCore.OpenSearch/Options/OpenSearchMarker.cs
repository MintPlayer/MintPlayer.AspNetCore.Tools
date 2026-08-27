namespace MintPlayer.AspNetCore.OpenSearch
{
    /// <summary>
    /// Presence marker registered by <c>AddOpenSearch</c> and looked up by <c>MapOpenSearch</c>.
    /// </summary>
    /// <remarks>
    /// A resolved <c>IOptions&lt;OpenSearchOptions&gt;</c> can never prove the registration ran:
    /// the options infrastructure hands back a default-constructed instance for any options type it
    /// has never heard of, so a null check on it is unreachable. Existence of this service is the
    /// only honest signal.
    /// </remarks>
    internal sealed class OpenSearchMarker;
}
