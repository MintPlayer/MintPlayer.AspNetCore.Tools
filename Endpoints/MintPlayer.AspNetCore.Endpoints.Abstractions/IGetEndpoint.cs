namespace MintPlayer.AspNetCore.Endpoints;

public interface IGetEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => ["GET"];
}

public interface IGetEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => ["GET"];
}

public interface IGetEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => ["GET"];
}
