namespace MintPlayer.AspNetCore.Endpoints;

public interface IPatchEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Patch;
}

public interface IPatchEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Patch;
}

public interface IPatchEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Patch;
}
