namespace MintPlayer.AspNetCore.Endpoints;

public interface IPostEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => ["POST"];
}

public interface IPostEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => ["POST"];
}

public interface IPostEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => ["POST"];
}
