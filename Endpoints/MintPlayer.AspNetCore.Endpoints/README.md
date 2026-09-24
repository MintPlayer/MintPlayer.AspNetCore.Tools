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
        return Task.FromResult(Results.Created(Routes.Api.Users.GetUser(id: response.Id), response));
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

A class may also declare its own `Methods` while implementing a convenience interface — the class
member is more specific than the interface's, so it wins:

```csharp
public class HealthCheck : IGetEndpoint
{
    public static string Path => "/health";
    public static IEnumerable<string> Methods => ["GET", "HEAD"];

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok());
}
```

A class implementing **two** convenience interfaces must do this — otherwise neither verb is most
specific and the compiler reports `CS8705`. Declaring `Methods` resolves it and yields the union.

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
        return Results.Created(Routes.Api.Users.GetUser(id: user.Id), user);
    }
}
```

Endpoints also support `IDisposable` and `IAsyncDisposable` for cleanup. Both registration paths
prefer `IAsyncDisposable`, and the base class's `DisposeAsync` forwards to `Dispose()`, so overriding
either one is enough:

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

## Binding failures

A request the library cannot bind never reaches your `HandleAsync(TRequest, CancellationToken)` —
its signature promises a non-null request, so handing it a null would turn a malformed request into
a 500 from inside your own code. Instead the bridge answers:

| Request | Response |
|---------|----------|
| Empty, whitespace-only or malformed JSON body | `400 Bad Request` |
| A content type the endpoint cannot read | `415 Unsupported Media Type` |
| A literal JSON `null` body, or a binder returning `default` | `400 Bad Request` |
| An input formatter that recorded validation errors | `400 Bad Request`, with the errors in the detail |

Custom binders say the same thing by throwing `EndpointBindingException`:

```csharp
protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
{
    if (!int.TryParse(context.Request.RouteValues["id"]?.ToString(), out var id))
        throw new EndpointBindingException(StatusCodes.Status400BadRequest, "The id must be an integer.");

    return ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(id));
}
```

Any *other* exception from your binder propagates untouched: the library cannot tell a malformed
request from a bug in the binder, and guessing `400` would hide the real ones.

Override `OnBindFailedAsync` to change the response — to add problem details, to log, or to map a
failure onto a different status code:

```csharp
protected override ValueTask<IResult> OnBindFailedAsync(HttpContext context, EndpointBindingException? failure)
    => new(Results.Problem(
        statusCode: failure?.StatusCode ?? StatusCodes.Status400BadRequest,
        title: "The request could not be read."));
```

`failure` is `null` when binding completed but produced no request at all.

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

`Path` is the **fully resolved** route, with group prefixes composed in — the route a request has to
use, not the group-relative `TEndpoint.Path`. So a grouped endpoint appears as `/api/users/{id}`.

`Name` is the class name, unless `[EndpointDescriptorName]` says otherwise:

```csharp
[EndpointDescriptorName("Health")]
public class HealthCheckEndpoint : IGetEndpoint
{
    // ... described as "Health"
}
```

`EndpointDescriptor` compares by value, including its `Methods` list, so descriptors work as
dictionary keys and in sets.

## Endpoint names and typed links

Every generated mapping is named with `.WithName(name)`, where the name is the class name or its
`[EndpointDescriptorName("…")]`. That one name is the ASP.NET Core route name, the OpenAPI
`operationId` (suffixed with the method for an endpoint that answers several), and the name of the
endpoint's method on the generated `Routes` class:

```csharp
return Results.Created(Routes.Api.Users.GetUser(id: user.Id), user);   // "/api/users/42"
```

`Routes` is `internal`, with nested classes mirroring the groups (`UsersApi` → `Users`: one trailing
`Api` or `Group` is dropped). Each method takes one parameter per route token — typed from its
`[RouteParam]` property, `string` otherwise, optional for `{id?}`, `{n=1}` and catch-alls — then one
optional parameter per `[QueryParam]`. It returns an `EndpointRoute`, which converts to its path
implicitly; `Path(linkGenerator)` and `Path(httpContext)` ask the framework's `LinkGenerator`
instead (honouring `PathBase`, constraints and `LowercaseUrls`) and throw where it would return null.
A `{Name}Template` constant holds each composed route. An endpoint whose route is not a
compile-time constant gets no link.

## Manual registration

For one-off registrations without the source generator:

```csharp
app.MapEndpoint<HealthCheck>();
```

The endpoint is named exactly as the generated mapping would name it, but this path sees one
endpoint at a time and cannot prove the name unique: a duplicate throws on the first request.

Group membership is honoured, so an endpoint declaring `IMemberOf<UsersApi>` is mapped under its
group's prefix — the same route the generated mapping gives it, including nested groups and the
groups' `Configure` hooks. Each call builds its own group chain, so a group's `Configure` runs once
per call.

An endpoint (or group) declaring `IMemberOf<T>` for more than one group, and a cyclic group nesting,
have no single prefix to resolve; `MapEndpoint<T>()` throws `InvalidOperationException` rather than
picking one. The generator reports the same shapes as MPEP003, MPEP004 and MPEP005.

## Analyzer diagnostics

The source generator emits diagnostics for common mistakes:

| Code | Severity | Description |
|------|----------|-------------|
| MPEP001 | Error | Endpoint class implements a typed endpoint interface and must be declared as `partial` |
| MPEP002 | Error | Endpoint class already has a base class that does not derive from an endpoint base, so the generator cannot add the required one |
| MPEP003 | Error | Endpoint class implements `IMemberOf<T>` for multiple groups; only one group is allowed |
| MPEP004 | Error | Endpoint group implements `IMemberOf<T>` for multiple parent groups; only one parent is allowed |
| MPEP005 | Error | Endpoint group is nested inside itself through `IMemberOf<T>` |
| MPEP006 | Warning | The mapping method name had to be adjusted, because the assembly name or `[assembly: EndpointsMethodName]` is not a valid C# identifier |
| MPEP007 | Warning | Two endpoints answer the same verb on the same composed route — an ambiguous-match 500 at run time. Case, a trailing slash and parameter names are ignored; constraints are not; `/users/me` beside `/users/{id}` is fine and not reported. A custom `Methods` counts only when written as a literal collection or an `HttpVerbs` field |
| MPEP008 | Warning | A `{token}` in a typed endpoint's own `Path` has no `[RouteParam]` property, so the handler cannot read it (raw endpoints are not checked) |
| MPEP009 | Error | A `[RouteParam]` property names a parameter the composed route does not have, so every request is a 400 |
| MPEP010 | Warning | `Path` already begins with its group's composed prefix, so the endpoint maps at the prefix twice |
| MPEP011 | Info | `Path` is not a compile-time constant, so the route checks above are skipped for it |
| MPEP012 | Error | Two endpoints have the same endpoint name (the class name, or `[EndpointDescriptorName]`) — ASP.NET Core would throw on the first request. The later one is mapped without a name and gets no typed link |
| MPEP016 | Info | A declared group is never joined by any endpoint, so it is not mapped |
| MPEP018 | Info | The single type argument of `IGetEndpoint<T>`/`IDeleteEndpoint<T>` (the response) is named like a request (`*Request`, `*Body`, `*Command`) |

The route checks (MPEP007–MPEP010) only run where the route can be read at compile time — a
constant `Path` and constant group `Prefix`es. Anything else is skipped silently, never guessed.

A base class that **already** derives from one of the endpoint bases (`PostEndpoint<T>`,
`GetEndpoint<T>`, …) is the supported way to share endpoint behaviour and reports nothing — the
generator has nothing left to add, and such a class need not be `partial`.

An endpoint dropped for MPEP003 still gets its generated base class, so the ambiguous group is the
only error you see.

## How the source generator works

The source generator discovers all classes implementing `IEndpointBase` and generates:

1. **Partial class declarations** for typed endpoints, adding the appropriate base class (`PostEndpoint<T>`, `GetEndpoint<T>`, etc.)
2. **Pre-compiled factory fields** using `ActivatorUtilities.CreateFactory<T>()` for fast endpoint instantiation
3. **A single `Map{Name}Endpoints()` extension method** that registers all endpoints, including route groups
4. **`.Produces<TResponse>(statusCode)`** calls for endpoints with a response type
5. **An `Endpoints` property** listing all registered `EndpointDescriptor` records

The extensions class is emitted into `MintPlayer.AspNetCore.Endpoints.Generated`, together with a
`global using` for that namespace — so `app.Map{Name}Endpoints()` resolves with no extra import, and
a method name that happens to strip down to a shipped type's name cannot shadow it. The emitted file
carries the using directives its own calls need, so it also compiles in a plain `Microsoft.NET.Sdk`
project and with `<ImplicitUsings>disable</ImplicitUsings>`.

Emission is ordered by fully qualified name throughout — routes, factory fields and descriptors —
so the generated file is identical across builds. An assembly with no endpoints still gets its
mapping method, as a no-op.

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
        return Task.FromResult(Results.Created(Routes.Api.Users.GetUser(id: response.Id), response));
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
