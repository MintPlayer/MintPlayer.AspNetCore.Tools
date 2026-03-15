namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Base class for non-body endpoints (GET, DELETE).
/// BindRequestAsync is abstract — the user MUST override it.
/// </summary>
public abstract class NonBodyEndpoint<TRequest> : EndpointBase<TRequest>
{
    // BindRequestAsync remains abstract — compiler forces explicit implementation
}
