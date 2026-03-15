namespace MintPlayer.AspNetCore.Endpoints;

public interface IPatchEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => ["PATCH"];
}

public interface IPatchEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => ["PATCH"];
}

public interface IPatchEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => ["PATCH"];
}
