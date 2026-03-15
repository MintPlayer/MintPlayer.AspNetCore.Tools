# PRD: MintPlayer.AspNetCore.Endpoints

## Problem Statement

When building ASP.NET Core applications with Minimal APIs, endpoint definitions are typically inline lambdas or static methods registered in `Program.cs`. This works for small projects but lacks structure for larger applications:

- **No class-per-endpoint**: Related logic (handling, validation, DI) is scattered
- **No constructor injection**: Minimal API handlers rely on parameter injection, not constructor injection
- **No enforced contract**: Nothing guarantees an endpoint declares its route and HTTP method

The user initially considered an `IEndpoint` interface with instance properties for `Path` and `Method`, but this requires creating an instance just to read routing metadata at startup — before the request-scoped DI container exists.

## Research Summary

Four approaches were investigated:

| Approach | Used By | Instance Needed at Startup? | Compile-Time Enforced? | AOT-Friendly? |
|----------|---------|----------------------------|----------------------|---------------|
| Separate metadata class | IdentityServer | No (metadata is separate) | No (manual registration) | Yes |
| Attributes on class | Ardalis.ApiEndpoints | No (reflection reads attrs) | No (can forget attr) | Depends |
| Configure() method | FastEndpoints | Yes (calls Configure()) | No (runtime error) | No |
| **Static abstract interface members** | — | **No** | **Yes** | **Yes** |

## Recommended Design: Interfaces + Abstract Base Classes + Source Generator

### Core principles

1. **Interfaces** for the static contract (`Path`, `Methods`) — compile-time enforcement, no instance needed at startup
2. **Abstract base classes** for shared implementation (content-negotiated body parsing, `HandleAsync` bridge, disposal) — generated automatically via partial classes
3. **Source generator** for discovery, mapping, and wiring (generates `MapEndpoints()` + partial class base declarations)
4. **User only writes the interface** — the generator adds the abstract base class via `partial class`

### Why this hybrid?

- `static abstract` members only exist on interfaces in C# — can't be on abstract classes
- Abstract base classes provide shared implementation (body parsing, content negotiation) without repetition
- Single inheritance prevents multi-group conflicts (can only extend one base class)
- The partial class trick hides the base class entirely — the user only sees the interface

---

## Detailed Design

### Package Structure

```
Endpoints/
├── MintPlayer.AspNetCore.Endpoints.Abstractions/   # Interfaces, attributes, EndpointDescriptor
├── MintPlayer.AspNetCore.Endpoints/                 # Runtime: abstract base classes, registration, body parsing
└── MintPlayer.AspNetCore.Endpoints.Generator/       # Source generator (discovery + partial class generation)
```

### 1. Abstractions Package (`MintPlayer.AspNetCore.Endpoints.Abstractions`)

#### Interface Hierarchy

Three levels of endpoint abstraction, from full control to fully typed:

```csharp
namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Base contract for all endpoints. Provides route metadata as static abstract members.
/// </summary>
public interface IEndpointBase
{
    /// <summary>The route pattern (e.g., "/api/users/{id}").</summary>
    static abstract string Path { get; }

    /// <summary>
    /// The HTTP methods this endpoint handles (e.g., ["GET"], ["POST"], ["GET", "HEAD"]).
    /// Convenience interfaces (IGetEndpoint, IPostEndpoint, etc.) provide this automatically.
    /// </summary>
    static abstract IEnumerable<string> Methods { get; }

    /// <summary>
    /// Optional hook to configure the route handler (auth, caching, OpenAPI metadata, etc.).
    /// Default implementation is a no-op.
    /// </summary>
    static virtual void Configure(RouteHandlerBuilder builder) { }
}

/// <summary>
/// Level 1: Raw endpoint — full control over HttpContext.
/// </summary>
public interface IEndpoint : IEndpointBase
{
    Task<IResult> HandleAsync(HttpContext httpContext);
}

/// <summary>
/// Level 2: Typed request — automatic request binding via the abstract base class.
/// BindRequestAsync has NO default on this interface. The abstract base class
/// (generated via partial class) provides content-negotiated body parsing for
/// body-based methods (POST, PUT, PATCH). Non-body methods (GET, DELETE)
/// require explicit binding implementation.
/// </summary>
public interface IEndpoint<TRequest> : IEndpoint
{
    Task<IResult> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Level 3: Typed request and response — full type information for OpenAPI/Swagger.
/// TResponse is used by the source generator to emit .Produces&lt;TResponse&gt;(statusCode).
/// </summary>
public interface IEndpoint<TRequest, TResponse> : IEndpoint<TRequest>
{
    /// <summary>
    /// The HTTP status code for successful responses.
    /// Used by the source generator for .Produces&lt;TResponse&gt;(statusCode).
    /// Default: 200 (OK).
    /// </summary>
    static virtual int SuccessStatusCode => 200;
}

/// <summary>
/// Defines a route group with a shared prefix and optional group-wide configuration.
/// Endpoints join a group by implementing IMemberOf&lt;TGroup&gt;.
/// </summary>
public interface IEndpointGroup
{
    /// <summary>The route prefix for all endpoints in this group (e.g., "/api/users").</summary>
    static abstract string Prefix { get; }

    /// <summary>
    /// Optional hook to configure the route group (auth, rate limiting, CORS, tags, etc.).
    /// Default implementation is a no-op.
    /// </summary>
    static virtual void Configure(RouteGroupBuilder group) { }
}

/// <summary>
/// Marker interface that associates an endpoint with an endpoint group.
/// The endpoint's Path becomes relative to the group's Prefix.
/// </summary>
public interface IMemberOf<TGroup> where TGroup : IEndpointGroup;
```

**Key design decisions:**
- `IEndpointBase` holds the shared static metadata (`Path`, `Methods`, `Configure`)
- `IEndpoint` is the simplest form — raw `HttpContext` access
- `IEndpoint<TRequest>` declares the typed handler. Body parsing is handled by the abstract base class (not by a default interface method), using MVC input formatters for content negotiation with JSON fallback
- `IEndpoint<TRequest, TResponse>` adds response type metadata for OpenAPI. The `TResponse` type is used by the source generator to emit `.Produces<TResponse>(statusCode)`. `SuccessStatusCode` defaults to 200, customizable (e.g., 201 for create endpoints)
- `HandleAsync` always returns `Task<IResult>` — full control over status codes and headers
- `IEndpointGroup` + `IMemberOf<TGroup>` for route groups. Group-wide configuration (auth, rate limiting, CORS, tags) goes in `Configure(RouteGroupBuilder)`. Single inheritance via the generated base class naturally prevents multi-group conflicts
- No `BindRequestAsync` on the interface — body parsing lives in the abstract base class

#### Convenience Interfaces

```csharp
// --- GET (non-body: base class requires explicit BindRequestAsync override) ---
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

// --- POST (body-based: base class provides content-negotiated binding) ---
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

// --- PUT (body-based) ---
public interface IPutEndpoint : IEndpoint
{
    static IEnumerable<string> IEndpointBase.Methods => ["PUT"];
}
public interface IPutEndpoint<TRequest> : IEndpoint<TRequest>
{
    static IEnumerable<string> IEndpointBase.Methods => ["PUT"];
}
public interface IPutEndpoint<TRequest, TResponse> : IEndpoint<TRequest, TResponse>
{
    static IEnumerable<string> IEndpointBase.Methods => ["PUT"];
}

// --- DELETE (non-body: base class requires explicit BindRequestAsync override) ---
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

// --- PATCH (body-based) ---
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
```

#### Assembly-Level Attributes

```csharp
/// <summary>
/// Overrides the name of the generated MapEndpoints extension method for this assembly.
/// Without this attribute, the method name is derived from the assembly name
/// (e.g., assembly "MyApp.Api" → "MapMyAppApiEndpoints").
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public class EndpointsMethodNameAttribute(string methodName) : Attribute
{
    public string MethodName { get; } = methodName;
}
```

#### Supplementary Attributes

Standard ASP.NET Core attributes (`[Authorize]`, `[Tags]`, `[EnableCors]`, etc.) work directly on endpoint classes via automatic metadata transfer. The only custom attribute is for endpoint naming:

```csharp
[AttributeUsage(AttributeTargets.Class)]
public class EndpointNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
```

#### Endpoint Descriptor

```csharp
public record EndpointDescriptor(string Name, string Path, IEnumerable<string> Methods, Type HandlerType);
```

### 2. Runtime Package (`MintPlayer.AspNetCore.Endpoints`)

This package references `Microsoft.AspNetCore.App` shared framework (implicit with `Sdk.Web`), which includes MVC types. No extra NuGet dependency is needed.

#### Abstract Base Classes

Body-based base classes provide content-negotiated request parsing using MVC's `IInputFormatter` system (with JSON fallback). Non-body base classes require explicit `BindRequestAsync` override.

```csharp
namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>
/// Base class for all typed endpoints. Provides the HandleAsync(HttpContext) bridge
/// and disposal logic. Subclassed by body-based and non-body-based variants.
/// </summary>
public abstract class EndpointBase<TRequest> : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Binds the request from the HttpContext. Override for custom binding.
    /// Body-based subclasses (PostEndpoint, PutEndpoint, PatchEndpoint) provide a
    /// default using MVC input formatters with JSON fallback.
    /// Non-body subclasses (GetEndpoint, DeleteEndpoint) leave this abstract.
    /// </summary>
    protected abstract ValueTask<TRequest?> BindRequestAsync(HttpContext context);

    /// <summary>Typed request handler — implemented by the user's endpoint class.</summary>
    public abstract Task<IResult> HandleAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>Bridge: IEndpoint.HandleAsync(HttpContext) → BindRequestAsync → HandleAsync(TRequest, CT).</summary>
    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var request = await BindRequestAsync(httpContext);
        return await HandleAsync(request!, httpContext.RequestAborted);
    }

    // Disposal (virtual, overridable)
    public virtual void Dispose() { }
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Base class for body-based endpoints (POST, PUT, PATCH).
/// Provides content-negotiated request body parsing using MVC input formatters
/// (if available) with JSON fallback.
/// </summary>
public abstract class BodyEndpoint<TRequest> : EndpointBase<TRequest>
{
    /// <summary>
    /// Reads the request body using MVC input formatters for content negotiation.
    /// If MVC is not configured, falls back to JSON deserialization.
    /// Override for custom binding (e.g., multi-source, form data).
    /// </summary>
    protected override async ValueTask<TRequest?> BindRequestAsync(HttpContext context)
    {
        // Try MVC input formatters (available if AddControllers/AddMvc was called)
        var mvcOptions = context.RequestServices.GetService<IOptions<MvcOptions>>();
        if (mvcOptions is not null)
        {
            var formatters = mvcOptions.Value.InputFormatters;
            var modelMetadataProvider = context.RequestServices
                .GetRequiredService<IModelMetadataProvider>();
            var modelMetadata = modelMetadataProvider
                .GetMetadataForType(typeof(TRequest));

            var formatterContext = new InputFormatterContext(
                context,
                modelName: string.Empty,
                modelState: new ModelStateDictionary(),
                metadata: modelMetadata,
                readerFactory: (stream, encoding) => new StreamReader(stream, encoding));

            foreach (var formatter in formatters)
            {
                if (formatter.CanRead(formatterContext))
                {
                    var result = await formatter.ReadAsync(formatterContext);
                    if (result.IsModelSet)
                        return (TRequest?)result.Model;
                }
            }
        }

        // Fallback: JSON (works without MVC)
        return await context.Request.ReadFromJsonAsync<TRequest>(context.RequestAborted);
    }
}

/// <summary>
/// Base class for non-body endpoints (GET, DELETE).
/// BindRequestAsync is abstract — the user MUST override it.
/// </summary>
public abstract class NonBodyEndpoint<TRequest> : EndpointBase<TRequest>
{
    // BindRequestAsync remains abstract — compiler forces explicit implementation
}

// Concrete base classes per HTTP method (used by the generator in partial class declarations)
public abstract class PostEndpoint<TRequest> : BodyEndpoint<TRequest>;
public abstract class PutEndpoint<TRequest> : BodyEndpoint<TRequest>;
public abstract class PatchEndpoint<TRequest> : BodyEndpoint<TRequest>;
public abstract class GetEndpoint<TRequest> : NonBodyEndpoint<TRequest>;
public abstract class DeleteEndpoint<TRequest> : NonBodyEndpoint<TRequest>;
```

**Hierarchy:**
```
EndpointBase<TRequest>
├── BodyEndpoint<TRequest>        (content-negotiated body parsing)
│   ├── PostEndpoint<TRequest>
│   ├── PutEndpoint<TRequest>
│   └── PatchEndpoint<TRequest>
└── NonBodyEndpoint<TRequest>     (abstract BindRequestAsync — must override)
    ├── GetEndpoint<TRequest>
    └── DeleteEndpoint<TRequest>
```

#### Extension Methods for Manual Registration

```csharp
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a single endpoint class.
    /// </summary>
    public static IEndpointRouteBuilder MapEndpoint<TEndpoint>(this IEndpointRouteBuilder app)
        where TEndpoint : class, IEndpoint
    {
        var factory = ActivatorUtilities.CreateFactory<TEndpoint>(Type.EmptyTypes);

        var builder = app.MapMethods(
            TEndpoint.Path,
            TEndpoint.Methods,
            async (HttpContext ctx) =>
            {
                var endpoint = factory(ctx.RequestServices, null);
                try
                {
                    return await endpoint.HandleAsync(ctx);
                }
                finally
                {
                    if (endpoint is IAsyncDisposable asyncDisposable)
                        await asyncDisposable.DisposeAsync();
                    else if (endpoint is IDisposable disposable)
                        disposable.Dispose();
                }
            });

        // Transfer class-level attributes to endpoint metadata
        builder.WithMetadata(typeof(TEndpoint).GetCustomAttributes(true));

        // Call the optional Configure hook
        TEndpoint.Configure(builder);

        return app;
    }
}
```

### 3. Source Generator Package (`MintPlayer.AspNetCore.Endpoints.Generator`)

The source generator performs **two code generation tasks**:

#### Task A: Partial Class Base Declaration

For each `partial class` implementing a typed endpoint interface, the generator emits a second partial that adds the appropriate abstract base class:

| User writes | Generator emits |
|---|---|
| `partial class X : IPostEndpoint<T>` | `partial class X : PostEndpoint<T>` |
| `partial class X : IPostEndpoint<T, R>` | `partial class X : PostEndpoint<T>` |
| `partial class X : IPutEndpoint<T>` | `partial class X : PutEndpoint<T>` |
| `partial class X : IPatchEndpoint<T>` | `partial class X : PatchEndpoint<T>` |
| `partial class X : IGetEndpoint<T>` | `partial class X : GetEndpoint<T>` |
| `partial class X : IGetEndpoint<T, R>` | `partial class X : GetEndpoint<T>` |
| `partial class X : IDeleteEndpoint<T>` | `partial class X : DeleteEndpoint<T>` |
| `partial class X : IDeleteEndpoint<T, R>` | `partial class X : DeleteEndpoint<T>` |
| `partial class X : IEndpoint<T>` | `partial class X : EndpointBase<T>` (abstract BindRequestAsync) |
| `partial class X : IEndpoint` | *(no base class — raw HttpContext)* |

**Generated partial example:**

```csharp
// <auto-generated/>
namespace MyApp.Endpoints;

partial class CreateUser : global::MintPlayer.AspNetCore.Endpoints.PostEndpoint<global::MyApp.CreateUserRequest>
{
}
```

The compiler merges this with the user's partial:
```csharp
// Combined: class CreateUser : PostEndpoint<CreateUserRequest>, IPostEndpoint<CreateUserRequest, CreateUserResponse>
```

**Diagnostics:**
- Error if the class is not declared `partial`
- Error if the class already declares a base class (conflict)
- Error if the class implements `IMemberOf<T>` for multiple groups

#### Task B: Endpoint Mapping Extension Method

Discovers all `IEndpointBase` implementations and generates a `Map{AssemblyName}Endpoints()` extension method:

1. **Reads `[assembly: EndpointsMethodName("...")]`** for method name override
2. **Groups endpoints** by `IMemberOf<T>` association
3. **Generates `ActivatorUtilities.CreateFactory<T>()`** per endpoint (compiled once at startup)
4. **Emits `MapGroup()` + `Configure()`** for grouped endpoints
5. **Emits `MapMethods()`** per endpoint with try/finally disposal
6. **Transfers class-level attributes** via `.WithMetadata(typeof(T).GetCustomAttributes(true))`
7. **Calls `T.Configure(builder)`** per endpoint
8. **Emits `.Produces<TResponse>(statusCode)`** for `IEndpoint<TRequest, TResponse>` endpoints
9. **Generates endpoint metadata list** for diagnostics/admin UIs

**Method naming:**

| Assembly Name | Generated Method |
|---|---|
| `MyApp.Api` | `MapMyAppApiEndpoints()` |
| `MyApp.Features.Auth` | `MapMyAppFeaturesAuthEndpoints()` |

Override with: `[assembly: EndpointsMethodName("MapAuthEndpoints")]`

**Generated output example:**

For a project called `MyApp.Api`:

```csharp
// <auto-generated/>
#nullable enable

namespace MintPlayer.AspNetCore.Endpoints;

public static class MyAppApiEndpointExtensions
{
    private static readonly global::System.Func<global::System.IServiceProvider, object?[]?, global::MyApp.Api.HealthCheck> _factory0 =
        global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateFactory<global::MyApp.Api.HealthCheck>(global::System.Type.EmptyTypes);

    private static readonly global::System.Func<global::System.IServiceProvider, object?[]?, global::MyApp.Api.Login> _factory1 =
        global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateFactory<global::MyApp.Api.Login>(global::System.Type.EmptyTypes);

    private static readonly global::System.Func<global::System.IServiceProvider, object?[]?, global::MyApp.Api.CreateUser> _factory2 =
        global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateFactory<global::MyApp.Api.CreateUser>(global::System.Type.EmptyTypes);

    public static global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder MapMyAppApiEndpoints(
        this global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
    {
        // HealthCheck : IGetEndpoint (IEndpoint — no typed request)
        {
            var builder = app.MapMethods(
                global::MyApp.Api.HealthCheck.Path,
                global::MyApp.Api.HealthCheck.Methods,
                async (global::Microsoft.AspNetCore.Http.HttpContext ctx) =>
                {
                    var endpoint = _factory0(ctx.RequestServices, null);
                    try
                    {
                        return await endpoint.HandleAsync(ctx);
                    }
                    finally
                    {
                        await DisposeEndpointAsync(endpoint);
                    }
                });
            builder.WithMetadata(typeof(global::MyApp.Api.HealthCheck).GetCustomAttributes(true));
            global::MyApp.Api.HealthCheck.Configure(builder);
        }

        // UsersApi group
        {
            var group = app.MapGroup(global::MyApp.Api.UsersApi.Prefix);
            global::MyApp.Api.UsersApi.Configure(group);

            // CreateUser : IPostEndpoint<CreateUserRequest, CreateUserResponse>, IMemberOf<UsersApi>
            {
                var builder = group.MapMethods(
                    global::MyApp.Api.CreateUser.Path,
                    global::MyApp.Api.CreateUser.Methods,
                    async (global::Microsoft.AspNetCore.Http.HttpContext ctx) =>
                    {
                        var endpoint = _factory2(ctx.RequestServices, null);
                        try
                        {
                            return await endpoint.HandleAsync(ctx);
                        }
                        finally
                        {
                            await DisposeEndpointAsync(endpoint);
                        }
                    });
                builder.WithMetadata(typeof(global::MyApp.Api.CreateUser).GetCustomAttributes(true));
                global::MyApp.Api.CreateUser.Configure(builder);
                builder.Produces<global::MyApp.Api.CreateUserResponse>(201);
            }
        }

        return app;
    }

    private static async global::System.Threading.Tasks.ValueTask DisposeEndpointAsync(object endpoint)
    {
        if (endpoint is global::System.IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (endpoint is global::System.IDisposable disposable)
            disposable.Dispose();
    }
}

public static class MyAppApiEndpointMetadata
{
    public static global::System.Collections.Generic.IReadOnlyList<global::MintPlayer.AspNetCore.Endpoints.EndpointDescriptor> Endpoints { get; } =
    [
        new("HealthCheck", "/health", ["GET"], typeof(global::MyApp.Api.HealthCheck)),
        new("CreateUser", "/api/users", ["POST"], typeof(global::MyApp.Api.CreateUser)),
    ];
}
```

#### Generator Implementation Approach

- Use `IIncrementalGenerator` (not the legacy `ISourceGenerator`)
- Use `CreateSyntaxProvider` with a syntactic predicate checking for `ClassDeclarationSyntax` with base types, then semantic check for `IEndpointBase` implementation
- Detect which interface level is implemented (most specific first: `IEndpoint<T,R>` → `IEndpoint<T>` → `IEndpoint`)
- Detect `IMemberOf<T>` via `AllInterfaces` to group endpoints
- Check for `partial` keyword — emit diagnostic if missing on typed endpoints
- Check for existing base class — emit diagnostic if conflict
- Read `[assembly: EndpointsMethodName("...")]` for method naming
- Extract fully-qualified type names into plain data models (with value equality) for caching
- Generate a single output file with all factories and mappings
- Use `global::` prefixed fully-qualified type names throughout
- Report diagnostics for: non-partial typed endpoints (error), base class conflicts (error), abstract classes implementing IEndpointBase (warning), duplicate path+method combinations (error), multiple `IMemberOf<T>` implementations (error)

### 4. Consumer Experience

**Simple endpoint (raw HttpContext — no base class needed):**

```csharp
public class HealthCheck : IGetEndpoint
{
    public static string Path => "/health";

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok("healthy"));
}
```

**POST with typed request (JSON body binding — content negotiation via base class):**

```csharp
// User only writes this — the generator adds `: PostEndpoint<CreateOrderRequest>` via partial class
public partial class CreateOrder : IPostEndpoint<CreateOrderRequest>
{
    public static string Path => "/api/orders";

    // No BindRequestAsync needed — PostEndpoint<T> handles body parsing
    // with content negotiation (JSON, XML, etc. via MVC input formatters)

    private readonly IOrderService _orders;
    public CreateOrder(IOrderService orders) => _orders = orders;

    public override async Task<IResult> HandleAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var order = await _orders.CreateAsync(request, ct);
        return Results.Created($"/api/orders/{order.Id}", order);
    }
}
```

**GET with typed request (must implement BindRequestAsync — compile error without it):**

```csharp
public partial class GetOrder : IGetEndpoint<GetOrderRequest>
{
    public static string Path => "/api/orders/{id}";

    // Required — GetEndpoint<T> has abstract BindRequestAsync (compile error without this)
    protected override ValueTask<GetOrderRequest?> BindRequestAsync(HttpContext context)
    {
        var id = int.Parse(context.Request.RouteValues["id"]!.ToString()!);
        var includeItems = context.Request.Query.ContainsKey("includeItems");
        return ValueTask.FromResult<GetOrderRequest?>(new GetOrderRequest(id, includeItems));
    }

    private readonly IOrderService _orders;
    public GetOrder(IOrderService orders) => _orders = orders;

    public override async Task<IResult> HandleAsync(GetOrderRequest request, CancellationToken ct)
    {
        var order = await _orders.GetAsync(request.Id, request.IncludeItems, ct);
        return order is null ? Results.NotFound() : Results.Ok(order);
    }
}
```

**POST with custom binding override (e.g., form data):**

```csharp
public partial class UploadFile : IPostEndpoint<UploadRequest>
{
    public static string Path => "/api/upload";

    // Override the content-negotiated default for custom form binding
    protected override async ValueTask<UploadRequest?> BindRequestAsync(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync();
        return new UploadRequest(form.Files.GetFile("file")!, form["description"].ToString());
    }

    public override async Task<IResult> HandleAsync(UploadRequest request, CancellationToken ct) { ... }
}
```

**Fully typed (request + response for OpenAPI) with attributes:**

```csharp
[Authorize(Policy = "AdminPolicy")]
[Tags("Users", "Admin")]
public partial class CreateUser : IPostEndpoint<CreateUserRequest, CreateUserResponse>
{
    public static string Path => "/api/users";
    static int IEndpoint<CreateUserRequest, CreateUserResponse>.SuccessStatusCode => 201;

    private readonly IUserService _users;
    public CreateUser(IUserService users) => _users = users;

    public override async Task<IResult> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        var user = await _users.CreateAsync(request, ct);
        return Results.Created($"/api/users/{user.Id}", new CreateUserResponse(user.Id, user.Name));
    }
}
```

**Multi-method endpoint (no typed request, no base class needed):**

```csharp
public class CorsPreflightEndpoint : IEndpoint
{
    public static string Path => "/api/{**path}";
    public static IEnumerable<string> Methods => ["OPTIONS", "HEAD"];

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok());
}
```

**Route groups:**

```csharp
// Group definition — Configure for group-wide concerns
public class UsersApi : IEndpointGroup
{
    public static string Prefix => "/api/users";

    static void IEndpointGroup.Configure(RouteGroupBuilder group)
    {
        group.RequireAuthorization();
        group.WithTags("Users");
        group.RequireRateLimiting("standard");
    }
}

// Endpoints join via IMemberOf<T> — paths are relative to group prefix
public partial class ListUsers : IGetEndpoint, IMemberOf<UsersApi>
{
    public static string Path => "/";              // → GET /api/users/
    public async Task<IResult> HandleAsync(HttpContext httpContext) { ... }
}

public partial class GetUser : IGetEndpoint<GetUserRequest>, IMemberOf<UsersApi>
{
    public static string Path => "/{id}";          // → GET /api/users/{id}

    // Required — GetEndpoint<T> has abstract BindRequestAsync
    protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
    {
        var id = int.Parse(context.Request.RouteValues["id"]!.ToString()!);
        return ValueTask.FromResult<GetUserRequest?>(new(id));
    }

    public override async Task<IResult> HandleAsync(GetUserRequest request, CancellationToken ct) { ... }
}

[AllowAnonymous]  // overrides the group's RequireAuthorization()
public partial class CreateUser : IPostEndpoint<CreateUserRequest, CreateUserResponse>, IMemberOf<UsersApi>
{
    public static string Path => "/";              // → POST /api/users/
    // No BindRequestAsync needed — PostEndpoint<T> handles content-negotiated body parsing

    public override async Task<IResult> HandleAsync(CreateUserRequest request, CancellationToken ct) { ... }
}
```

**Registration in Program.cs:**

```csharp
var app = builder.Build();

// Source-generated (recommended) — method name derived from assembly or overridden
app.MapMyAppApiEndpoints();

// OR manual registration (works without source generator)
app.MapEndpoint<CreateOrder>();

app.Run();
```

**Multi-assembly app:**

```csharp
// In MyApp.Features.Auth assembly:
[assembly: EndpointsMethodName("MapAuthEndpoints")]

// In MyApp.Features.Users assembly:
[assembly: EndpointsMethodName("MapUserEndpoints")]

// In Program.cs:
app.MapAuthEndpoints();
app.MapUserEndpoints();
```

---

## Endpoint Lifetime, Scoping, and Disposal

Endpoint instances are created **per-request** via `ActivatorUtilities.CreateFactory<T>()`, using `ctx.RequestServices` (the request-scoped `IServiceProvider`).

**Scoped services work correctly.** Since `ctx.RequestServices` is the request-scoped container, all constructor-injected dependencies (e.g., `DbContext`, `IHttpContextAccessor`, custom scoped services) resolve from the correct scope — the same instances used throughout the rest of the request pipeline.

**Endpoint instances are disposed manually.** Instances created by `ActivatorUtilities` are NOT tracked by the DI container for disposal. The generated code wraps every handler call in `try/finally` to dispose endpoints. The abstract base class implements `IDisposable`/`IAsyncDisposable` with virtual no-op methods that endpoints can override if needed. Dependencies (like `DbContext`) are still disposed by the scoped container as usual.

## Class-Level Attribute Transfer

The generated code calls `.WithMetadata(typeof(T).GetCustomAttributes(true))` to transfer all class-level attributes to endpoint metadata. Standard ASP.NET Core attributes work directly:

| Attribute | Middleware That Reads It |
|---|---|
| `[Authorize]` / `[AllowAnonymous]` | `AuthorizationMiddleware` |
| `[EnableCors]` / `[DisableCors]` | `CorsMiddleware` |
| `[EnableRateLimiting]` / `[DisableRateLimiting]` | `RateLimitingMiddleware` |
| `[OutputCache]` | `OutputCacheMiddleware` |
| `[Tags]` | OpenAPI document generation |
| `[RequireAntiforgeryToken]` | `AntiforgeryMiddleware` |
| `[RequestSizeLimit]` | `EndpointRoutingMiddleware` |

**Note:** MVC-specific filter attributes (`IFilterMetadata`) will NOT execute in minimal APIs.

## Content Negotiation (Body Parsing)

The `BodyEndpoint<TRequest>` base class provides content-negotiated request body parsing:

1. **If MVC is configured** (app calls `AddControllers()` or `AddMvc()`): resolves `IOptions<MvcOptions>` from DI, iterates `InputFormatters`, and uses the first formatter that can read the request's `Content-Type`. This automatically supports JSON, XML, and any custom formatters the app has registered.

2. **If MVC is not configured**: falls back to `ReadFromJsonAsync<TRequest>()` (JSON-only). This covers the majority of Minimal API-only applications.

3. **Override for custom binding**: endpoints can always override `BindRequestAsync` for custom scenarios (form data, multi-source binding, etc.).

The endpoint itself never knows whether the body is JSON, XML, or another format — the base class handles it transparently.

---

## NuGet Package Details

| Package | PackageId | Target | Dependencies |
|---------|-----------|--------|-------------|
| Abstractions | `MintPlayer.AspNetCore.Endpoints.Abstractions` | `net10.0` | `Microsoft.AspNetCore.Http.Abstractions` |
| Runtime | `MintPlayer.AspNetCore.Endpoints` | `net10.0` | Abstractions, shared framework (includes MVC types) |
| Generator | `MintPlayer.AspNetCore.Endpoints.Generator` | `netstandard2.0` | `Microsoft.CodeAnalysis.CSharp` |

All packages:
- Version: `10.0.0`
- License: Apache-2.0
- Author: Pieterjan De Clippel
- Company: MintPlayer
- IncludeSymbols: true
- SymbolPackageFormat: snupkg

The Generator package is referenced as:
```xml
<PackageReference Include="MintPlayer.AspNetCore.Endpoints.Generator" Version="10.0.0"
                  PrivateAssets="all" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

---

## Implementation Plan

### Phase 1: Core Library
1. Create `MintPlayer.AspNetCore.Endpoints.Abstractions` project
   - `IEndpointBase`, `IEndpoint`, `IEndpoint<TRequest>`, `IEndpoint<TRequest, TResponse>`
   - `IEndpointGroup`, `IMemberOf<TGroup>`
   - All convenience interfaces (GET/POST/PUT/DELETE/PATCH × 3 generic variants)
   - `EndpointDescriptor` record
   - `EndpointsMethodNameAttribute`, `EndpointNameAttribute`
2. Create `MintPlayer.AspNetCore.Endpoints` project
   - Abstract base class hierarchy (`EndpointBase<T>`, `BodyEndpoint<T>`, `NonBodyEndpoint<T>`, `PostEndpoint<T>`, `GetEndpoint<T>`, etc.)
   - Content-negotiated body parsing in `BodyEndpoint<T>` using MVC input formatters with JSON fallback
   - `MapEndpoint<T>()` extension method
3. Add both projects to the solution

### Phase 2: Source Generator
4. Create `MintPlayer.AspNetCore.Endpoints.Generator` project (targets `netstandard2.0`)
   - `IIncrementalGenerator` implementation
   - **Task A**: Generate partial class base declarations
     - Detect interface level → pick correct base class
     - Emit diagnostic if class is not `partial`
     - Emit diagnostic if class already has a base class
   - **Task B**: Generate `Map{Name}Endpoints()` extension
     - Discover `IEndpointBase` implementors
     - Detect `IMemberOf<T>` for grouping
     - Read `[assembly: EndpointsMethodName]` for method naming
     - Generate factories, disposal, metadata transfer, Configure calls
     - Emit `.Produces<TResponse>(statusCode)` for typed endpoints
     - Generate endpoint metadata list
   - Diagnostics: non-partial typed endpoints, base class conflicts, duplicate path+method, multiple `IMemberOf<T>`

### Phase 3: Validation & Polish
5. Test with a sample application
6. Verify source generator works in IDE (IntelliSense for generated method)
7. Verify partial class base declaration generation
8. Verify content negotiation (JSON default, XML with MVC formatters)
9. Verify disposal of endpoint instances
10. Verify typed binding (body-based default + non-body explicit + custom override)
11. Verify OpenAPI metadata generation (`.Produces<T>()`)
12. Verify route groups (`IMemberOf<T>` → `MapGroup()`)
13. Verify group-wide configuration (e.g., `RequireAuthorization()` applies to all members)
14. Verify attribute transfer (`[Authorize]`, `[Tags]`, etc.)
15. Update solution README
16. NuGet package metadata

---

## Resolved Questions

1. ~~**Should `HandleAsync` support typed request/response?**~~ **Yes.** Three interface levels: `IEndpoint` (raw HttpContext), `IEndpoint<TRequest>` (typed request), `IEndpoint<TRequest, TResponse>` (typed + OpenAPI response metadata).

2. ~~**Should the source generator live in this repo?**~~ **Yes.** ASP.NET Core-specific.

3. ~~**Multi-assembly support?**~~ **Yes.** Generated method name derived from assembly name (PascalCased), overridable via `[assembly: EndpointsMethodName("...")]`.

4. ~~**Disposal pattern**~~ **Yes, dispose manually.** Abstract base class implements `IDisposable`/`IAsyncDisposable` with virtual no-ops. Generated code wraps handlers in `try/finally`.

5. ~~**Multiple HTTP methods per endpoint**~~ **Yes.** `IEndpointBase.Methods` is `IEnumerable<string>`. Convenience interfaces provide single-method defaults.

6. ~~**Class-level attributes**~~ **Yes, automatic transfer.** `.WithMetadata(typeof(T).GetCustomAttributes(true))` transfers `[Authorize]`, `[Tags]`, `[EnableCors]`, `[OutputCache]`, etc. to endpoint metadata.

7. ~~**Route groups**~~ **`IEndpointGroup` + `IMemberOf<TGroup>`.** Configure via `Configure(RouteGroupBuilder)`. Single inheritance via generated base class prevents multi-group naturally.

8. ~~**`BindRequestAsync` defaults**~~ **Method-aware via abstract base classes.** `BodyEndpoint<T>` (POST/PUT/PATCH) provides content-negotiated binding. `NonBodyEndpoint<T>` (GET/DELETE) requires explicit override.

9. ~~**Multiple group membership**~~ **Prevented by design.** Single inheritance (one base class) + diagnostic for multiple `IMemberOf<T>`.

10. ~~**Abstract classes vs interfaces**~~ **Both.** Interfaces for the static contract (C# requires it for `static abstract`). Abstract base classes for shared implementation (body parsing, bridge, disposal). Source generator bridges them via partial classes — user only writes the interface.

11. ~~**Content negotiation / body parsing**~~ **MVC input formatters with JSON fallback.** `BodyEndpoint<T>.BindRequestAsync` resolves `IOptions<MvcOptions>` from DI. If MVC is configured, uses input formatters for content negotiation (JSON, XML, custom). If not, falls back to `ReadFromJsonAsync`. No extra NuGet dependency — MVC types are in the shared framework.

## Remaining Open Questions

None — all design decisions have been resolved. Ready for implementation.
