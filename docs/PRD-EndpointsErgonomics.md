# PRD: Endpoint ergonomics for MintPlayer.AspNetCore.Endpoints

## Overview

`MintPlayer.AspNetCore.Endpoints` shipped in `59eeda5` (#27) and gained a test suite in
`d60f87e` (#28). The library works. What it does not do is carry its own weight at the
declaration site: a consumer who wants one integer out of a route writes six lines of
binder, and the library's own sample ships that binder twice, byte for byte.

This is not a bug-fix release. It is a rewrite of the consumer-facing surface, plus the
generator capability that surface turns out to depend on — and a register of defects the
investigation uncovered along the way, several of which are live today and none of which
are visible in a green build.

The difference from the two documents that precede this one is the ratio of design to
execution. `PRD-Net11Migration` had one answer and the work was finding it.
Here five candidate designs were prototyped end to end and measured against a single
criterion — **how little code the downstream consumer writes** — and three of the five
turned out to be within six lines of each other. The interesting findings were not in
the ranking. They were in what each design could not do.

No backward compatibility is required. `IMemberOf<TGroup>` is deleted, not deprecated.

## Problem statement

### P1 — Every body-less endpoint hand-writes its own binder, and the sample duplicates it

`NonBodyEndpoint<TRequest>.BindRequestAsync` is abstract by design, so GET and DELETE
endpoints must supply it. `TestApp\Endpoints\GetUserEndpoint.cs:13-21` and
`TestApp\Endpoints\DeleteUserEndpoint.cs:13-21` are byte-identical, comment included:

```csharp
protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
{
    if (!int.TryParse(context.Request.RouteValues["id"]?.ToString(), out var id))
        throw new EndpointBindingException(StatusCodes.Status400BadRequest, "The id must be an integer.");

    return ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(id));
}
```

Two of eight sample endpoints. Measured across seven representative scenarios, the
status quo costs **122 non-blank consumer lines** where a generated binder costs **68**.
The status-quo endpoint class grows with the parameter count — 16 lines for one route
parameter plus one query flag, 22 for four. A generated one is 6 lines regardless.

The concept count matters more than the line count. To write that binder correctly a
consumer must know: the `BindRequestAsync` signature; `RouteValues` and its three
distinct no-value shapes; `Query`/`StringValues`; `EndpointBindingException` and which
status code to pass; the `ValueTask<T?>` null-means-400 protocol; `int.TryParse`
culture-sensitivity; and `Enum.IsDefined`, without which `Enum.TryParse("7")` yields an
undefined enum value and a 200. **Eleven concepts, against seven for the generated form.**

### P2 — Group membership is an interface, so it inherits silently and pollutes the contract

`IMemberOf<TGroup>` is read off `symbol.AllInterfaces` (`EndpointGenerator.cs:122-135`),
which means membership propagates through base classes with nothing at the derived
declaration site to say so. It also puts a routing concern into the type's interface
list, where it sits beside `IPostEndpoint<TReq,TRes>` — one of which says what the type
*is* and the other where it *lives*.

Two of the library's six diagnostics exist only to police shapes the compiler would
reject for free under an attribute. MPEP003 (endpoint in multiple groups) and MPEP004
(group with multiple parents) are both `AllowMultiple = false`, i.e. **CS0579**.

### P3 — The generator never captures the route, so nothing about routing is checkable

`EndpointInfo` has no route field. `EndpointGenerator.cs` never reads `Path`. The
producer emits `TEndpoint.Path` as a *runtime* expression, and the repo's own harness
records the consequence (`Tests\…\Infrastructure\GeneratedEndpointHost.cs`): *"routes
come from `TEndpoint.Path` … the only way to assert on their values is to run the emitted
code."*

This single gap is why every route-shaped defect below is undetectable, and why four
separate features cannot be built. It is ~40 lines to close.

### P4 — The OpenAPI document is structurally invalid, on every templated route

Dumped live from `TestApp` with `AddOpenApi()`/`MapOpenApi()` added:

```
POST    /api/users          parameters: []   requestBody: ABSENT   responses: ['201']
GET     /api/users/{id}     parameters: []   requestBody: ABSENT   responses: ['200']
PUT     /api/users/{id}     parameters: []   requestBody: ABSENT   responses: ['200']
DELETE  /api/users/{id}     parameters: []   requestBody: ABSENT   responses: ['200']
GET     /health             parameters: []   requestBody: ABSENT   responses: ['200']
```

OpenAPI's path-templating rule requires every `{token}` to have a corresponding
`required: true` path parameter. The three `/{id}` operations declare none, so they are
not merely sparse — they are **spec-invalid**, and Spectral's default ruleset, NSwag,
openapi-generator and Kiota each either error or emit a client that cannot fill the
segment.

**Cause**, verified in `EndpointMetadataApiDescriptionProvider` and reproduced:
`ApiDescription.ParameterDescriptions` is built exclusively from
`IParameterBindingMetadata`, one item per *delegate* parameter. The generated handler is
`async (HttpContext ctx) => …`; `HttpContext` classifies as `BindingSource.Services` and
is dropped. The route template is used only to *classify and name* parameters that
already exist — it never creates one. This is dotnet/aspnetcore#41773, open since 2022,
milestone Backlog.

**P4.1 — `DELETE /api/users/{id}` documents 200 and returns `Results.NoContent()`.** It is
a level-2 endpoint, so there is no `SuccessStatusCode` to override and ASP.NET Core's
default 200 wins. The library's own sample ships a wrong contract.

**P4.2 — the documented escape hatch does not exist.** `.WithOpenApi(op => …)` is
deprecated in .NET 10 (`ASPDEPR002`) *and* never integrated with `AddOpenApi()` in the
first place — its own XML remark says it is "primarily intended for consumption
along-side Swashbuckle". Probed: it compiled, ran, and changed nothing.

### P5 — Adopting this library costs the consumer a diagnostic they already had

`ASP0022` catches a duplicate verb+route at build time for direct `MapGet`/`MapMethods`
call sites with literal patterns. It cannot fire here, because the generated call site is
`routes.MapMethods(TEndpoint.Path, TEndpoint.Methods, …)` with no literal.

Measured runtime behaviour, so that any replacement diagnostic matches reality rather
than intuition:

| Case | Result |
|---|---|
| Same verb + same literal route | **500** ambiguous match |
| `/users/{id}` vs `/users/me` | **both 200** — a literal segment beats a parameter |
| `/api/{**rest}` vs `/api/things` | **both 200** — no conflict |
| `/a/{id}` vs `/a/{key}` (names differ) | **500** |
| `/Case` vs `/case` | **500** — routing is case-insensitive |
| group `/api/users` + `Path "/"` vs literal `/api/users` | **500** — trailing slash is not a distinction |
| `["GET","HEAD"]` vs `["HEAD","OPTIONS"]` | GET 200, OPTIONS 200, **HEAD 500** |
| `/items/{id:int}` vs `/items/{slug}` | no conflict — constraints disambiguate |

Two consequences. **Route shadowing is not a defect and must not be diagnosed** — a
diagnostic on `/users/{id}` vs `/users/me` would be a pure false positive. And the
partial-verb-overlap case in row 7 is one `ASP0022` misses entirely, which this library
is uniquely placed to catch because `Methods` is declarative.

### P6 — The generated file emits CS1591 into consumers' builds, and the header does not help

`EndpointMapping.g.cs` already carries an `<auto-generated>` header. That suppression
applies to **analyzer** diagnostics; CS1591 is a **compiler** diagnostic and is not
covered. Measured on `TestApp` with `GenerateDocumentationFile=true`:

```
EndpointMapping.g.cs(39,25):   warning CS1591: … 'TestAppEndpointsExtensions'
EndpointMapping.g.cs(65,82):   warning CS1591: … '…MapTestAppEndpoints(IEndpointRouteBuilder)'
EndpointMapping.g.cs(139,132): warning CS1591: … '…Endpoints'
```

This repo's own convention is `GenerateDocumentationFile=true` in Release for every
shipped package. A consumer following it with `TreatWarningsAsErrors` gets three errors
in a file they cannot edit. Adding one `#pragma` line took the build from 48 warnings to
45 with zero `EndpointMapping.g.cs` hits remaining.

Note this is **not** the 72×CS1591 baseline recorded in `PLAN-Net11Migration.md:333` —
that population originates in `MintPlayer.SourceGenerators` output in SitemapXml and
MustChangePassword, and this change will not move it.

### P7 — A nested endpoint class emits uncompilable code

Base-class emission opens a flat `namespace { … }` block
(`EndpointGenerator.Producer.cs:77-80`), so an endpoint declared inside another type —
`class Outer { partial class GetUser : IGetEndpoint<…> { } }` — produces output that does
not compile. Nothing in `FixtureSources.Corpus` declares a nested endpoint, which is why
988 tests do not catch it.

### P8 — The pack target can ship a nupkg containing no analyzer, and succeed

`PackEndpointsGenerator` in `MintPlayer.AspNetCore.Endpoints.csproj` has **no `<Error>`
guards**. An empty analyzer payload installs happily and does nothing; there is no error
for the consumer to search for. Both csprojs carry comments documenting that this exact
failure has already been hit and fixed once. The sibling repo's CLAUDE.md names it as the
number-one packaging failure mode and mandates `<Error>`, never `<Warning>`.

### P9 — `AddValidation()` silently does nothing, and the framework will not say so

`Microsoft.Extensions.Validation`'s source generator discovers validatable types from
**endpoint handler parameter lists at `Map*` call sites**. The generated handler takes
`(HttpContext ctx)`. So a consumer who puts `[Required]` on a request record and calls
`AddValidation()` gets no validation — and per Microsoft's own documentation, *"Missing
metadata doesn't produce a runtime exception or log entry."*

**P9.1 — and a source generator cannot fix it by changing the call-site shape.** Measured
with identical shapes:

```
hand-written in Program.cs:  MapPut("/opt3/users/{id}", ([AsParameters] UpdateUserRouteParams _, HttpContext ctx) => …)
  PUT /opt3/users/9999  ->  400 {"errors":{"Id":["The field Id must be between 1 and 1000."]}}

generator-emitted, same shape, concrete type, same AddValidation():
  GET /validated/0      ->  200
```

Roslyn generators do not observe each other's output. Anything this library emits is
invisible to the validation generator. This is a wall, not a trade-off, and it is the
single most important constraint in this document.

### P10 — `UpdateUser` reads its id from the body, not the route

`TestApp\Endpoints\UpdateUserEndpoint.cs` is mapped at `/{id}` and has no
`BindRequestAsync`, so `UpdateUserRequest.Id` is populated by the body deserializer and
the route token is never read. `PUT /api/users/1` with `{"id":999,…}` acts on 999. A live
defect in the shipped sample.

### P11 — The library is four annotations from AOT-clean and nobody has looked

No project sets `IsAotCompatible`, `EnableTrimAnalyzer`, `IsTrimmable` or `PublishAot`.
Full ILC analysis of `TestApp` under `PublishAot=true` produced **exactly two warnings**,
both from the same `MapMethods(…, Delegate)` call. The manual `MapEndpoint<T>` path adds
four more: `IL2060`+`IL3050` on `MakeGenericMethod`, `IL2091` on
`ActivatorUtilities.CreateFactory<T>`, `IL2070` on `Type.GetInterfaces()`. Two of those
four are one attribute each.

## Goals

1. Delete the hand-written binder from every body-less endpoint, and cut consumer lines
   for a typical endpoint by at least 40%.
2. Move group membership from an interface to an attribute, and delete the two
   diagnostics the compiler then enforces for free.
3. Give the generator the route, and use it to diagnose at build time what today fails
   silently at run time.
4. Make the emitted OpenAPI document valid, and make it describe what the README already
   promises.
5. Close every defect in the register below that is closable without a second release.
6. Keep incremental caching intact, and keep generated and manual registration in
   lockstep.

## Non-goals

- **A validation abstraction.** `Microsoft.Extensions.Validation` is the right substrate;
  competing with it would be a second thing to keep in sync with the framework.
- **Route-shadowing diagnostics.** Measured as correct behaviour (P5); diagnosing it
  would be a false positive.
- **Inferring the route from the request type.** Prototyped and rejected — see Appendix B.
- **Inferring the verb from the class name.** Saves zero lines: the verb already lives
  inside the interface name on a line the consumer writes anyway.
- **Switching to `MapMethods(…, RequestDelegate)` for AOT.** It is the correct fix and it
  is warning-free, but it returns `IEndpointConventionBuilder` rather than
  `RouteHandlerBuilder`, which would break `Configure(RouteHandlerBuilder)` — the
  library's most-used public hook — for a benefit nobody has asked for.
- **Backward compatibility.** Explicitly waived by the requester.

## Requirements

### R1 — Group membership

**R1.1 — Membership is declared by a generic attribute.** `[MemberOf<TGroup>]` with
`class MemberOfAttribute<TGroup> : Attribute where TGroup : IEndpointGroup`, applied to
endpoint classes and to group classes for nesting. `[MemberOf(UsersApi)]` as originally
sketched is not valid C# — `error CS0119: 'UsersApi' is a type, which is not valid in the
given context`. The generic form is preferred over `[MemberOf(typeof(UsersApi))]` because
the constraint is compiler-enforced: naming a non-group is **CS0311**.

**R1.2 — `IMemberOf<TGroup>` is deleted.** Not obsoleted. Two mechanisms would mean two
discovery paths, two diagnostic sets, and a permanent sync obligation on every future
group-level feature — and they do not behave identically under inheritance.

**R1.3 — The generator matches the attribute by arity-encoded metadata name.** The string
is `"MintPlayer.AspNetCore.Endpoints.MemberOfAttribute\`1"` — backtick, then arity.
`"…MemberOfAttribute"` matches only a non-generic form; `"…MemberOfAttribute<TGroup>"`
matches nothing. **Both wrong forms produce zero hits with no error and no warning**, so
R1.3 is satisfied only when a test asserts a known-good sample produces at least one
match.

**R1.4 — Membership inherits through base classes.** `Inherited = true`, and both
registration paths implement the same rule: **nearest declaration wins**, walking up from
the type. The generator uses `GetAllBaseTypes()` from `MintPlayer.SourceGenerators.Tools`;
the reflection path uses `GetCustomAttribute(inherit: true)`. `ISymbol.GetAttributes()`
never returns inherited attributes, so `Inherited = true` alone is inert for a generator —
the walk is mandatory, not optional.

**R1.5 — A derived endpoint may re-declare membership to override it.** `AllowMultiple =
false` gives CS0579 within one type; two declarations in a chain are legal and mean
override.

**R1.6 — MPEP003 and MPEP004 are removed.** Both become CS0579. MPEP005 (cyclic nesting)
survives — C# permits `[MemberOf<B>] class A` / `[MemberOf<A>] class B` and the generator
must still detect the cycle or recurse forever.

**R1.7 — `EndpointAttributes.ForMetadata` must exclude the new attribute.** `IsMeaningful`
excludes only compiler-generated attributes today, so `[MemberOf<T>]` would otherwise land
in `endpoint.Metadata` as a routing-visible object. `MapEndpointTests.cs:144,170,187`
assert on exactly this filtering.

### R2 — Request binding

**R2.1 — Route and query values bind to properties on the endpoint class.** `TRequest`
carries the body and nothing else.

> **This reverses an earlier specification, and the reason is worth recording.** The first
> draft put binding attributes on the request type, on the strength of a seven-surface
> comparison that request-type binding won 6–1. Two of those wins do not survive contact
> with a third design that the comparison did not include.
>
> **The body schema.** With route values flattened into `TRequest`,
> `.Accepts<TRequest>()` advertises `id` as a field of the request body. It is not one.
> Fixing that means a transformer performing surgery on the body schema to strip
> route-bound members — fragile, and exactly the class of thing that rots silently. The
> whole of P4 is that this library emits a wrong OpenAPI document; a design that fixes the
> missing-parameters half by introducing a wrong-body half is not a fix.
>
> **The name collision.** A flat type where `Id` may arrive from the route *or* the body
> is not undefined — measured, route wins, deterministically. But it is *implicit*: the
> declaration says nothing, and a reader must know the precedence rule. Separating the two
> makes the collision inexpressible rather than resolved.
>
> **The OpenAPI loss was measured against the wrong baseline.** Design A was scored before
> the shadow-parameter technique existed (R4). The shadow decouples where the consumer
> *declares* a value from what the framework *sees*, and nothing about it requires the
> values to live on `TRequest` — the generator knows the endpoint's bound properties just
> as well. A's OpenAPI deficit is an artifact of the measurement order.
>
> **The validation loss shrinks to near nothing.** The rule that request-type binding
> uniquely enables is "the `{id}` in the URL must match `request.Id`". That rule exists
> only because the id is in both places. Here it never is.
>
> What A genuinely still costs is recorded honestly in R2.12, R2.13 and Appendix B: two
> homes for request data, a mutable property on the endpoint, and binding that is not
> testable without a server.

**R2.2 — `TRequest` means the body, on verbs that have one.** A body-less verb has no
body, so it has no `TRequest`. This is not a gap to be filled with a placeholder — see
R2.14.

**R2.3 — Binding is convention-first.** A property binds from the route value of the same
name, else the query string of the same name. Matching is case-insensitive, which is free
because `RouteValueDictionary` and `IQueryCollection` are both ordinal-ignore-case. An
attribute is written only to override the source or the name. Measured: convention ties
attribute-per-member on consumer lines and beats it by one concept.

**R2.4 — The library ships its own attributes, implementing the framework's interfaces.**
`RouteParamAttribute : Attribute, IFromRouteMetadata` and `QueryParamAttribute :
IFromQueryMetadata` in `MintPlayer.AspNetCore.Endpoints`. Binding and ApiExplorer match on
the **interface**, not the concrete type, so these receive identical framework treatment
to `Microsoft.AspNetCore.Mvc`'s — proven. Because the match is interface-based, a consumer
who writes `[FromRoute]` with the MVC import gets identical behaviour for free, at no cost
to us.

> **Why not simply reuse MVC's attributes.** They work verbatim, on properties and on
> positional-record parameters. But `Microsoft.AspNetCore.Mvc` is not an implicit global
> using, and **a generator cannot supply the import**: emitting `global using
> global::Microsoft.AspNetCore.Mvc;` made every typed endpoint fail CS0534, because the
> generator reads the pre-generation compilation where the unimported attribute is an
> error type, finds no binding members, and emits no binder. The import would have to come
> from MSBuild `build/*.props` or from a hand-written line in every request-type file.
> Own attributes in the namespace the consumer already imports cost nothing and also
> remove any CS0104 risk in a file that imports MVC.

**R2.5 — Conversion is classified at generate time.** A single overload set is impossible:
generic constraints are not part of a method signature, so `TryConvert<T>(…) where T :
IParsable<T>` and `where T : struct, Enum` are CS0111 duplicates. The generator classifies
the member's `ITypeSymbol` in this order and picks the helper name: `string` → enum →
nullable enum → `IParsable<T>` → nullable `IParsable<T>` → **diagnostic**. There is no
fallback: `Convert.ChangeType`/`TypeDescriptor` are reflective, culture-fragile and
AOT-hostile.

**R2.6 — Enum binding uses `Enum.IsDefined`.** Without it `Enum.TryParse("7")` succeeds
and yields an undefined enum value, and `/items/kind/7` returns `200 {"kind":7}`. With it,
400. Parsing is case-insensitive; the framework's own enum binding is case-sensitive and
this is a deliberate divergence.

**R2.7 — Parsing is `InvariantCulture`.** Proven: `TryParsable<DateOnly>("2026-09-24")`
succeeds and `("24/09/2026")` fails, which is the intended contract.

**R2.8 — The route value is read defensively.** `RouteValues` is typed `object?`, and
while every one of 21 observed values was `System.String`, a custom `IRouteConstraint` or
parameter transformer could in principle place a non-string. Read via `TryGetValue` then
`as string`, never a hard cast. Three distinct no-value shapes must collapse to one "not
supplied" path: `{id?}` unmatched is **key absent**; `{*rest}` unmatched is **key present,
value null**; `{id=5}` unmatched is **key present with the default string**.

**R2.9 — The body path is not touched.** `BodyEndpoint<TRequest>` keeps binding the body
through MVC's input formatters with the JSON fallback, exactly as today. This is a
material gain over the rejected flat design, which had to route the body through a
`JsonObject` overlay in order to merge route values into it — and therefore **lost
content negotiation on every endpoint that had a route parameter**. Here no merge is ever
needed, so XML and any other registered input formatter keep working everywhere.

> Worth recording as a defect avoided rather than a feature gained: the overlay was
> mandatory under the flat design, because the naive "deserialize the body, then patch the
> route value over it" **does not work**. The body does not carry `id`, so
> `System.Text.Json` throws on the missing `required` member before any patching runs,
> producing a 400 the caller can never fix by sending a correct request. That whole class
> of problem does not exist once route values stop entering the body type.

**R2.10 — No route constraints are emitted.** `{id:int}` with a non-int is **404**, because
a constraint failure means no route match and the endpoint is never reached. Microsoft's
routing documentation is explicit: *"Don't use constraints for input validation … Invalid
input should produce a 400 Bad Request with an appropriate error message."* Constraints
also change route *matching*, so inferring one from a CLR type would be an invisible
behaviour change.

**R2.11 — Binding failures keep the library's message quality.** A malformed value answers
400 with a message naming the parameter, the offending value and the expected type, routed
through `OnBindFailedAsync` so the existing customisation point still applies. Parameter
binding runs **before** body binding, so `PUT /users/abc` with a valid body is rejected on
the route value without the body ever being read — proven.

**R2.12 — Endpoint instances must remain per-request, and this is now load-bearing.**
Bound properties make the endpoint stateful. That is safe today because every request gets
a fresh instance from a cached `ActivatorUtilities.CreateFactory<T>(Type.EmptyTypes)`
invoked against `ctx.RequestServices` and disposed in a `finally` — never pooled, never
container-owned, with tests already pinning it.

This requirement exists so that the constraint is written down rather than folklore.
**Pooling or hoisting an endpoint instance would become a silent cross-request data
leak** — the worst class of bug in a web framework, and invisible under single-threaded
testing. Two specific guards:

- The generated code must construct the instance **inside** the request delegate. `MapGet`
  captures its lambda once for the life of the application; `var ep = new MyEndpoint(); app.MapGet(p, () => ep.Handle())`
  is a process-wide singleton with mutable request state. A generator test must assert
  that instantiation appears inside the delegate.
- Ship the analyzer, not the documentation paragraph. Carter's `CARTER2` is the precedent,
  including the detail that detection has to walk base types.

**R2.13 — Every endpoint level is served, including raw.** Because binding attaches to the
endpoint rather than to `TRequest`, a level-1 `IEndpoint` can carry bound properties too —
which is an advantage of this design, not an afterthought. It requires a generated partial
where the current gates (`Level != Raw && IsPartial && !HasExistingBaseClass`) emit
nothing at all. A raw endpoint has no base class to override, so binding is reached
through a new `IParameterBinder` interface implemented **explicitly** so it stays off the
consumer's public surface; `EndpointBase<TRequest>` implements it and forwards to
`OnBindFailedAsync`, so both rungs share one customisation point.

**R2.14 — The interface ladder loses `TRequest` on body-less verbs rather than gaining a
placeholder.** A GET with a typed response and no body must not be written
`IGetEndpoint<NoRequest, UserResponse>`. A prototype that did this had five of eight
scenarios declaring a placeholder request they never touched — a word that appears in
every signature and in the OpenAPI-facing generic argument list and means nothing.

Specified ladder:

| Verb class | Interfaces | Handler |
|---|---|---|
| GET, DELETE | `IGetEndpoint` | `HandleAsync(HttpContext)` |
| | `IGetEndpoint<TResponse>` | `HandleAsync(CancellationToken)` |
| POST, PUT, PATCH | `IPostEndpoint` | `HandleAsync(HttpContext)` |
| | `IPostEndpoint<TBody>` | `HandleAsync(TBody, CancellationToken)` |
| | `IPostEndpoint<TBody, TResponse>` | `HandleAsync(TBody, CancellationToken)` |

The single type argument therefore means the **response** on a body-less verb and the
**body** on a body verb. That asymmetry is a real cost in learnability and must be stated
plainly in the README — but it mirrors HTTP, where a GET has no body to name, and it is
cheaper than a placeholder type that lies in every signature. It is a breaking
reinterpretation of today's `IGetEndpoint<TRequest>`, which the no-backward-compatibility
waiver permits.

### R3 — Route capture and diagnostics

**R3.1 — `EndpointInfo` carries the route and `GroupInfo` the prefix.** Recovered by
`SemanticModel.GetConstantValue` on the property's expression body, initializer, getter
arrow, or single-`return` getter. Proven recoverable for: string literal, `const`, const
concatenation, const interpolation, raw string, block getter, auto-property initializer,
explicit interface implementation, declaration in a different partial file, and
declaration on a base class. Proven **not** recoverable for: runtime interpolation,
`static readonly`, method invocation, multi-statement getter, and any endpoint in a
**referenced assembly** (`DeclaringSyntaxReferences.Length == 0`).

**R3.2 — Route-derived diagnostics are opportunistic, never mandatory.** Hard-erroring
when `Path` cannot be read would break every cross-assembly endpoint. Unreadable means
silent (or at most MPEP011 at Info), never an error.

**R3.3 — New diagnostics, MPEP007 onwards.** MPEP001–MPEP006 are in use; MPEP003 and
MPEP004 are freed by R1.6 but are **not** reused, so shipped ids never change meaning.

| Id | Severity | Condition |
|---|---|---|
| MPEP007 | Warning | Two endpoints answer the same verb+route (incl. partial verb overlap) |
| MPEP008 | Error | A route parameter has no bindable member on the request type |
| MPEP009 | Error | A member is marked route-bound but its name is not a parameter of the route |
| MPEP010 | Warning | `Path` already begins with its own group's composed prefix |
| MPEP011 | Info | `Path` is not a compile-time constant; route-derived checks skipped |
| MPEP012 | Error | Two endpoints resolve to the same endpoint name |
| MPEP013 | Error | A bound member's type is neither `string`, an enum, nor `IParsable<T>` |
| MPEP014 | Error | A type carries bound members but is not `partial` |
| MPEP015 | Warning | A request type carries DataAnnotations but is not `[ValidatableType]` |
| MPEP016 | Info | A declared group is never joined by any endpoint |
| MPEP017 | Error | Both a generated and a hand-written binder exist |

**R3.4 — Every diagnostic that aborts emission must emit a throwing stub.** Measured: a
generator that bails on a bad input leaves the abstract member unimplemented, so the
consumer reads `CS0534 … does not implement inherited abstract member BindRequestAsync`
*after* the real diagnostic and acts on the wrong one. MPEP007-017 must not produce a
cascade.

**R3.5 — Route templates are parsed once per pipeline run and cached.** The framework's
own route analyzer has a documented 1.5-minute execution-time defect
(dotnet/aspnetcore#53899) from re-parsing; do not repeat it.

**R3.6 — MPEP007's duplicate detection ships as a Warning for one release.** Getting
matcher precedence wrong turns it into a false-positive error that blocks builds. A wrong
diagnostic here is worse than none.

### R4 — OpenAPI

**R4.1 — Every templated route declares its path parameters.** Required for the document
to be valid at all (P4).

**R4.2 — Body verbs declare a request body.** `.Accepts<TRequest>("application/json")`.
The generator already holds `RequestTypeFqn`.

**R4.3 — Typed endpoints declare their documented failure responses.**
`.ProducesProblem(400)` and `.ProducesProblem(415)`, which are exactly the responses the
README's binding-failure table already guarantees.

**R4.4 — Enrichment uses `AddOpenApiOperationTransformer`, not `.WithOpenApi`.** The
latter is deprecated and, separately, has never worked with `AddOpenApi()` (P4.2).

**R4.5 — `SuccessStatusCode` reaches level-2 endpoints.** `IDeleteEndpoint<TRequest>` has
no `SuccessStatusCode` member, so the fix for P4.1 is generator-side: emit the declared
status for level-3 and a sane default for level-2.

**R4.6 — Transformer code is per-TFM.** `Microsoft.OpenApi` broke 1.x→2.x in .NET 10 and
breaks 2.x→3.x in .NET 11 (default spec version 3.0 → 3.1 → 3.2). The packages target
`net10.0;net11.0`, so anything touching `OpenApiSchema`/`OpenApiParameter` needs
conditional code. This is the real cost of R4.

### Requires a decision — how the framework learns about route parameters **[decision]**

R4.1 needs the framework to see a bound parameter. The generator can emit a nullable
"shadow" record as an `[AsParameters]` lambda parameter purely for documentation, while
the library still performs the real binding.

The shadow is built from the **endpoint's** bound properties (R2.1) and from the route
template (R3.1). Nothing about it requires the values to live on `TRequest` — which is
precisely why the OpenAPI argument for request-type binding did not survive: the framework
sees whatever parameter the generator declares, not whatever the consumer declares.

The shadow's member types decide the outcome, and the two obvious choices are exclusive:

| Option | Consequence |
|---|---|
| **Typed shadow** (`int?`, `Guid?`, `ItemKind?`) | OpenAPI gets `id:path:integer format=int32` and `$ref` for enums. But the framework parses first, so `/users/abc` returns **400 with an empty body** and the library's message never runs. Enum binding becomes case-sensitive and loses `Enum.IsDefined`. |
| **Untyped shadow** (`string?`) | The framework never rejects, so the library's binder produces `"The route parameter 'Id' must be a valid Int32; 'abc' is not."` and keeps case-insensitive enums. But OpenAPI documents every path parameter as `type: string`. |
| **Untyped shadow + schema transformer** | Untested. The transformer required by R4.1 already runs; if it can also set the schema's type and format from the CLR type the generator knows, both properties hold at once. |

Nullability is load-bearing either way: a non-nullable shadow member makes the framework
return 400 on a *missing* value before the endpoint runs. `Name` must be left unset unless
the consumer supplied one — with `Name = "Id"` against template `{id}`, OpenAPI documents
`Id:path` and Swagger UI fails to match it.

**Specified: untyped shadow plus schema transformer, gated on spike S1.** If S1 fails, fall
back to the untyped shadow and accept `type: string`, because the library's error messages
are the thing the library is *for* — the existing `EndpointBindingException` design and
its README documentation are an explicit prior commitment to message quality, and a
bodyless 400 is a regression against the status quo. A degraded schema is not.

### R5 — Validation

**R5.1 — The library does not discover validatable types; the consumer marks them.**
Automatic discovery is impossible (P9.1). The consumer writes `[ValidatableType]` on the
body type — hand-written syntax, therefore visible to the validation generator.

**R5.1a — Validation covers the body; it does not cover route and query values.** This is
a direct consequence of R2.1 and must be documented rather than discovered. `[Range]` on a
`[RouteParam]` property of the endpoint is **not** evaluated by
`Microsoft.Extensions.Validation`, because the endpoint class is not a validatable type the
framework knows about. Route and query values are guarded in two other places: the binder
enforces the **type** (a non-integer `{id}` is a 400 before the handler runs), and business
rules belong in `HandleAsync`, which is where a 404-versus-400 decision has to be made
anyway. The README must say this in one sentence; a consumer who decorates a route
property with `[Range]` and sees nothing happen is exactly the silent-no-op failure this
requirement group exists to prevent.

> If route-value validation later proves to be wanted, the honest options are a library-side
> DataAnnotations pass over the endpoint's bound properties, or `[ValidatableType]` on the
> endpoint class itself. Neither is specified here. Do not invent one mid-implementation.

**R5.2 — The library invokes validation explicitly.** After a successful bind,
`EndpointBase<TRequest>` resolves `IOptions<ValidationOptions>`, calls
`TryGetValidatableTypeInfo(typeof(TRequest), out var info)`, runs it, and maps failures to
a 400, behind a null check so nothing breaks when `AddValidation()` was never called.

**R5.3 — MPEP015 converts the framework's silent no-op into a visible one.** A request
type carrying DataAnnotations but not `[ValidatableType]` is exactly the shape that
silently returns 200 on invalid input.

**R5.4 — The generator must not emit `[ValidatableType]`.** .NET 11 ships **ASP0037
"ValidatableType cannot be used in generated code"**, and generator-ordering makes it
unreliable regardless.

### R6 — Packaging, AOT and hygiene

**R6.1 — `#pragma warning disable CS1591` in the generated file**, or real `///` docs on
the three public members. The latter is preferred: `MapXxxEndpoints()` and `Endpoints`
have no IntelliSense today.

**R6.2 — `<Error>` guards on the pack target** (P8), and the pack must be verified by
unpacking a real nupkg in **both** Debug and Release and diffing the `analyzers/**`
entries, not by reading the build log.

**R6.3 — Nested endpoint classes emit correctly** (P7), via `OpenPathSpec` from
`MintPlayer.SourceGenerators.Tools`, with a nested endpoint added to the fixture corpus.

**R6.4 — `[DynamicallyAccessedMembers]` on the two reflective sites**, plus
`<IsAotCompatible>true</IsAotCompatible>` on the two shipping projects as a regression
gate (P11).

**R6.5 — `MintPlayer.SourceGenerators.Tools` is upgraded 10.16.0 → 10.21.0**, moving the
generator's Roslyn floor from 4.14.0 to 5.x. Safe given the packages target
`net10.0;net11.0` and `global.json` pins `11.0.100-rc.1.26425.128`, but it is a breaking
change for consumers on older SDKs and must be re-verified per R6.2.

**R6.6 — New attributes ship in the existing packages.** No `*.Attributes` split. The
toolkit's own guidance carves out packages that ship runtime code *and* a generator as the
case where they stay together; a fourth project adds a packaging surface where every
mistake fails silently, for no benefit.

**R6.7 — Equatable models stay equatable.** `EndpointInfo` is a hand-written
`IEquatable<T>` with no collections today. Adding `ImmutableArray<BoundMemberInfo>`
without extending `Equals` silently kills incremental caching, because `ImmutableArray`
equality is by reference. `EndpointGeneratorIncrementalTests` will catch it if the tests
are kept honest.

**R6.8 — Member discovery reads symbols, not the triggering syntax node.** A partial split
across files, or an attribute on two partial parts, makes `ForAttributeWithMetadataName`
fire twice for the same symbol with `ctx.Attributes` scoped to each node. De-duplicate by
`SymbolEqualityComparer` and read the authoritative set from `ctx.TargetSymbol`.

**R6.9 — Generated code resolves every name without relying on a `using`.** Types are
already emitted `global::`-qualified. **Extension-method calls are not**, and they are the
remaining hole: `routes.MapMethods(…)` and `routes.MapGroup(…)` resolve only because the
generated file emits `using Microsoft.AspNetCore.Builder;`. Extension-method lookup needs
the namespace imported, so that `using` is load-bearing in a way a qualified type
reference is not. Emit the static form instead —
`global::Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(routes, …)`
— and delete the `using` block from the generated file entirely.

> The rule is worth stating as a rule rather than a fix, because the failure it prevents
> is invisible in this repo's own tests. `GeneratedCodeUsingsTests` covers "with and
> without Web SDK implicit usings", but not a consumer who defines a conflicting
> `MapMethods` extension, sets `<ImplicitUsings>disable</ImplicitUsings>`, or has a
> namespace that shadows `Microsoft.AspNetCore.Builder`. Unqualified generated code puts
> the consumer's import set on the library's critical path for no benefit.
>
> **What this rule does *not* rescue** is the consumer's own source. R2.3's finding stands:
> a consumer writing `[FromRoute]` needs the MVC namespace imported *in their file*, and a
> generator cannot supply it — emitting `global using global::Microsoft.AspNetCore.Mvc;`
> made every typed endpoint fail CS0534, because the generator reads the pre-generation
> compilation, where the unimported attribute is an error type and no binding members are
> found. Fully-qualified emission governs what the generator writes; it cannot govern what
> the consumer writes. That is why R2.3 ships the attributes in the namespace the consumer
> already imports.

### R7 — Typed links, contract and client

**R7.1 — A generated `Routes` façade replaces magic-string URLs.** Nested static classes
mirroring the group tree, one method per endpoint typed from the request members.

**R7.2 — URL construction delegates to `LinkGenerator`.** Do not hand-roll. A 70-line
substitution helper matched `LinkGenerator` byte-for-byte on 12 cases and then diverged on
**7 of 16** once the character set widened: routing uses `UrlEncoder.Default`, whose
allow-list includes `! $ ( ) * , ; @`, while `Uri.EscapeDataString` escapes all of them.
Also measured: route encoding *does* escape `/`, and `LinkGenerator` returns **null
silently** for a missing required value, a constraint violation, or an unknown name — so a
generated method returning non-null is strictly better than what it wraps.

**R7.3 — Endpoint names are unique, and the generator proves it.** `.WithName()`
duplicates throw `InvalidOperationException: Duplicate endpoint name … must be globally
unique` on the **first request**, not at startup, contradicting the ASP.NET Core
documentation. MPEP012 exists because only the generator sees every endpoint at once.

**R7.4 — Cross-assembly contract travels as an assembly attribute.** `[assembly:
EndpointContract(typeof(GetUser), "/api/users/{id}", …)]` emitted into the server's own
generated file and read downstream via `IAssemblySymbol.GetAttributes()`. Proven to
survive the assembly boundary where the `Path` literal does not. Razor and RestEase both
use this pattern. **Do not build a JSON manifest**: Microsoft deprecated
`Microsoft.Extensions.ApiDescription.Client`, `<OpenApiReference>` and `dotnet openapi` in
.NET 10 Preview 7 and deleted the source directory.

**R7.5 — The contract snapshot is a committed OpenAPI document plus `oasdiff`, not a
reimplementation of `PublicApiAnalyzers`.** `OpenApiVersion` must be pinned explicitly —
the default moved 3.0 → 3.1 → 3.2 across three releases and an unpinned snapshot churns
catastrophically on SDK upgrade. Note build-time generation **runs `Program.cs`** against a
mock server.

**R7.6 — The AOT claim must not be made.** `[AsParameters]` was expected to buy RDG-generated,
AOT-safe binding. Measured: with `EnableRequestDelegateGenerator=true`, RDG emitted
**nothing** for ~60 generator-emitted `MapMethods` calls, then immediately intercepted a
single hand-written `MapGet` added to `Program.cs`. Source generators cannot see each
other's output. The library gets the framework's binding semantics and OpenAPI metadata,
not AOT binding — and neither does Immediate.Apis, by the same mechanism.

### R8 — Documentation

**R8.1 — The 646-line package README is rewritten**, not patched. `IMemberOf<T>` appears
in ~20 places, `BindRequestAsync` in five code blocks, and the diagnostics table changes.

**R8.2 — The already-working attribute passthrough is documented.** `[Authorize]`,
`[Authorize(Policy=…)]`, `[EnableRateLimiting]`, `[Tags]` and `[ProducesResponseType]`
already flow into endpoint metadata via `EndpointAttributes.ForMetadata` and are honoured
— verified end to end (401, 401, 200-then-503, and OpenAPI). The README only shows the
imperative `Configure` hook. This is a working, entirely undocumented feature.

**R8.3 — The binding rule is stated once, in one sentence,** and the MPEP008/MPEP009
messages teach it at the moment it matters.

## Acceptance criteria

1. A GET endpoint with one route parameter is **8 consumer lines or fewer**, against 17
   for the same endpoint in today's README, and declares **no** `BindRequestAsync`.
2. Consumer lines across the seven-scenario corpus fall by **at least 40%**.
3. `IMemberOf<TGroup>` does not exist in the shipped assemblies; `[MemberOf<UsersApi>]`
   resolves the same routes, and a generator test asserts the metadata-name string
   produces at least one match.
4. A route template naming `{userId}` against a member named `Id` is a **build error**,
   both directions (MPEP008 and MPEP009).
5. Two endpoints answering the same verb+route produce MPEP007, including the partial
   verb-overlap shape; `/users/{id}` vs `/users/me` produces **nothing**.
6. `/openapi/v1.json` for `TestApp` declares a path parameter for every `{token}`, a
   request body for every body verb, and 400/415 for every typed endpoint; `DELETE
   /api/users/{id}` documents 204.
7. `dotnet build` of a consumer with `GenerateDocumentationFile=true` produces **zero**
   CS1591 from `EndpointMapping.g.cs`.
8. A nested endpoint class (`class Outer { partial class Inner : IGetEndpoint<…> }`)
   compiles, and is in the fixture corpus.
9. `dotnet pack` in **both** Debug and Release produces byte-identical `analyzers/**`
   entries, verified by unpacking; removing the generator output makes the pack **fail**.
10. `PUT /api/users/1` with body `{"id":999}` acts on **1** — closed by construction,
    because `UpdateUserBody` has no `Id` to disagree with the route (P10).
11. A **body** type with `[ValidatableType]` and `[Range(1,…)]` answers 400 on
    out-of-range input through the library's own invocation; a `[Range]` on a
    `[RouteParam]` endpoint property is documented as not evaluated (R5.1a).
12. A body endpoint that also has a route parameter still content-negotiates: `PUT` with
    `application/xml` reaches a registered XML input formatter (R2.9).
13. `PUT /api/users/abc` with a valid body is a 400 on the route value, and the body is
    never read.
14. No shipped signature contains a placeholder request type; a GET with a typed response
    and no body is written `IGetEndpoint<TResponse>` (R2.14).
15. A generator test asserts the endpoint instance is constructed **inside** the request
    delegate, not captured in its closure (R2.12).
16. The full suite passes on both `net10.0` and `net11.0` with an identical test count per
    TFM, and incrementality tests still show a cache hit on an unchanged compilation.

## Risks

| Risk | Mitigation |
|---|---|
| The metadata-name string fails silently and the feature ships dead | R1.3's positive-match test; spike S3 pins it against the real Tools package |
| The shadow-parameter trade (typed schema vs good messages) has no third option | Spike S1 decides before M4; the decision block names the fallback and why |
| Validation cannot be invoked the way R5.2 assumes | Spike S2 gates it; if it fails, R5 reduces to MPEP015 alone and the README documents "no automatic validation" |
| Adding a collection to `EndpointInfo` kills incremental caching | R6.7; `EndpointGeneratorIncrementalTests` already asserts cache hits |
| The Tools 10.21.0 upgrade changes which SDKs can load the analyzer | R6.2 verification; spike S4 |
| The code-fix assembly breaks the pack, silently | Spike S5, and R6.2's unpack diff covers it |
| MPEP007 false-positives block builds | R3.6 ships it as a Warning for one release |
| `Microsoft.OpenApi` 3.x on net11.0 breaks transformer code | R4.6 per-TFM code; both TFMs in the acceptance sweep |
| The PR is large enough that a late failure is expensive | M0 gates; Tier-3 diagnostics are additive and are the designated cut |

## Appendix A — measurement method

Every figure in this document came from a run, not an estimate.

- **Consumer line counts** are non-blank, non-comment lines, excluding `using` and
  file-scoped `namespace`, counting braces, over a fixed seven-scenario corpus: one route
  param; two route params with a literal between; route param + JSON body; Guid; enum;
  optional route param + query param; raw endpoint. Five designs were built as running web
  applications and measured with the same script.
- **HTTP behaviour** was observed against real `WebApplication` instances started with
  `app.StartAsync()` and driven by `HttpClient` — 21 probes for design A, 23 endpoints for
  design B, 82 checks across nine consumer families for the design-it-twice prototype.
- **OpenAPI claims** come from dumping `/openapi/v1.json` from a running app, including
  from a scratchpad copy of the shipped library with four lines added to `Program.cs`.
- **Roslyn claims** come from probe generators compiled against
  `Microsoft.CodeAnalysis.CSharp` **4.14.0** — the version `MintPlayer.SourceGenerators.Tools`
  10.16.0 declares, i.e. the exact Roslyn the shipped generator compiles against.
- **Compiler-error claims** come from projects built to fail, with the verbatim diagnostic
  captured.
- **AOT claims** come from `dotnet publish -p:PublishAot=true -r win-x64`; the ILC analysis
  phase completed, the native link step failed on an unrelated `vswhere.exe` PATH problem.
- **Warning counts** were taken with `-t:Rebuild` and deduplicated, per
  [feedback_verify_warning_counts_with_rebuild].

## Appendix B — designs prototyped and rejected

Five consumer-facing designs were built and measured. The status quo costs **97** consumer
lines over the corpus; every viable candidate costs **42–48**. The whole prize is the
−51%, and it comes from deleting the hand-written binder. **The choice between candidates
is worth at most 6 lines of 48.**

- **Route and query on endpoint properties, body in `TRequest`** — 51 lines measured, all
  eight scenarios passing with correct 400s. **Specified in R2.** Chosen because it is the
  only design in which the request type is honestly named: `TRequest` is the body, the
  OpenAPI body schema is exactly the body, and a route/body name collision is not
  expressible rather than resolved by a precedence rule nothing in the declaration
  mentions. Its costs are real and are recorded as requirements rather than hidden:
  request data has two homes (R2.1), the endpoint carries a mutable property and therefore
  must never be pooled (R2.12), binding is not testable without a server, and the interface
  ladder becomes asymmetric to avoid a placeholder request type (R2.14).
- **Attributes on request-type members, flattened** — 48 lines, three fewer. Rejected
  after the flat shape's consequence for the body schema was understood: `.Accepts<TRequest>()`
  advertises route values as body fields, and un-flattening them means schema surgery.
  Its six-surface scorecard against endpoint properties does not survive scrutiny — two of
  those wins (OpenAPI, cross-field validation) were artifacts of measuring before the
  shadow-parameter technique existed and of a rule that only exists when the id is in two
  places. See the note under R2.1.
- **Attributes on request-type members, with the body in a nested envelope** — 52 lines
  measured, and it answers the collision objection as completely as the chosen design. It
  keeps everything on one root object, so a cross-field rule spanning route and body stays
  writable. Not chosen: it costs a `request.Body.Name` hop on every body endpoint and an
  extra declared type, to preserve a validation capability that the chosen design makes
  unnecessary rather than impossible.
- **Convention with attributes as override** — 48 lines, tied, and one concept cheaper.
  Folded into R2.2 rather than treated as a separate design.
- **No request type; handler parameters are the request** — 42 lines, the only design that
  beats the rest, by 6 lines. Rejected: the handler stops being an interface member because
  its signature differs per endpoint, so a typo is a generator diagnostic instead of CS0535,
  there is nothing to hang `[Required]` on, and nothing named for `.Accepts<T>()`. Six
  lines over seven endpoints is not worth the compiler-enforced contract.
- **Route inferred from the request type's members** — 24 lines but only 4 of 7 scenarios
  expressible. Rejected decisively: it cannot place a literal segment *between* two
  parameters, it cannot distinguish a body field from a route field, and it **invented**
  `/search/{page}/{sort}` so that `?sort=name` 404s. The route-vs-member diagnostic that
  protects every other design is vacuous here, because the route is derived *from* the
  members and can never disagree with them. It also makes an IDE rename a change to a
  public HTTP contract, and `grep "/users/{id}"` finds nothing.

One free win from that exercise is adopted: making `IEndpointBase.Path` `static virtual …
=> "/"` instead of `static abstract` deletes a line from every collection endpoint with no
inference at all.

## Appendix C — unverified at time of writing

These are the claims this document rests on that have **not** been executed, each with the
spike that settles it.

- Whether an OpenAPI schema transformer can restore typed schemas over a string-typed
  shadow parameter (the R4 decision) — **S1**.
- Whether `[ValidatableType]` plus a library-side `TryGetValidatableTypeInfo` call actually
  validates, given the generator only covers the assembly calling `AddValidation()` — **S2**.
- Whether `[StringSyntax("Route")]` on a `static abstract string Path` **property**
  activates `RoutePatternUsageDetector`. Documented and demonstrated only on parameters. If
  it does not light up, the library's own template parser must also diagnose malformed
  templates.
- Whether .NET 10's built-in `XmlCommentGenerator` reaches `HandleAsync`'s `///` summaries.
  Two agents reached opposite framings; the registration passes a lambda, which is the
  shape the feature is documented not to cover.
- Whether a nullable *reference* type implementing `IParsable<T>` can be bound as optional.
  The optional helper needs `where T : struct`, so the classification currently downgrades
  such a member to required.
- Whether a consumer configuring JSON only through `AddControllers()` diverges from the
  overlay's `IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>`.
- Whether `[RouteParam(Name = …)]` and `[JsonPropertyName(…)]` disagreeing on one member
  behaves sensibly in the overlay.

## Version

Created 2026-09-24. Branch `feature/endpoints-ergonomics`.
