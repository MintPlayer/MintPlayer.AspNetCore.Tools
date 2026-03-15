namespace MintPlayer.AspNetCore.Endpoints;

// Concrete base classes per HTTP method (used by the generator in partial class declarations)

public abstract class PostEndpoint<TRequest> : BodyEndpoint<TRequest>;
public abstract class PutEndpoint<TRequest> : BodyEndpoint<TRequest>;
public abstract class PatchEndpoint<TRequest> : BodyEndpoint<TRequest>;
public abstract class GetEndpoint<TRequest> : NonBodyEndpoint<TRequest>;
public abstract class DeleteEndpoint<TRequest> : NonBodyEndpoint<TRequest>;
