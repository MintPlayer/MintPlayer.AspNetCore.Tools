# MintPlayer.AspNetCore.Endpoints

One class per endpoint for ASP.NET Core Minimal APIs. A source generator discovers the classes and
writes the mapping, typed links, OpenAPI metadata and a cross-assembly contract a typed client can be
generated from. Endpoints get constructor injection, content negotiation and explicit route/query
binding. Targets .NET 10 and .NET 11.

| Package | What it is |
|---|---|
| `MintPlayer.AspNetCore.Endpoints` | Runtime library + generator + code fix. Reference this in the server. |
| `MintPlayer.AspNetCore.Endpoints.Abstractions` | The interfaces and attributes (`IGetEndpoint`, `IEndpointGroup`, `[MemberOf<T>]`, `[RouteParam]`, …). The main package references it. |
| `MintPlayer.AspNetCore.Endpoints.Generator` | Analyzer-only (no `FrameworkReference`). Reference this in a [typed client](#typed-client-in-another-project). |

```bash
dotnet add package MintPlayer.AspNetCore.Endpoints
```

## Quick start

An endpoint is a class with a static `Path` and a handler:

```csharp
using MintPlayer.AspNetCore.Endpoints;

public class HealthCheck : IGetEndpoint
{
    public static string Path => "/health";

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(new { status = "healthy" }));
}
```

The generator writes one `Map{AssemblyName}Endpoints()` extension that maps every endpoint in the
assembly. For assembly `MyShop.Api`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapMyShopApiEndpoints();

app.Run();
```

`[assembly: EndpointsMethodName("MapEndpoints")]` renames it. The method lives in
`MintPlayer.AspNetCore.Endpoints.Generated`, which the generated file imports with a `global using`.

## The interface ladder

Pick the rung that matches what the handler needs. Anything with a type argument must be `partial`:
the generator adds the base class that binds the request (MPEP001 otherwise, with a one-click fix).

| Interface | You write | Request body |
|---|---|---|
| `IGetEndpoint`, `IPostEndpoint`, `IPutEndpoint`, `IPatchEndpoint`, `IDeleteEndpoint`, `IEndpoint` | `Task<IResult> HandleAsync(HttpContext)` | read it yourself |
| `IGetEndpoint<TResponse>`, `IDeleteEndpoint<TResponse>` | `override Task<IResult> HandleAsync(CancellationToken)` | none |
| `IPostEndpoint<TRequest>`, `IPutEndpoint<TRequest>`, `IPatchEndpoint<TRequest>` | `override Task<IResult> HandleAsync(TRequest, CancellationToken)` | `TRequest`, content-negotiated |
| `I…Endpoint<TRequest, TResponse>` (every verb) | the same | `TRequest`, and `TResponse` is documented as the success response |
| `IEndpoint<TRequest>` / `IEndpoint<TRequest, TResponse>` (custom `Methods`) | the same, plus `override BindRequestAsync` | whatever you bind |

**Watch the one-argument rung.** `IGetEndpoint<T>` and `IDeleteEndpoint<T>` take the **response**
type — GET and DELETE have no body. `IPostEndpoint<T>`, `IPutEndpoint<T>` and `IPatchEndpoint<T>` take
the **request**. With two arguments it is always request, then response. (MPEP018 hints when the single
argument of a GET/DELETE is named like a request.) A GET or DELETE that does declare a request reads it
from the body like a POST.

`SuccessStatusCode` (default 200) sets the documented success status:
`static int IEndpoint<TRequest, TResponse>.SuccessStatusCode => 201;`, or
`static int IResponseEndpoint<TResponse>.SuccessStatusCode => …` on the response-only rung.

### A sample API

The rest of this README uses these groups, models and endpoints:

```csharp
using MintPlayer.AspNetCore.Endpoints;

public class ApiGroup : IEndpointGroup
{
    public static string Prefix => "/api";
}

[MemberOf<ApiGroup>]
public class UsersApi : IEndpointGroup
{
    public static string Prefix => "/users";

    static void IEndpointGroup.Configure(RouteGroupBuilder group) => group.WithTags("Users");
}

public record UserResponse(int Id, string Name, string Email);
public record CreateUserRequest(string Name, string Email);
public record RenameUserBody(string Name);

public interface IUserStore
{
    UserResponse? Find(int id);
    IReadOnlyList<UserResponse> List(int page, string? search);
    UserResponse Add(string name, string email);
    bool Rename(int id, string name);
    bool Remove(int id);
}
```

```csharp
using MintPlayer.AspNetCore.Endpoints;

// GET /api/users?page=2&search=al — raw rung, query values bound onto the endpoint
[MemberOf<UsersApi>]
public partial class ListUsers(IUserStore users) : IGetEndpoint
{
    public static string Path => "/";

    [QueryParam] public int Page { get; set; } = 1;
    [QueryParam] public string? Search { get; set; }

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(users.List(Page, Search)));
}

// GET /api/users/{id} — one type argument on GET is the RESPONSE
[MemberOf<UsersApi>]
public partial class GetUser(IUserStore users) : IGetEndpoint<UserResponse>
{
    public static string Path => "/{id}";

    [RouteParam] public int Id { get; set; }

    public override Task<IResult> HandleAsync(CancellationToken ct)
        => Task.FromResult(users.Find(Id) is { } user ? Results.Ok(user) : Results.NotFound());
}

// POST /api/users — request and response
[MemberOf<UsersApi>]
public partial class CreateUser(IUserStore users) : IPostEndpoint<CreateUserRequest, UserResponse>
{
    public static string Path => "/";

    static int IEndpoint<CreateUserRequest, UserResponse>.SuccessStatusCode => 201;

    public override Task<IResult> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        var user = users.Add(request.Name, request.Email);
        return Task.FromResult(Results.Created(Routes.Api.Users.GetUser(id: user.Id), user));
    }
}

// PUT /api/users/{id} — the id from the route, the rest from the body
[MemberOf<UsersApi>]
public partial class RenameUser(IUserStore users) : IPutEndpoint<RenameUserBody>
{
    public static string Path => "/{id}";

    [RouteParam] public int Id { get; set; }

    public override Task<IResult> HandleAsync(RenameUserBody request, CancellationToken ct)
        => Task.FromResult(users.Rename(Id, request.Name) ? Results.NoContent() : Results.NotFound());
}

// DELETE /api/users/{id}
[MemberOf<UsersApi>]
public partial class DeleteUser(IUserStore users) : IDeleteEndpoint
{
    public static string Path => "/{id}";

    [RouteParam] public int Id { get; set; }

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(users.Remove(Id) ? Results.NoContent() : Results.NotFound());
}
```

### Custom verbs

A class may declare `Methods` itself; the class member is more specific than the interface's, so it
wins (a class implementing two verb interfaces must do this, or the compiler reports `CS8705`). For
anything else implement `IEndpoint`:

```csharp
using MintPlayer.AspNetCore.Endpoints;

public class Preflight : IEndpoint
{
    public static string Path => "/api/{**path}";
    public static IEnumerable<string> Methods => ["OPTIONS", "HEAD"];

    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
}
```

`HttpVerbs.Get`, `HttpVerbs.Post`, … hold the standard lists.

## Route and query values

**Only properties marked `[RouteParam]` or `[QueryParam]` are bound from the URL, and the body binds to
`TRequest` and nothing else.**

- The endpoint must be `partial` (MPEP014; MPEP001 on a typed endpoint), because the binder is
  generated into it, and the property needs a `set` accessor (MPEP020).
- The key is the property name, case-insensitive; `[RouteParam("userId")]` / `[QueryParam("q")]`
  names it explicitly. A `[RouteParam]` whose key is not in the composed route is MPEP009; a `{token}`
  on a typed endpoint that nothing binds is MPEP008.
- Supported types: `string`, any enum (case-insensitive, must be a defined value), and any type
  implementing `IParsable<T>` (`int`, `Guid`, `DateOnly`, `decimal`, …), parsed with the invariant
  culture. Anything else is MPEP013. There is no reflective fallback.
- A nullable property is optional (absent → `null`); a property with an initializer keeps it when
  the value is absent; any other property is required.
- Failures answer `400` before the handler runs, as `application/problem+json`:
  `The route parameter 'id' must be a valid Int32; 'abc' is not.`,
  `The query parameter 'kind' must be one of: Book, Vinyl; 'x' is not.`,
  `The query parameter 'page' is required.` A typed endpoint routes them through
  `OnBindFailedAsync` (below); a raw endpoint has no base class, so its failures are not customisable.

**Why explicit, not by convention.** Binding happens on the endpoint class, and every settable
property of it would otherwise be writable from the URL — including state you set from DI. Convention
would make `?anyProperty=…` an over-posting hole by default. A route convention would also bind or not
depending on whether the generator can read `Path` (it cannot for a computed one, or across
assemblies). One attribute per value is the price.

URL fragments (`#section`) cannot be bound: browsers never send them to the server (RFC 3986 §3.5).

## The request body

POST, PUT and PATCH endpoints (and GET/DELETE with a request type) read `TRequest` from the body:

1. With `AddControllers()`/`AddMvc()`, through MVC's input formatters — JSON, XML, your own.
2. Otherwise with `ReadFromJsonAsync<TRequest>()`.

A request that cannot be bound never reaches `HandleAsync`:

| Request | Response |
|---|---|
| Empty, whitespace-only or malformed JSON | `400` |
| A content type no formatter reads | `415` |
| A literal JSON `null`, or a binder returning `default` | `400` |
| An input formatter that recorded model errors | `400`, errors in the detail |

Override `BindRequestAsync` to bind differently, and throw `EndpointBindingException` to reject the
request. Override `OnBindFailedAsync` to change the response for every bind failure, route and query
ones included (`failure` is `null` when binding produced no request; on the response-only rung it is
never `null`):

```csharp
using MintPlayer.AspNetCore.Endpoints;

public record AvatarUpload(IFormFile File, string? Caption);

public partial class UploadAvatar : IPostEndpoint<AvatarUpload>
{
    public static string Path => "/avatars/{userId}";

    [RouteParam] public int UserId { get; set; }

    protected override async ValueTask<AvatarUpload?> BindRequestAsync(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (form.Files.GetFile("file") is not { } file)
            throw new EndpointBindingException(StatusCodes.Status400BadRequest, "A 'file' part is required.");

        return new AvatarUpload(file, form["caption"]);
    }

    protected override ValueTask<IResult> OnBindFailedAsync(HttpContext context, EndpointBindingException? failure)
        => new(Results.Problem(
            statusCode: failure?.StatusCode ?? StatusCodes.Status400BadRequest,
            title: "The upload could not be read.",
            detail: failure?.Message));

    public override Task<IResult> HandleAsync(AvatarUpload request, CancellationToken ct)
        => Task.FromResult(Results.NoContent());
}
```

Any other exception from a binder propagates: the library cannot tell a malformed request from a bug.

## Groups

A group is a class implementing `IEndpointGroup` with a `Prefix` and an optional `Configure`. An
endpoint joins it with `[MemberOf<TGroup>]`; a group joins a parent group the same way, to any depth.
`Path` is relative to the composed prefix (`/api` + `/users` + `/{id}`); repeating the prefix in `Path`
is MPEP010. `[MemberOf<T>]` allows one group per class, which the compiler enforces (`CS0579`); a cycle
is MPEP005, and a group no endpoint joins is not mapped (MPEP016).

Membership **inherits**: `[MemberOf<T>]` on a base class applies to every endpoint deriving from it, and
the nearest declaration wins, so a derived class can move to another group:

```csharp
using MintPlayer.AspNetCore.Endpoints;

public class AdminApi : IEndpointGroup
{
    public static string Prefix => "/admin";
}

[MemberOf<AdminApi>]
public abstract class AdminEndpoint;

// GET /admin/audit — joins AdminApi through its base class
public class AuditLog : AdminEndpoint, IGetEndpoint
{
    public static string Path => "/audit";

    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
}

// GET /api/status — its own [MemberOf] is nearer than the base class's
[MemberOf<ApiGroup>]
public class ServerStatus : AdminEndpoint, IGetEndpoint
{
    public static string Path => "/status";

    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
}
```

A base class of your own blocks the generated one (MPEP002 on a typed endpoint) — unless it already
derives from a library base (`PostEndpoint<T>`, `GetEndpoint<T>`, `ResponseEndpoint`, …), which is
the supported way to share typed endpoint behaviour; such a class need not be `partial`.

## Dependency injection

An endpoint is created for **every request** from `HttpContext.RequestServices`, by a factory
`ActivatorUtilities` compiles once. So constructor injection — primary constructors included, as in the
sample API — gets scoped services from the request's scope, exactly like a controller: register
`builder.Services.AddScoped<IUserStore, UserStore>()` and each request gets its own `IUserStore`, shared
with everything else in that request. `IDisposable` and `IAsyncDisposable` endpoints are disposed when the
handler returns (`IAsyncDisposable` preferred; the base classes' `DisposeAsync` forwards to `Dispose()`).
Because the endpoint is per request, bound properties never leak between requests.

## Attributes, `Configure` and endpoint metadata

Every attribute on the endpoint class, inherited ones included, becomes endpoint metadata, so the framework's own attributes work
as they do on a controller action — verified end to end for `[Authorize]`, `[Authorize(Policy = …)]`,
`[EnableRateLimiting]`, `[Tags]` and `[ProducesResponseType]`. For anything imperative, implement
`Configure`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MintPlayer.AspNetCore.Endpoints;

[Authorize(Policy = "Reports")]
[EnableRateLimiting("reports")]
[Tags("Reports")]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public partial class DownloadReport : IGetEndpoint
{
    public static string Path => "/reports/{name}";

    [RouteParam] public string Name { get; set; } = "";

    static void IEndpointBase.Configure(RouteHandlerBuilder builder) => builder.RequireCors("reports");

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Text($"report {Name}", "text/csv"));
}
```

A group's `Configure` receives its `RouteGroupBuilder`, as `UsersApi` above shows.

## Validation

Validation runs on the **body** only, after binding and before the handler, with
`Microsoft.Extensions.Validation`. It runs when all of these hold:

- The request type carries `[ValidatableType]` in **hand-written** code. The library cannot mark it for
  you: one source generator never sees another's output. A request type with DataAnnotations but no
  `[ValidatableType]` is MPEP015.
- The assembly declaring that type calls `AddValidation()` — **exactly once**. On .NET 10 a second
  call site in the same assembly fails the validation generator with `CS8785` (a warning) and
  registers no resolver, so every type in that assembly silently stops validating. Keep the one call in
  a wrapper and call the wrapper from `Program.cs` — never both.
- On .NET 10, the project has `<NoWarn>$(NoWarn);ASP0029</NoWarn>`: the validation API is
  `[Experimental]` there and reports as an error. .NET 11 needs nothing.

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using MintPlayer.AspNetCore.Endpoints;

[ValidatableType]
public record RegisterUserRequest([Required] string Name, [Required, EmailAddress] string Email);

public partial class RegisterUser(IUserStore users) : IPostEndpoint<RegisterUserRequest, UserResponse>
{
    public static string Path => "/register";

    public override Task<IResult> HandleAsync(RegisterUserRequest request, CancellationToken ct)
        => Task.FromResult(Results.Ok(users.Add(request.Name, request.Email)));

    // Optional: the default is a 400 problem+json with the errors.
    protected override ValueTask<IResult> OnValidationFailedAsync(HttpContext context, IReadOnlyDictionary<string, string[]> errors)
        => new(Results.ValidationProblem(errors, title: "The registration is invalid."));
}

// This assembly's one AddValidation() call site; Program.cs calls builder.Services.AddMyShopValidation().
public static class MyShopValidation
{
    public static IServiceCollection AddMyShopValidation(this IServiceCollection services)
        => services.AddValidation();
}
```

A request type declared in **another** assembly is only validated if that assembly calls
`AddValidation()` itself: give it the same kind of wrapper and call both wrappers from the app. (If
you cannot change it, declare a local `[ValidatableType]` type with a property of the external type;
the validation generator then recurses into it.)

`[Range]` and friends on a `[RouteParam]`/`[QueryParam]` property are **not** evaluated — the endpoint
is not a validatable type. The binder enforces the type; put business rules in `HandleAsync`. (They do
reach the OpenAPI schema as `minimum`/`maximum`/`maxLength`, so the document advertises a constraint
nothing checks.) Error keys are member paths (`Name`, `Address.Street`); an `IValidatableObject` result
naming no member lands under `""`.

## Endpoint names and typed links

Every endpoint is mapped with `.WithName(name)`: its class name, or `[EndpointDescriptorName("…")]`.
That one name is the route name, the OpenAPI `operationId` and the typed-link method name, so it must
be unique in the assembly — a duplicate is MPEP012 at build time rather than the
`Duplicate endpoint name` exception ASP.NET Core throws on the first request. Name through the
attribute: a `WithName` inside `Configure` is overridden.

The generated `internal static class Routes` mirrors the groups (one trailing `Api` or `Group` is
dropped: `ApiGroup` → `Api`, `UsersApi` → `Users`) with one method per endpoint. Its parameters are the
route tokens — typed from their `[RouteParam]` property, `string` otherwise, optional for `{id?}`,
`{n=1}` and catch-alls — then one optional parameter per `[QueryParam]`. Each also has a
`{Name}Template` constant. It returns an `EndpointRoute`:

```csharp
using MintPlayer.AspNetCore.Endpoints;

public partial class UserLinks : IGetEndpoint
{
    public static string Path => "/links/{id}";

    [RouteParam] public int Id { get; set; }

    public Task<IResult> HandleAsync(HttpContext httpContext)
    {
        EndpointRoute link = Routes.Api.Users.GetUser(id: Id);

        return Task.FromResult(Results.Ok(new
        {
            path = link.ToString(),              // "/api/users/42" — also the implicit string conversion
            routed = link.Path(httpContext),     // through LinkGenerator, with the request's PathBase
            absolute = link.Uri(httpContext),    // scheme, host and PathBase
            template = Routes.Api.Users.GetUserTemplate,
        }));
    }
}
```

`ToString()` (and the implicit `string` conversion `Results.Created` uses) needs no `LinkGenerator`: it
reproduces routing's own substitution and `UrlEncoder.Default`, matching `LinkGenerator` byte for byte
with default options. It cannot see route constraints, `LowercaseUrls`, `LowercaseQueryStrings`,
`AppendTrailingSlash`, parameter transformers or a replaced `UrlEncoder`; if you use any of those, call
`Path(httpContext)` or `Path(linkGenerator)`, which ask the framework and throw where it would return
`null`. An endpoint whose route is not a compile-time constant, or whose name is a duplicate, gets no
link.

The descriptors are available too: `MyShopApiEndpointsExtensions.Endpoints` lists an
`EndpointDescriptor(Name, Path, Methods, HandlerType)` per endpoint, with the fully composed `Path`.

## OpenAPI

The mapping always declares the request body (`application/json`, required), `400` (and `415` where
there is a body) as problem details, and `Produces<TResponse>(SuccessStatusCode)`. A success status is
assumed when nothing declares one.

**Route and query parameters need `Microsoft.AspNetCore.OpenApi`.** When the server references it (a
version built on `Microsoft.OpenApi` 2.x or later, as the .NET 10 and 11 packages are), the generator also emits `EndpointOpenApi.g.cs` with typed
parameter schemas — `int`, enums with their values, `[Range]`/`[StringLength]` limits. Without the
package that file is not emitted and the runtime library takes no dependency on it. `operationId` is the
endpoint name; an endpoint answering several verbs gets the verb appended (`PreflightHead`) so the ids
stay unique.

`MapEndpoint<T>()` (below) documents the body and failures but **not** route or query parameters: it
has no compile-time parameter type to show ApiExplorer, so a templated route mapped by hand makes the
document invalid. Use the generated mapping for anything documented.

## Contract snapshot in CI

Commit the OpenAPI document and let CI compare it. `Microsoft.Extensions.ApiDescription.Server` (same
version as `Microsoft.AspNetCore.OpenApi`) writes it on every build:

```xml
<PropertyGroup>
  <OpenApiGenerateDocumentsOnBuild>true</OpenApiGenerateDocumentsOnBuild>
  <OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)\openapi</OpenApiDocumentsDirectory>
  <!-- The build-time tool ignores AddOpenApi's OpenApiVersion; pin it here as well. -->
  <OpenApiGenerateDocumentsOptions>--openapi-version OpenApi3_1</OpenApiGenerateDocumentsOptions>
</PropertyGroup>
```

Pin `OpenApiVersion` in `AddOpenApi` as well — the default differs between .NET 10 (3.1) and .NET 11
(3.2). A multi-targeted project writes the document for its first target framework only. Generating it
**runs your `Program.cs`**: put migrations and seeding behind
`Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider"`, but never skip `app.Run()`, or
the document has no paths. In CI, fail when the build leaves the file changed (`git status --porcelain`)
and run `oasdiff breaking base.json head.json --fail-on ERR` against the base branch's copy; this
repository's `pull-request.yml` does both.

## Typed client in another project

Every endpoint with a typed link is also described in the server assembly's metadata
(`[assembly: EndpointContract(…)]`): name, verbs, route, parameters and their types, request and response
types. A client project — Blazor WebAssembly, a console tool, another service — generates a typed
`HttpClient` wrapper from it without the server's source and without ASP.NET Core:

```xml
<PropertyGroup>
  <GenerateEndpointsClient>true</GenerateEndpointsClient>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="MintPlayer.AspNetCore.Endpoints.Generator" Version="…" PrivateAssets="all" />
  <!-- Request/response types, in an assembly both projects reference (recommended). -->
  <ProjectReference Include="..\MyShop.Contracts\MyShop.Contracts.csproj" />
  <!-- Builds the server first and references its assembly metadata-only: nothing is copied. -->
  <EndpointsServerReference Include="..\MyShop.Api\MyShop.Api.csproj" />
</ItemGroup>
```

That generates `internal sealed partial class {LastNameSegment}Client` (`MyShop.Api` → `ApiClient`) with
one `{Name}Async` method per endpoint (`{Name}{Verb}Async` for a multi-verb one): route values, then the
body, then optional query values, then a `CancellationToken`:

```csharp
using var http = new HttpClient { BaseAddress = new Uri("https://localhost:5001") };
var client = new ApiClient(http);

UserResponse? alice = await client.GetUserAsync(id: 1);
UserResponse? bob = await client.CreateUserAsync(new CreateUserRequest("Bob", "bob@example.com"));
using HttpResponseMessage page = await client.ListUsersAsync(page: 2, search: "al");
```

- URLs are built by the same source as `EndpointRoute.ToString()`, emitted into the client.
- A declared response type is read as JSON after `EnsureSuccessStatusCode()` (204 → `default`); without
  one you get the `HttpResponseMessage` to inspect and dispose.
- Renaming or removing a server endpoint breaks the **client's build**, not its first request.

**Never `ProjectReference` the server**: it drags `FrameworkReference Microsoft.AspNetCore.App` into the
client, and a Blazor WebAssembly client then fails with `NETSDK1082`. Without the package's targets, the
fallback is a raw reference plus the property made visible to the compiler:

```xml
<ItemGroup>
  <!-- Build the server first, in the same Configuration and target framework. -->
  <Reference Include="MyShop.Api">
    <HintPath>..\MyShop.Api\bin\$(Configuration)\net10.0\MyShop.Api.dll</HintPath>
    <Private>false</Private>
  </Reference>
  <CompilerVisibleProperty Include="GenerateEndpointsClient" />
</ItemGroup>
```

MPEP021 reports an endpoint left out of the client, MPEP022 a signature type declared in the server
assembly itself (compiles, but is not deployed with the client), MPEP023 a project missing
`System.Net.Http.Json`/`System.Text.Encodings.Web`. No contracts at all — or an IDE that momentarily
sees no references — is no client and no diagnostic; code using the client then fails to compile.

## Manual registration

`app.MapEndpoint<HealthCheck>();` maps one endpoint by reflection, under its group chain and with the
same name the generated mapping uses. It sees one endpoint at a time, so a duplicate name surfaces on the
first request, not as MPEP012; it cannot document route or query parameters (see
[OpenAPI](#openapi)); and it is annotated `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`. A
cyclic group nesting throws `InvalidOperationException`.

## Trimming and native AOT

Both runtime packages build with `<IsAotCompatible>true</IsAotCompatible>` and no trim or AOT warnings,
as a regression gate for the library's own code. **That is not an AOT claim.** The generated mapping
calls the `Delegate` overload of `MapMethods`, which `RequestDelegateFactory` binds by reflection at
startup; the Request Delegate Generator cannot intercept it, because one source generator never sees
another's output (measured: it intercepted a hand-written `MapGet` and none of the generated calls). The
body is read by MVC input formatters or `ReadFromJsonAsync<T>()`, neither of which this library makes
trim-safe. `MapEndpoint<T>()` says so with its annotations.

## Diagnostics

| Code | Severity | Meaning |
|---|---|---|
| MPEP001 | Error | A typed endpoint must be `partial` (code fix) |
| MPEP002 | Error | The endpoint's own base class does not derive from a library endpoint base, so the generated one cannot be added |
| MPEP003 | — | *Retired* (endpoint in two groups; now `CS0579`). Never reused |
| MPEP004 | — | *Retired* (group with two parents; now `CS0579`). Never reused |
| MPEP005 | Error | A group is nested inside itself through `[MemberOf<T>]` |
| MPEP006 | Warning | The mapping method name was adjusted to a valid identifier |
| MPEP007 | Warning | Two endpoints answer the same verb on the same composed route (an ambiguous-match 500) |
| MPEP008 | Warning | A `{token}` in a typed endpoint's `Path` is bound to no property |
| MPEP009 | Error | A `[RouteParam]` names a parameter the composed route does not have (every request would be a 400) |
| MPEP010 | Warning | `Path` repeats its group's prefix |
| MPEP011 | Info | `Path` is not a compile-time constant, so the route checks are skipped |
| MPEP012 | Error | Two endpoints have the same endpoint name; the later one gets no name and no link |
| MPEP013 | Error | A bound property's type is not `string`, an enum or `IParsable<T>` |
| MPEP014 | Error | An endpoint with bound properties must be `partial` (code fix) |
| MPEP015 | Warning | A request type has validation rules but no `[ValidatableType]`, so they never run |
| MPEP016 | Info | A group is joined by no endpoint and is not mapped |
| MPEP017 | — | *Reserved* |
| MPEP018 | Info | The single type argument of `IGetEndpoint<T>`/`IDeleteEndpoint<T>` (the response) is named like a request |
| MPEP019 | Error | The endpoint is nested in a type that is not `partial` (code fix) |
| MPEP020 | Error | A bound property has no usable `set` accessor |
| MPEP021 | Warning | *(client)* An endpoint contract cannot become a client method |
| MPEP022 | Warning | *(client)* A client method uses a type declared in the server assembly |
| MPEP023 | Warning | *(client)* `GenerateEndpointsClient` is set but a prerequisite assembly is missing |
| MPEP024 | Error | An endpoint or group is `private`, `protected`, `private protected` or `file`-local (or nested in such a type), so generated code cannot name it; it is not mapped |

The route checks (MPEP007–MPEP010) run only where the route is a compile-time constant; anything else
is skipped, never guessed. MPEP001, MPEP014 and MPEP019 have a **Make 'X' partial** code fix in Visual
Studio and Rider (for MPEP019, every enclosing type that is not yet `partial`), with Fix All.

## What the generator emits

Four files, fully qualified throughout (extension methods are called in their static form), with no
`using` directive beyond the one `global using` that exposes the mapping method — so they compile under
any import set and with `<ImplicitUsings>disable</ImplicitUsings>`: `EndpointMapping.g.cs` (the partial base classes and
binders, the `Map…Endpoints()` method, the descriptors), `EndpointRoutes.g.cs` (`Routes`),
`EndpointContracts.g.cs` (the client contract) and, with the OpenAPI package, `EndpointOpenApi.g.cs`.
Output is ordered by fully qualified name, so it is identical across builds.
