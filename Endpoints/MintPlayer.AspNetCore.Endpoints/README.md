# MintPlayer.AspNetCore.Endpoints

A class-per-endpoint library for ASP.NET Core Minimal APIs with constructor injection, content negotiation, and source-generated endpoint discovery.

## Installation

```bash
dotnet add package MintPlayer.AspNetCore.Endpoints
```

The source generator is bundled with the package and works automatically.

## Quick start

### 1. Define an endpoint

```csharp
using MintPlayer.AspNetCore.Endpoints;

public class HealthCheck : IGetEndpoint
{
    public static string Path => "/health";

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(new { status = "healthy" }));
}
```

### 2. Register all endpoints in `Program.cs`

The source generator creates an extension method named after your assembly. For example, assembly `MyApp.Api` generates `MapMyAppApiEndpoints()`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapMyAppApiEndpoints();

app.Run();
```

You can override the method name with an assembly attribute:

```csharp
using MintPlayer.AspNetCore.Endpoints;

[assembly: EndpointsMethodName("MapEndpoints")]

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapEndpoints();

app.Run();
```

## Endpoint levels

The library provides three levels of endpoint abstraction:

### Level 1: Raw endpoint (`IEndpoint`)

Full control over `HttpContext`. No base class or request binding involved.

```csharp
public class HealthCheck : IGetEndpoint
{
    public static string Path => "/health";

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
}
```

### Level 2: Typed request (`IEndpoint<TRequest>`)

Automatic request binding. The class **must be `partial`** so the generator can add the appropriate base class.

- **POST / PUT / PATCH** endpoints get content-negotiated body parsing for free (MVC input formatters with JSON fallback).
- **GET / DELETE** endpoints must override `BindRequestAsync` to parse route values, query strings, etc.

```csharp
public partial class UpdateUser : IPutEndpoint<UpdateUserRequest>, IMemberOf<UsersApi>
{
    public static string Path => "/{id}";

    public override Task<IResult> HandleAsync(UpdateUserRequest request, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { id = request.Id, name = request.Name }));
    }
}
```

```csharp
public partial class DeleteUser : IDeleteEndpoint<GetUserRequest>, IMemberOf<UsersApi>
{
    public static string Path => "/{id}";

    protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
    {
        var id = int.Parse(context.Request.RouteValues["id"]!.ToString()!);
        return ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(id));
    }

    public override Task<IResult> HandleAsync(GetUserRequest request, CancellationToken ct)
    {
        return Task.FromResult(Results.NoContent());
    }
}
```

### Level 3: Typed request + response (`IEndpoint<TRequest, TResponse>`)

Same as Level 2, plus the source generator emits `.Produces<TResponse>(statusCode)` for OpenAPI/Swagger documentation.

Override `SuccessStatusCode` to change the documented status code (default: `200`).

```csharp
public partial class CreateUser : IPostEndpoint<CreateUserRequest, CreateUserResponse>, IMemberOf<UsersApi>
{
    public static string Path => "/";

    static int IEndpoint<CreateUserRequest, CreateUserResponse>.SuccessStatusCode => 201;

    public override Task<IResult> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        var response = new CreateUserResponse(42, request.Name, request.Email);
        return Task.FromResult(Results.Created($"/api/users/42", response));
    }
}
```

## HTTP method interfaces

Convenience interfaces automatically provide the HTTP method. Each comes in three levels:

| Interface | HTTP method | Body parsing |
|-----------|-------------|--------------|
| `IGetEndpoint` / `IGetEndpoint<TReq>` / `IGetEndpoint<TReq, TResp>` | GET | No (manual binding) |
| `IPostEndpoint` / `IPostEndpoint<TReq>` / `IPostEndpoint<TReq, TResp>` | POST | Yes |
| `IPutEndpoint` / `IPutEndpoint<TReq>` / `IPutEndpoint<TReq, TResp>` | PUT | Yes |
| `IPatchEndpoint` / `IPatchEndpoint<TReq>` / `IPatchEndpoint<TReq, TResp>` | PATCH | Yes |
| `IDeleteEndpoint` / `IDeleteEndpoint<TReq>` / `IDeleteEndpoint<TReq, TResp>` | DELETE | No (manual binding) |

For custom or multiple HTTP methods, implement `IEndpoint` directly and provide `Methods`:

```csharp
public class PreflightEndpoint : IEndpoint
{
    public static string Path => "/api/{**path}";
    public static IEnumerable<string> Methods => ["OPTIONS", "HEAD"];

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok());
}
```

## Route groups

Group endpoints under a shared prefix with `IEndpointGroup` and `IMemberOf<TGroup>`:

```csharp
public class UsersApi : IEndpointGroup
{
    public static string Prefix => "/api/users";

    static void IEndpointGroup.Configure(RouteGroupBuilder group)
    {
        group.WithTags("Users");
    }
}
```

Endpoint paths become relative to the group prefix:

```csharp
// Resolves to GET /api/users/
public class ListUsers : IGetEndpoint, IMemberOf<UsersApi>
{
    public static string Path => "/";

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(new[] { new { Id = 1, Name = "Alice" } }));
}

// Resolves to GET /api/users/{id}
public partial class GetUser : IGetEndpoint<GetUserRequest, UserResponse>, IMemberOf<UsersApi>
{
    public static string Path => "/{id}";

    protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
    {
        var id = int.Parse(context.Request.RouteValues["id"]!.ToString()!);
        return ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(id));
    }

    public override Task<IResult> HandleAsync(GetUserRequest request, CancellationToken ct)
    {
        var user = new UserResponse(request.Id, "Alice", "alice@example.com");
        return Task.FromResult(Results.Ok(user));
    }
}
```

### Nested groups

Groups can be nested at any depth by implementing `IMemberOf<TParentGroup>` on a group class:

```csharp
// Root group: /api
public class ApiGroup : IEndpointGroup
{
    public static string Prefix => "/api";
}

// Nested under ApiGroup: /api/users
public class UsersApi : IEndpointGroup, IMemberOf<ApiGroup>
{
    public static string Prefix => "/users";

    static void IEndpointGroup.Configure(RouteGroupBuilder group)
    {
        group.WithTags("Users");
    }
}

// Nested under ApiGroup: /api/products
public class ProductsApi : IEndpointGroup, IMemberOf<ApiGroup>
{
    public static string Prefix => "/products";

    static void IEndpointGroup.Configure(RouteGroupBuilder group)
    {
        group.WithTags("Products");
    }
}

// Resolves to GET /api/users/
public class ListUsers : IGetEndpoint, IMemberOf<UsersApi>
{
    public static string Path => "/";
    // ...
}

// Resolves to GET /api/products/
public class ListProducts : IGetEndpoint, IMemberOf<ProductsApi>
{
    public static string Path => "/";
    // ...
}
```

The generator emits nested `MapGroup` calls:

```csharp
var grp0 = MapGroup<ApiGroup>(app);          // /api
{
    var grp1 = MapGroup<UsersApi>(grp0);      // /api/users
    Map<ListUsers>(grp1, _f0);
}
{
    var grp2 = MapGroup<ProductsApi>(grp0);   // /api/products
    Map<ListProducts>(grp2, _f1);
}
```

Nesting works at any depth. Each group's `Prefix` is relative to its parent.

## Route configuration

Use the static `Configure` method to add authorization, caching, rate limiting, CORS, or other endpoint metadata:

```csharp
public class SecureEndpoint : IGetEndpoint
{
    public static string Path => "/api/secret";

    static void IEndpointBase.Configure(RouteHandlerBuilder builder)
    {
        builder.RequireAuthorization("AdminPolicy");
    }

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok("secret data"));
}
```

## Dependency injection

Endpoints are instantiated per-request using `ActivatorUtilities`, so constructor injection works:

```csharp
public partial class CreateUser : IPostEndpoint<CreateUserRequest, CreateUserResponse>, IMemberOf<UsersApi>
{
    private readonly IUserService _userService;
    private readonly ILogger<CreateUser> _logger;

    public CreateUser(IUserService userService, ILogger<CreateUser> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    public static string Path => "/";

    public override async Task<IResult> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Creating user {Name}", request.Name);
        var user = await _userService.CreateAsync(request, ct);
        return Results.Created($"/api/users/{user.Id}", user);
    }
}
```

Endpoints also support `IDisposable` and `IAsyncDisposable` for cleanup:

```csharp
public partial class GetUser : IGetEndpoint<GetUserRequest, UserResponse>, IMemberOf<UsersApi>
{
    public static string Path => "/{id}";

    // ...

    public override ValueTask DisposeAsync()
    {
        // Cleanup resources
        return ValueTask.CompletedTask;
    }
}
```

## Content negotiation

POST, PUT, and PATCH endpoints automatically support content negotiation:

1. If `AddControllers()` or `AddMvc()` was called, MVC input formatters are used (supports JSON, XML, custom formatters).
2. Otherwise, falls back to `ReadFromJsonAsync<T>()`.

Override `BindRequestAsync` in body endpoints for custom binding (e.g., multi-source, form data):

```csharp
public partial class UploadFile : IPostEndpoint<UploadRequest>
{
    public static string Path => "/api/upload";

    protected override async ValueTask<UploadRequest?> BindRequestAsync(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync();
        return new UploadRequest(form.Files["file"]!, form["description"].ToString());
    }

    public override async Task<IResult> HandleAsync(UploadRequest request, CancellationToken ct)
    {
        // Process upload
        return Results.Ok();
    }
}
```

## Endpoint metadata

The `EndpointDescriptor` list is available on the generated extensions class for introspection:

```csharp
// Access all registered endpoint descriptors
var endpoints = MyAppEndpointsExtensions.Endpoints;

foreach (var ep in endpoints)
{
    Console.WriteLine($"{string.Join(", ", ep.Methods)} {ep.Path} -> {ep.HandlerType.Name}");
}
```

## Manual registration

For one-off registrations without the source generator:

```csharp
app.MapEndpoint<HealthCheck>();
```

## Analyzer diagnostics

The source generator emits diagnostics for common mistakes:

| Code | Description |
|------|-------------|
| MPEP001 | Endpoint class implements a typed endpoint interface and must be declared as `partial` |
| MPEP002 | Endpoint class already has a base class; the generator cannot add the required base class |
| MPEP003 | Endpoint class implements `IMemberOf<T>` for multiple groups; only one group is allowed |

## How the source generator works

The source generator discovers all classes implementing `IEndpointBase` and generates:

1. **Partial class declarations** for typed endpoints, adding the appropriate base class (`PostEndpoint<T>`, `GetEndpoint<T>`, etc.)
2. **Pre-compiled factory fields** using `ActivatorUtilities.CreateFactory<T>()` for fast endpoint instantiation
3. **A single `Map{Name}Endpoints()` extension method** that registers all endpoints, including route groups
4. **`.Produces<TResponse>(statusCode)`** calls for endpoints with a response type
5. **An `Endpoints` property** listing all registered `EndpointDescriptor` records

For a typed POST endpoint like:

```csharp
public partial class CreateUser : IPostEndpoint<CreateUserRequest, CreateUserResponse>, IMemberOf<UsersApi>
{
    // ...
}
```

The generator emits:

```csharp
// Partial class with base class
partial class CreateUser : PostEndpoint<CreateUserRequest> { }

// Factory field (pre-compiled, allocated once)
private static readonly ObjectFactory<CreateUser> _f0 =
    ActivatorUtilities.CreateFactory<CreateUser>(Type.EmptyTypes);

// Inside the mapping method
var grp = MapGroup<UsersApi>(app);
var b = Map<CreateUser>(grp, _f0);
Produces<CreateUser, CreateUserRequest, CreateUserResponse>(b);
```

## Full example

**Models:**

```csharp
public record CreateUserRequest(string Name, string Email);
public record CreateUserResponse(int Id, string Name, string Email);
public record GetUserRequest(int Id);
public record UserResponse(int Id, string Name, string Email);
public record UpdateUserRequest(int Id, string Name, string Email);
```

**Route groups (nested):**

```csharp
public class ApiGroup : IEndpointGroup
{
    public static string Prefix => "/api";
}

public class UsersApi : IEndpointGroup, IMemberOf<ApiGroup>
{
    public static string Prefix => "/users";

    static void IEndpointGroup.Configure(RouteGroupBuilder group)
    {
        group.WithTags("Users");
    }
}
```

**Endpoints:**

```csharp
public class ListUsers : IGetEndpoint, IMemberOf<UsersApi>
{
    public static string Path => "/";

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(new[]
        {
            new { Id = 1, Name = "Alice", Email = "alice@example.com" },
            new { Id = 2, Name = "Bob", Email = "bob@example.com" },
        }));
}

public partial class GetUser : IGetEndpoint<GetUserRequest, UserResponse>, IMemberOf<UsersApi>
{
    public static string Path => "/{id}";

    protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
    {
        var id = int.Parse(context.Request.RouteValues["id"]!.ToString()!);
        return ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(id));
    }

    public override Task<IResult> HandleAsync(GetUserRequest request, CancellationToken ct)
    {
        var user = new UserResponse(request.Id, "Alice", "alice@example.com");
        return Task.FromResult(Results.Ok(user));
    }
}

public partial class CreateUser : IPostEndpoint<CreateUserRequest, CreateUserResponse>, IMemberOf<UsersApi>
{
    public static string Path => "/";
    static int IEndpoint<CreateUserRequest, CreateUserResponse>.SuccessStatusCode => 201;

    public override Task<IResult> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        var response = new CreateUserResponse(42, request.Name, request.Email);
        return Task.FromResult(Results.Created($"/api/users/42", response));
    }
}

public partial class UpdateUser : IPutEndpoint<UpdateUserRequest>, IMemberOf<UsersApi>
{
    public static string Path => "/{id}";

    public override Task<IResult> HandleAsync(UpdateUserRequest request, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { id = request.Id, name = request.Name }));
    }
}

public partial class DeleteUser : IDeleteEndpoint<GetUserRequest>, IMemberOf<UsersApi>
{
    public static string Path => "/{id}";

    protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
    {
        var id = int.Parse(context.Request.RouteValues["id"]!.ToString()!);
        return ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(id));
    }

    public override Task<IResult> HandleAsync(GetUserRequest request, CancellationToken ct)
    {
        return Task.FromResult(Results.NoContent());
    }
}
```

**Program.cs:**

```csharp
using MintPlayer.AspNetCore.Endpoints;

[assembly: EndpointsMethodName("MapEndpoints")]

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapEndpoints();

app.Run();
```
