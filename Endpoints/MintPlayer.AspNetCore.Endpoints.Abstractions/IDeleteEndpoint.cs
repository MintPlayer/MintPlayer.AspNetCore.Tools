namespace MintPlayer.AspNetCore.Endpoints;

public interface IDeleteEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => ["DELETE"];
}

public interface IDeleteEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => ["DELETE"];
}

public interface IDeleteEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => ["DELETE"];
}
