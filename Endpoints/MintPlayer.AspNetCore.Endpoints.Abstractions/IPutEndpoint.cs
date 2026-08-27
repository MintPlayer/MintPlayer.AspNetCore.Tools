namespace MintPlayer.AspNetCore.Endpoints;

public interface IPutEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Put;
}

public interface IPutEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Put;
}

public interface IPutEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => HttpVerbs.Put;
}
