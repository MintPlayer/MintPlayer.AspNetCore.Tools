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
- **Binding URL fragments ("hash segments").** The part after `#` is never sent in an HTTP
  request (RFC 3986 §3.5; browsers and `HttpClient` strip it), so no server can read it: it
  is not in `HttpRequest.Path`, `QueryString` or any header. A value the server needs belongs in
  the route (`[RouteParam]`) or the query (`[QueryParam]`). A client-side SPA router that keeps
  state in the fragment has to copy it into one of those itself.
  *(Added 2026-09-24 against the session goal, which listed hash segments with route and query
  parameters.)*

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

> **Corrected while implementing M3: the reflection path must not use
> `GetCustomAttribute(inherit: true)`.** The runtime hides an inherited
> `AllowMultiple = false` attribute only when the derived type carries the *same attribute
> type* — and `MemberOfAttribute<UsersApi>` and `MemberOfAttribute<OtherApi>` are different
> closed types. So for exactly the override case R1.5 permits, `inherit: true` returns
> **both** attributes, and the reflection path would disagree with the generator. The
> implementation walks `Type.BaseType` with `inherit: false` and takes the first hit — the
> generator's rule, stated the same way. The same fact means `EndpointAttributes.ForMetadata`,
> which does use `inherit: true`, would have leaked two membership attributes into an
> overriding endpoint's metadata; R1.7's exclusion covers it.
>
> **And membership inheritance exposed a discovery bug that predates this PRD.** The
> generator's syntactic pre-filter matched base-list names beginning with an endpoint
> interface, plus `IMemberOf`. An endpoint inheriting its *verb* from a base class of its own
> — `partial class GetUser : UsersEndpointBase<…>` — names neither, so it never reached the
> semantic check and was silently never mapped. It only ever worked when an `IMemberOf<T>`
> happened to sit in the same base list. With that accident removed the shape would have
> broken outright, so the pre-filter now accepts any non-abstract class with a base list and
> the semantic check decides. The cost is one `GetDeclaredSymbol` per such class.

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

> **Reversed while implementing M4: binding is explicit, not convention-first.** Only
> properties carrying `[RouteParam]` or `[QueryParam]` are bound. Convention was measured on
> the *request type*, where it is harmless; R2.1 then moved binding onto the *endpoint*, and
> there it is not.
>
> - **Query convention would be a mass-assignment hole.** Every settable property on an
>   endpoint — including state set from DI or used internally — could be overwritten by a
>   client appending `?thatProperty=…` to the URL. That is the classic over-posting defect,
>   and the library would be shipping it as the default.
> - **Route convention would bind or not depending on how `Path` is spelled.** Matching `Id`
>   to `{id}` needs the route literal, which is unrecoverable for a computed `Path` and for
>   every endpoint in a referenced assembly (R3.1). The same declaration would silently bind
>   in one project and silently not in another.
>
> The price is one attribute per bound value. protoC measured an attribute at **zero** extra
> lines and one concept; that is a better trade than either hazard above.

**R2.4 — The library ships its own attributes, implementing the framework's interfaces.**
`RouteParamAttribute : Attribute, IFromRouteMetadata` and `QueryParamAttribute :
IFromQueryMetadata` in `MintPlayer.AspNetCore.Endpoints`. Binding and ApiExplorer match on
the **interface**, not the concrete type, so these receive identical framework treatment
to `Microsoft.AspNetCore.Mvc`'s — proven. Because the match is interface-based, a consumer
who writes `[FromRoute]` with the MVC import gets identical behaviour for free, at no cost
to us.

> **Corrected while implementing M4: the attributes do not implement the framework interfaces,
> and MVC's `[FromRoute]` is not accepted.** Under R2.1 the endpoint's properties are never
> framework-bound — the shadow parameter (R4) is what the framework sees, and the generator
> writes that itself. `IFromRouteMetadata` on `RouteParamAttribute` would therefore be purely
> decorative, suggesting a framework integration that does not happen, so it is left off. The
> generator matches the library's attributes by name and namespace. Accepting MVC's
> `[FromRoute]` as an alias was considered and rejected: two spellings for one thing is the
> problem R1.2 removed from group membership, and `[FromRoute]` would read as though ASP.NET
> Core were doing the binding.

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

> **Implementation note (M4): a raw endpoint's parameter-binding failure is not
> customisable.** Typed levels route parameter failures through `OnBindFailedAsync`, the same
> hook a malformed body uses. A raw endpoint has no library base class and therefore no such
> hook; its failures go straight to `ParameterBinding.Failure` — a `problem+json` 400 with the
> same message. A raw endpoint that needs a different failure shape can bind by hand from
> `HttpContext`, which is what the raw level is for.

**R2.14 — The interface ladder loses `TRequest` on body-less verbs rather than gaining a
placeholder.** A GET with a typed response and no body must not be written
`IGetEndpoint<NoRequest, UserResponse>`. A prototype that did this had five of eight
scenarios declaring a placeholder request they never touched — a word that appears in
every signature and in the OpenAPI-facing generic argument list and means nothing.

Specified ladder, for all five verbs:

| Arity | Body verbs (POST/PUT/PATCH) | Body-less verbs (GET/DELETE) | Handler |
|---|---|---|---|
| 0 | `IPostEndpoint` | `IGetEndpoint`, `IDeleteEndpoint` | `HandleAsync(HttpContext)` |
| 1 | `IPostEndpoint<TRequest>` — the **body** | `IGetEndpoint<TResponse>`, `IDeleteEndpoint<TResponse>` — the **response** | body verb: `HandleAsync(TRequest, ct)`; body-less: `HandleAsync(ct)` |
| 2 | `IPostEndpoint<TRequest, TResponse>` | `IGetEndpoint<TRequest, TResponse>`, `IDeleteEndpoint<TRequest, TResponse>` | `HandleAsync(TRequest, ct)` |

**Arity 2 is uniform across all five verbs** — always `(TRequest, TResponse)`. Only arity 1
differs, and the rule is one sentence: *GET and DELETE do not normally carry a body, so
their single type argument is the response; if your API sends one anyway, use the
two-argument form.*

**R2.14a — declaring a `TRequest` on a body-less verb means "this endpoint takes a body",
and routes it to `BodyEndpoint<TRequest>`.** Real APIs deviate from the specification —
Elasticsearch accepts a body on GET, and a DELETE that carries one is legal if
semantically undefined (RFC 9110). The two-argument form exists for exactly those, and it
must give them content-negotiated body binding like any POST, not the abstract binder that
`NonBodyEndpoint<TRequest>` supplies today.

**R2.14b — `NonBodyEndpoint<TRequest>` is deleted.** It loses every user. Route values no
longer travel through `TRequest` (R2.1), and a body-less verb that *does* declare a request
now derives from `BodyEndpoint<TRequest>` (R2.14a), so nothing is left that needs an
abstract `BindRequestAsync`. This removes a class, the abstract-member ceremony, the
"GET/DELETE must override `BindRequestAsync`" rule from the README, and the reflection pin
test that guards it. The library gets smaller, not merely the consumer's code.

**Why arity 1 is the response on body-less verbs, and not the request.** A DELETE that
*returns* a body is common — the deleted resource, or a confirmation envelope — while a
DELETE that *takes* one is rare. That is the same profile as GET, so the two agree. The
resilience argument is asymmetric and decides it: if arity 1 meant `TRequest`, then
DELETE-with-a-response — the common case — would need the two-argument form with a
placeholder request, reintroducing exactly the problem R2.14 exists to remove. The
converse costs nothing: an endpoint with no response body simply declares no type argument.

**Two rough edges, recorded rather than designed around:**

- **A body-less verb with a body but no declared response is inexpressible.**
  `IGetEndpoint<TRequest>` collides with `IGetEndpoint<TResponse>`. Fallbacks: declare a
  response type, or drop to arity 0. One README line, not a third interface.
- **A 204-style endpoint gets `HandleAsync(HttpContext)` rather than `HandleAsync(ct)`**,
  because it has no type argument and therefore lands at arity 0. The cancellation token is
  still reachable as `httpContext.RequestAborted`. Filling this gap would mean either a
  placeholder response type or changing arity 0 to stop meaning "raw", and neither is worth
  one ignored parameter.

The asymmetry at arity 1 is a genuine learnability cost and the README must state it
plainly. It is a breaking reinterpretation of today's `IGetEndpoint<TRequest>`, which the
no-backward-compatibility waiver permits.

**R2.14c — MPEP018 (Info) flags a suspected mis-declared type argument.** A body-less verb
whose arity-1 type argument is never returned from `HandleAsync` — or whose name ends in
`Request`/`Body`/`Command` — is probably a consumer who expected arity 1 to mean the
request. Heuristic, therefore **Info** severity, never a warning.

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
| MPEP018 | Info | A body-less verb's arity-1 type argument looks like a request, not a response (R2.14c) |
| MPEP019 | Error | An endpoint is nested inside a type that is not `partial` |

> **Adjusted for design A before M6 (R2.1 moved binding onto the endpoint):**
> - **MPEP008 becomes a Warning, and skips raw endpoints.** A raw endpoint may read
>   `RouteValues` by hand, so an unbound token there is not evidence of anything. On a typed
>   level the handler has no other way to reach the value, so an unbound token is almost always
>   a typo — but a token can legitimately exist purely for matching (`/v{version}/…`), so it
>   warns rather than blocks. It is checked against the endpoint's *own* `Path`, so a token a
>   group prefix contributes is not blamed on the endpoint.
> - **MPEP009 stays an Error.** A `[RouteParam]` whose key is not in the composed route is
>   prototype A's control C3: it compiles clean and returns 400 on every request, forever.
> - **MPEP012 moves to M8.** Duplicate names only matter once `.WithName()` is emitted, and
>   nothing emits it until typed links need a stable name. Its id stays reserved.
> - **MPEP007 treats an unreadable custom `Methods` as unknown verbs, and unknown as no
>   conflict.** As built in M6, a custom `Methods` written as a collection expression, a
>   `new[]`/`new string[]` of constants, or an `HttpVerbs.X` field *is* recovered at compile
>   time, so the measured partial-overlap case — `["GET","HEAD"]` against
>   `["HEAD","OPTIONS"]`, where HEAD answers 500 — is reported, naming only HEAD. Anything else
>   stays silent.
>
> Two defects in M2's `ComposedRoute`, found by M6's measurements and fixed there: `Normalise`
> erased the catch-all marker, which would have reported `/api/{**rest}` against `/api/{id}` as a
> conflict although ASP.NET Core gives the ordinary parameter precedence and both answer; and
> `Compose` joined `/api` + `users` as `/apiusers` where ASP.NET Core inserts the slash. And on
> its first run MPEP009 found its own target shape in an M4 test fixture — a `[RouteParam("userId")]`
> on a route with no `{userId}`.

**R3.3a — MPEP019 exists because the compiler's own message is unhelpful here.** Found
while implementing M1: an endpoint nested inside a non-`partial` type now emits a correct
nested partial chain, which the compiler then rejects with **`CS0260 Missing partial
modifier on declaration of type 'NestedContainer'; another partial declaration of this
type exists`** — pointed at the consumer's own class, mentioning a partial declaration they
never wrote, and saying nothing about endpoints. `PathSpec.AllPartial` already computes
exactly this condition and is currently unused. Report MPEP019 and skip emission, so the
consumer reads one accurate error instead of a confusing one.

> **As built in M11 — the code fix covers MPEP001, MPEP014 and MPEP019:** all three are "a
> `partial` is missing", reported on the endpoint's identifier, so one `CodeFixProvider`
> (`MakePartialCodeFixProvider`, "Make 'X' partial") repairs them. MPEP001 and MPEP014 make the
> endpoint itself `partial`; MPEP019 makes every enclosing type that is not yet `partial`
> `partial`, outermost first, leaving the ones that already are alone. `partial` is appended after
> the existing modifiers (CS0267 wants it last); a declaration with no modifiers hands its leading
> trivia to the new keyword. Fix All uses the stock batch fixer: measured, two MPEP019s on endpoints
> in the same container produce identical edits that it merges into one, so a custom fix-all bought
> nothing and was dropped. The ids are literals in the code-fix assembly, which does not reference
> the generator; a test pins them to `DiagnosticDescriptors`.
>
> Shipping: a separate `MintPlayer.AspNetCore.Endpoints.Generator.CodeFixes` assembly
> (netstandard2.0, Roslyn 4.14.0 with `PrivateAssets="all" ExcludeAssets="runtime"`, no
> `EnforceExtendedAnalyzerRules`), packed into `$(EndpointsAnalyzerPackPath)` beside the generator
> in **both** packages. The generator project owns it (`GetEndpointsCodeFixAssembly`, called by
> both pack targets) and guards it: pack fails if the path is not returned, the file does not exist,
> it is not among the packed files, or it lands in another folder (triggered on purpose:
> the missing file fails both packs, the wrong folder fails the generator's). It is
> deliberately not part of the generator's `GetTargetPath`, which would hand it to `csc` as an
> analyzer in every project in this repo that references the generator. A consumer restored from the
> Release nupkg builds with zero warnings and the generator's four files present.
>
> Found while writing the tests, not fixed here: an endpoint declared as a *private* nested type (a
> nested `class X` with no modifiers) is mapped anyway, and the generated `EndpointMapping.g.cs` /
> `EndpointContracts.g.cs` then fail with CS0122. No diagnostic explains it.

> **Fixed alongside M12 — MPEP024 (Error), "Endpoint or group is not accessible to generated code".**
> The generated files are their own top-level types and reach an endpoint or group only by its fully
> qualified name, so a type is usable only when it and every containing type is `public`, `internal`
> or `protected internal`. A `private`, `protected` or `private protected` nested type, anything nested
> inside one, and a `file` type (or anything nested in one — a `file` type cannot be named from another
> file at all) now gets MPEP024 on its identifier, naming the offending modifier or container, and is
> left out of the mapping, the descriptors, `Routes` and the contract. The partial base class and
> binder are still emitted for a hidden nested endpoint — a partial inside the partial container can
> reach it — so MPEP024 is the only error. For a `file`-local endpoint no partial is emitted either: a
> `partial class` in the generated file cannot join a file type and would declare a second, unrelated
> type that fails in generated code; so a *typed* file-local endpoint, like MPEP019's typed case,
> leaves `CS0115`/`CS0535` after MPEP024 (pinned by a test). A hidden **group** gets MPEP024 too and is
> unusable, like a cyclic one: neither it, nor a group nested inside it, nor any endpoint joining them is
> mapped, and only the group is reported. `internal` and `protected internal` nested endpoints are
> mapped with no diagnostic. The accessibility reason is a string on the value-equal `EndpointInfo` /
> `GroupInfo`, computed in the transform; MPEP017 stays reserved, so this is the next free id.
>
> Found while verifying the README, fixed with it: an endpoint in the **global namespace** that needs a
> partial (a typed endpoint, or any endpoint with bound properties) was emitted inside
> `namespace <global namespace>` — `ToDisplayString()` of the global namespace — and did not compile.
> It now gets no namespace block. The TestApp and the fixture corpus declare everything in a namespace,
> which is why nothing caught it; a README snippet pasted into a file without one hits it at once.

**R3.4 — Every diagnostic that aborts emission must emit a throwing stub.** Measured: a
generator that bails on a bad input leaves the abstract member unimplemented, so the
consumer reads `CS0534 … does not implement inherited abstract member BindRequestAsync`
*after* the real diagnostic and acts on the wrong one. MPEP007-017 must not produce a
cascade.

> **One exception, measured in M4: MPEP019 on a *typed* endpoint cannot be cascade-free.** When
> a typed endpoint is nested in a non-`partial` type, emission is skipped — but the endpoint
> still needs its generated base class, and C# offers no way to add one to a type nested in a
> non-partial container. The consumer sees MPEP019 first, then CS0115 on their `override` and
> CS0535 on `IEndpoint.HandleAsync(HttpContext)`. A test pins that MPEP019 comes first and that
> exactly those two follow. The raw-endpoint case of MPEP019 is fully clean.

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

> **Corrected by M5: `.Accepts<TRequest>("application/json")` must not be used.** It is not
> documentation-only. Routing's `AcceptsMatcherPolicy` reads the content types it declares, and
> once they exist routing answers `application/xml` or `text/plain` **itself** with an
> empty-body 415, before the endpoint runs. That would replace the library's own
> `problem+json` 415 and break every consumer whose MVC input formatters read XML — the content
> negotiation R2.9 exists to keep. Reproduced in a scratch app. The body is declared instead
> through a metadata object that lists no content types: routing ignores it, and the document
> still shows `requestBody: { required: true, content: application/json }` on both TFMs.

**R4.3 — Typed endpoints declare their documented failure responses.**
`.ProducesProblem(400)` and `.ProducesProblem(415)`, which are exactly the responses the
README's binding-failure table already guarantees.

> **Found in M5: declaring failures removes ApiExplorer's assumed 200.** ApiExplorer assumes a
> 200 only for an endpoint with no response metadata at all. After `.ProducesProblem(400)`,
> `GET /api/users` and `PUT /api/users/{id}` documented *only* their failures. A convention now
> restores a 200 when nothing declares a success — checking both the minimal-API response
> metadata and the interface MVC's `[ProducesResponseType]` implements, since the attribute is
> not visible through the first. So `DELETE /api/users/{id}` documents 204 alone, not 200 and
> 204. 415 is declared only for endpoints with a request body; GET and DELETE with bound
> properties document 400 only.

**R4.4 — Enrichment uses `AddOpenApiOperationTransformer`, not `.WithOpenApi`.** The
latter is deprecated and, separately, has never worked with `AddOpenApi()` (P4.2).

> **Known gap, recorded in M5: the manual `MapEndpoint<T>()` path documents bodies and failures
> but not route or query parameters.** It is reflection-based and has no compile-time shadow
> type to hand the framework, so a templated route mapped by hand stays spec-invalid in the
> document. Documented in `MapEndpoint`'s remarks. The generated path is complete.

**R4.5 — `SuccessStatusCode` reaches level-2 endpoints.** `IDeleteEndpoint<TRequest>` has
no `SuccessStatusCode` member, so the fix for P4.1 is generator-side: emit the declared
status for level-3 and a sane default for level-2.

**R4.6 — Transformer code is per-TFM.** `Microsoft.OpenApi` broke 1.x→2.x in .NET 10 and
breaks 2.x→3.x in .NET 11 (default spec version 3.0 → 3.1 → 3.2). The packages target
`net10.0;net11.0`, so anything touching `OpenApiSchema`/`OpenApiParameter` needs
conditional code. This is the real cost of R4.

> **Corrected by spike S1. This requirement overstated the cost and is not supported by
> the evidence.** One transformer source file compiled and ran unchanged against
> **Microsoft.OpenApi 2.12.0.0** (net10.0) and **3.10.0.0** (net11.0), with **zero `#if`**
> and zero warnings on both. `OpenApiSchema`, `IOpenApiSchema`, `OpenApiParameter`,
> `IOpenApiParameter`, `JsonSchemaType`, `OpenApiSchemaReference`,
> `OpenApiDocument.AddComponent` and `OpenApiOperationTransformerContext.Document` are
> present and identically shaped in both.
>
> The one API break that does bite is 1.x→2.x, not 10→11: the `Microsoft.OpenApi.Models`
> namespace **does not exist** on either TFM (consolidated into `Microsoft.OpenApi`), so
> any snippet carried over from a .NET 9 sample needs that `using` stripped. Also
> `OpenApiSchema.Minimum`/`Maximum` are **`string?`** (arbitrary precision) in both 2.x and
> 3.x — assigning a numeric literal is CS0029 — while `MaxLength` is `int?`.
>
> Both TFMs stay in the acceptance sweep as the guard. No conditional code is planned.

**R4.7 — A new package dependency is required.** `AddOpenApi` and
`AddOpenApiOperationTransformer` are **not in the `Microsoft.AspNetCore.App` shared
framework** — verified by reflecting over all 142 assemblies in
`C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\10.0.12`. They ship in the
separate `Microsoft.AspNetCore.OpenApi` package, so the runtime library must take a
reference on it. The PRD did not previously account for this.

> **Superseded before implementation: the library takes no new dependency.** Referencing
> `Microsoft.AspNetCore.OpenApi` from the runtime library would force it on every consumer,
> including those that never produce an OpenAPI document. The generator can see whether the
> *consumer's* compilation references it, so the schema transformer is emitted into the
> generated file **only when it does**. Consumers who use OpenAPI get typed parameter schemas
> with nothing to configure; consumers who do not pay nothing. The unconditional metadata —
> the request-body declaration, `.ProducesProblem()`, the shadow parameter itself — lives in
> the shared framework and needs no reference at all.
>
> As built in M5, the OpenAPI code is its own producer and its own file, `EndpointOpenApi.g.cs`,
> emitted only when the consumer compilation resolves the OpenAPI transformer types.
> `EndpointMapping.g.cs` names no OpenAPI type; the two are joined by one
> `static partial void OnEndpointMapped{n}(RouteHandlerBuilder)` hook per endpoint, which the
> compiler erases entirely when the OpenAPI file is absent. A test loads the compiled assembly
> and confirms the hook methods do not exist without the package and do with it.

**R4.8 — An optional route token cannot be documented as optional.** `/search/{term?}` is
emitted as path `/search/{term}` with `required: true`, and no separate `/search` entry, in
**both** shadow modes. This is framework behaviour, not a consequence of the shadow choice:
an operation transformer cannot add a path key, and OpenAPI forbids `required: false` on a
path parameter. Swagger UI therefore cannot exercise the absent case. Record it as a known
accuracy gap in R4.1 rather than attempting a fix.

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

> **Confirmed by spike S1. The fallback is not needed, and the chosen option is strictly
> better than the typed shadow rather than merely equal.**
>
> The parameter document produced by a `string?` shadow plus a ~70-line transformer is
> **byte-identical** to the typed shadow's, on net10.0 and net11.0 (`diff` empty after
> normalising the ephemeral port). Simultaneously, `GET /users/abc` still returns
> `application/problem+json` with `"The route parameter 'id' must be a valid Int32; 'abc'
> is not."`, where the typed shadow returns a **400 with a zero-length body and no
> content type**.
>
> Three findings that go beyond parity:
> - **Enums are better, not equal.** A typed `ItemKind?` shadow produces
>   `"ItemKind": { "type": "integer" }` — no `enum` list at all. The transformer emits
>   `"enum": [0,1,2], "type":"integer", "format":"int32"`. The typed shadow is also
>   case-*sensitive* (`/items/kind/vinyl` → empty 400) and accepts undefined values, so it
>   is the only mode where the framework's parse and the library's `Enum.IsDefined` check
>   disagree about validity.
> - **`[Range]`/`[StringLength]` on a bound property can reach the schema** as
>   `minimum`/`maximum`/`maxLength`. Every line of that diff is an addition; nothing is
>   lost. In the library the generator emits these as literals, so no reflection ships.
> - **An `int` path parameter is not `type: integer`.** ASP.NET Core 10/11 emits
>   `pattern: "^-?(?:0|[1-9]\d*)$"` + `type: ["integer","string"]` + `format: "int32"`.
>   The transformer must reproduce that triple exactly or parity drifts — a first attempt
>   writing plain `type: integer` was caught only by the byte-diff, which is why the M5
>   gate compares documents rather than eyeballing them.
>
> Corroborating R5.1a from the other direction: **`GET /paged/0` returns 200 in every
> mode.** `[Range]` is now *documented* and still not *enforced*, so the document
> advertises a constraint nothing checks. That asymmetry needs a README sentence.

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

> **Confirmed by spike S2 — it works — with one correction to the mechanism.** A
> generator-shaped call site (`MapMethods(path, methods, async (HttpContext ctx) => …)`,
> no typed parameter) returned **400 `application/problem+json`** with every field key
> populated, on both TFMs.
>
> **The null check described above will never fire.** `IOptions<ValidationOptions>`
> *always* resolves — `AddOptions()` registers the open generic and `ValidationOptions` is
> default-constructible — so it was non-null even in an app that never called
> `AddValidation()`. The real guard is `TryGetValidatableTypeInfo` returning **false**. Use
> `GetService` rather than `GetRequiredService` for the degenerate non-ASP.NET host, but do
> not treat null as the signal.
>
> Measured aside worth keeping: the library's explicit path emits a *more* RFC-compliant
> document than the framework's own filter, which returns `application/json` with no `type`
> and no `status`. And an `IValidatableObject` `ValidationResult` carrying **no** member
> names lands under the empty-string key `""` — ugly, not lost, worth a README line.

**R5.5 — A marked type must live in an assembly that itself calls `AddValidation()`.**
This is the condition most likely to bite, because failing it is silent on .NET 10. A
`[ValidatableType]` declared in a contracts assembly while `AddValidation()` is called in
the web app is **not** discovered — proven, `TryGetValidatableTypeInfo` returns false and
the request returns 200. The generator uses `ForAttributeWithMetadataName`, which sees only
its own compilation.

Two remedies, both proven to produce an identical correct 400:

- **Preferred:** the declaring assembly references `Microsoft.Extensions.Validation` and
  exposes its own `AddXxxValidation(this IServiceCollection s) => s.AddValidation();`,
  which the app calls alongside its own. Caveat: the second `AddValidation()` re-registers
  `RuntimeValidatableParameterInfoResolver`, so that resolver appears twice — harmless, but
  it is duplicate registration.
- **Fallback:** the app declares a local `[ValidatableType]` "anchor" whose only member is
  of the external type. The generator's automatic recursion into complex members then pulls
  the external type into the local resolver. Requires nothing of the other assembly. The
  anchor must be `public` or `internal` and must not be `file`-local (ASP0033).

> **Corrected by M7: exactly one `AddValidation()` call site per assembly, or net10.0 loses
> validation entirely.** When one compilation contains more than one `AddValidation()` call,
> the net10.0 validation generator fails with **CS8785** (*"hintName
> 'ValidatableInfoResolver.g.cs' … must be unique"*) and registers **no resolver at all** —
> every `[ValidatableType]` in that assembly silently stops validating, and the only symptom
> is a build warning. net11.0 does not have the defect. So the remedy above is safe only in
> this form: the declaring assembly exposes its one-line `AddXxxValidation()` wrapper, and a
> project that calls it must not *also* call `AddValidation()` directly in the same assembly.
> The TestApp follows this — `Program.cs` calls `AddTestAppValidation()`, a single wrapper in
> `Models/TestAppValidation.cs` — with a comment explaining why a direct call alongside it
> would break net10. The README must say this in one sentence.
>
> Also confirmed in M7: DataAnnotations on a positional record **parameter**
> (`record R([Required] string Name)`) are honoured, not only `[property: Required]`, on both
> TFMs. MPEP015 therefore treats both spellings alike.

**R5.6 — .NET 10 requires `<NoWarn>$(NoWarn);ASP0029</NoWarn>`, including in this
library's own projects.** On net10.0 `ASP0029` is reported as an **error**, not a warning
— *"'ValidatableTypeAttribute' is for evaluation purposes only"* — and it fires on
`[ValidatableType]`, `[SkipValidation]`, **and on the entire R5.2 API surface** the library
touches: `ValidationOptions.Resolvers`, `ValidationOptions.TryGetValidatableTypeInfo`,
`IValidatableInfo`, `ValidateContext`. Plain `Microsoft.NET.Sdk` and
`Microsoft.NET.Sdk.Web` behave identically — there is no Web-SDK exemption.
`services.AddValidation()` itself is not flagged. On net11.0 the attributes and the API
have graduated out of `[Experimental]`: same source, zero warnings, no `NoWarn` needed.

**R5.7 — The validation call needs `#if NET11_0_OR_GREATER`.** This is the largest
implementation cost S2 uncovered, and it is not a one-liner. The public surface changed
between the TFMs:

| net10.0 | net11.0 |
|---|---|
| `IValidatableInfo` | `IValidatableTypeInfo` |
| `ValidateContext.ValidationContext` | removed; `ValidateContext.ServiceProvider` |
| `ValidationErrors` is `Dictionary<string,string[]>` | `IReadOnlyDictionary<string, IReadOnlyList<ValidationError>>` |
| concrete `ValidatableTypeInfo` etc. public | not in the public ref surface |

Note this is the one place R4.6's retracted concern turns out to be true after all — just
for `Microsoft.Extensions.Validation` rather than for `Microsoft.OpenApi`.

**R5.3 — MPEP015 converts the framework's silent no-op into a visible one.** A request
type carrying DataAnnotations but not `[ValidatableType]` is exactly the shape that
silently returns 200 on invalid input.

> **Confirmed by S2, and narrowed.** .NET 11 ships **ASP0038** — *"'[ValidatableType]' has
> no effect if there is no 'AddValidation' call in the current project"* — which covers
> R5.5's failure but **not** R5.3's: it says nothing about a type carrying DataAnnotations
> with no `[ValidatableType]` at all. .NET 10 ships no equivalent diagnostic whatsoever
> (its generator ships only ASP0029). MPEP015 therefore still earns its place, and must
> **not** fire where ASP0038 already would, or a .NET 11 consumer gets told twice.

**R5.4 — The generator must not emit `[ValidatableType]`.** .NET 11 ships **ASP0037
"ValidatableType cannot be used in generated code"**, and generator-ordering makes it
unreliable regardless.

> **Corroborated verbatim by S2**, from the shipped analyzer's own message text:
> *"…has no effect because the type is declared in generated code… Source generators
> cannot inspect each other's output. Declare the type in a regular .cs file instead."*

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

> **Corrected while implementing M1. Two annotations do not clear it, and the gate moves
> to after M3.** Annotating `MapEndpoint<TEndpoint>` and `ParentGroupOf` did remove IL2091
> and IL2070 as predicted, but the annotations **cascade**: the analyzer then reported
> IL2087 on `ResolveGroupChain`'s call site and IL2072 on `ParentGroupOf`'s return, and
> annotating the return in turn produced four IL2063s. Measured residue with
> `IsAotCompatible=true`: **10 warnings**, of which only four are the genuinely-known
> `MakeGenericMethod` and `MapMethods(…, Delegate)` pair.
>
> Chasing the rest now would be wasted work, because **M3 deletes the code being
> annotated**: `ParentGroupOf`'s `GetInterfaces()` walk is replaced by
> `GetCustomAttribute`, which needs no `Interfaces` annotation at all, and the group-chain
> reflection changes shape with it. So M1 keeps the two parameter annotations, which are
> correct and permanent, and **defers turning `IsAotCompatible` on until M3 has landed**
> and the true residue is visible. Turning it on early would put ten warnings into a repo
> whose convention is a warning-clean build, for no benefit.

> **Resolved with M12: `IsAotCompatible` is on for both shipping runtime projects, on both TFMs.**
> Measured with `-t:Rebuild -c Release -p:IsAotCompatible=true`, deduplicated by file, line, code and
> TFM: **Abstractions 0 warnings; the runtime library 5 on net10.0 and 4 on net11.0.**
> - `MapEndpoint<TEndpoint>` → `MapMethods(…, Delegate)`: IL2026 + IL3050 (both TFMs).
>   `RequestDelegateFactory` binds the delegate by reflection.
> - `MapGroupOf` → `MethodInfo.MakeGenericMethod`: IL2060 + IL3050 (both TFMs). The group's static
>   abstract `Prefix`/`Configure` are reachable only through a generic parameter.
> - net10.0 only, `RequestValidation`: IL2026 on `new ValidationContext(instance, services, items)`,
>   the overload that discovers its display name by reflection.
>
> All three are handled honestly, with no suppression. The first two are inherent to the manual,
> reflection-based path, so `MapEndpoint<T>` and `MapGroupOf` are annotated
> `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` with one message saying why — the annotation *is*
> the correct statement of that path, and a trimmed consumer calling it now gets the warning at their
> call site (a deliberate, visible API change; no code in this repo outside the tests calls it). The
> third is not inherent: the constructor with an explicit `displayName` is trim-safe, and passing
> `request.GetType().Name` is the value the reflective one falls back to when no member name is set
> (the validation resolver overwrites it per member). After the change: **0 warnings on both TFMs**,
> with `EnableTrimAnalyzer`/`EnableAotAnalyzer` confirmed on.
>
> This remains a regression gate for the library's own code, not an AOT claim (R7.6 stands). What the
> analyzer cannot see is unannotated rather than safe: the *generated* mapping calls the same
> `Delegate` overload of `MapMethods` from the consumer's assembly; `BodyEndpoint` reads through MVC
> input formatters (not trim-annotated) or `ReadFromJsonAsync<T>()` with whatever `JsonOptions` the app
> configured. The README says exactly this.

**R6.5 — `MintPlayer.SourceGenerators.Tools` is upgraded 10.16.0 → 10.21.0**, moving the
generator's Roslyn floor from 4.14.0 to 5.x. Safe given the packages target
`net10.0;net11.0` and `global.json` pins `11.0.100-rc.1.26425.128`, but it is a breaking
change for consumers on older SDKs and must be re-verified per R6.2.

> **Withdrawn. Spike S4 disproved the premise, and the upgrade is dropped from this PR.**
>
> "Every SDK that can build net10.0 ships Roslyn 5.x" is true and irrelevant. **The real
> boundary is 5.3, not 5.0**, and no published Tools version binds 5.0. Measured: SDK
> 10.0.112 → Roslyn 5.0.0, SDK 10.0.401 → 5.9.0, the pinned 11.0.100-rc.1 → 5.11.0; and
> `Tools` 10.19.0 onward all bind `Microsoft.CodeAnalysis 5.3.0.0`. On SDK 10.0.112 the
> upgraded analyzer is **rejected**:
>
> ```
> CSC : warning CS9057: Analyzer assembly '…Generator.dll' cannot be used because it
>       references version '5.3.0.0' of the compiler, which is newer than the currently
>       running version '5.0.0.0'.
> Program.cs(3,5): error CS1061: 'WebApplication' does not contain a definition for
>       'MapConsumerEndpoints'
> ```
>
> The consumer does not see "unsupported SDK"; they see the library as broken.
>
> **Renaming the folder does not fix it.** `analyzers/dotnet/roslyn5.0/cs` was tested
> directly and produced the identical CS9057 + CS1061: the folder name controls *selection*,
> not *binding*. A 5.0 host selects the `roslyn5.0` folder and then rejects the 5.3-bound
> assembly anyway. That folder name only becomes honest once Tools itself is built against
> `Microsoft.CodeAnalysis 5.0.0` — a change that exists in the toolkit's working tree but
> is **in no published package**.
>
> Since the upgrade is a "while we are here" item that nothing else in this PR depends on,
> and since it would silently drop every SDK on the Roslyn 5.0–5.2 line, it is withdrawn.
> Revisit when a Tools release binds 5.0.0.

> **Revisited 2026-09-25 on `fix/endpoints-open-generics` (PR #35): upgraded to Tools 11.0.0, and the
> reason for the withdrawal no longer applies — by owner decision, not because the boundary moved.**
> No Tools release binds 5.0.0; 11.0.0 binds `Microsoft.CodeAnalysis 5.9.0`, so the floor rises further
> than S4 feared. The owner decided that **Roslyn 5.9 is the deliberate minimum and older Roslyn versions
> are not supported**: no multi-targeted analyzer folders, fallbacks or shims. The consequence S4
> objected to, a consumer on an older SDK seeing the library as broken, is handled by documenting the
> requirement, not by avoiding it: the Endpoints and Generator READMEs now state ".NET SDK 10.0.400+ or
> 11.x, or Visual Studio 2026 (with Roslyn 5.9+)" and quote the exact symptom. Re-measured with a local
> Release pack and a scratch `net10.0` web consumer: SDK 10.0.401 (Roslyn 5.9.0) builds clean; SDK
> 10.0.112 (Roslyn 5.0.0) gives the same pair S4 recorded, now naming 5.9.0.0 —
> `CSC : warning CS9057: Analyzer assembly '…\MintPlayer.AspNetCore.Endpoints.Generator.dll' cannot be used
> because it references version '5.9.0.0' of the compiler, which is newer than the currently running
> version '5.0.0.0'.` and `Program.cs(4,5): error CS1061: 'WebApplication' does not contain a definition
> for 'MapConsumerEndpoints' …`. The analyzer stays in the single unversioned `analyzers/dotnet/cs`
> folder; R6.5a's suppression still removes the Tools props' copies (now `analyzers/dotnet/roslyn5.9/cs`),
> and the packed listing is one folder with the Generator, CodeFixes and Tools dlls. The details are in
> `PRD-EndpointsOpenGenerics.md`, "Dependency update".

**R6.5a — The standalone Generator package runs its generator twice, and this predates the
upgrade.** Found by S4 while testing folder layouts. `Tools`' own `build/*.props` packs
copies of the analyzer into `analyzers/dotnet/roslyn4.0/cs` and `roslyn4.9/cs` *in addition
to* the `analyzers/dotnet/cs` copy this repo's pack target places. The SDK resolves more
than one as `@(Analyzer)`, the generator runs twice, and the consumer gets
`error CS0101`/`CS0111` duplicate definitions. The 10.16.0 baseline package has the same
three folders, so this is a live defect on the shipped package, not a consequence of
anything in this PR. **Exactly one analyzer folder must ship**, so the Tools-props
contributions have to be suppressed. Fix it here regardless of R6.5's withdrawal.

**R6.6 — New attributes ship in the existing packages.** No `*.Attributes` split. The
toolkit's own guidance carves out packages that ship runtime code *and* a generator as the
case where they stay together; a fourth project adds a packaging surface where every
mistake fails silently, for no benefit.

**R6.7 — Equatable models stay equatable.** `EndpointInfo` is a hand-written
`IEquatable<T>` with no collections today. Adding `ImmutableArray<BoundMemberInfo>`
without extending `Equals` silently kills incremental caching, because `ImmutableArray`
equality is by reference. `EndpointGeneratorIncrementalTests` will catch it if the tests
are kept honest.

> **Superseded 2026-09-25 (PR #35, PRD-EndpointsOpenGenerics addendum D10):** the models are no longer
> hand-written. Every one is a `[GenerateEquality]` partial class (MintPlayer.ValueComparerGenerator 12.0.1),
> which generates `IEquatable<T>` from the properties and compares `ImmutableArray` members element-wise,
> so adding a collection no longer needs a matching `Equals` edit. The rule's intent stands: the incremental
> tests still assert the cache hits.

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

> **As built in M8:**
> - **`ToString()` substitutes after all, and R7.2's "do not hand-roll" now applies to *how*.**
>   `EndpointRoute.ToString()` (and the implicit `string` conversion `Results.Created` goes through)
>   has no `LinkGenerator`, so it ports routing's `TemplateBinder`/`UriBuildingContext` state
>   machine and encodes with `UrlEncoder.Default`, the encoder routing uses. A test resolves every
>   case of a 43-value battery through a real `LinkGenerator` and requires byte equality, on both
>   TFMs. `Path(LinkGenerator)`/`Path(HttpContext)` delegate as R7.2 says and throw where the
>   framework returns null. `Path(HttpContext)` passes only `PathBase`, not the request's ambient
>   route values, so one link does not produce different URLs on different pages.
> - **One name, three uses:** `WithName` is emitted after the endpoint's own `Configure`, so a
>   `WithName` inside `Configure` no longer wins — `[EndpointDescriptorName]` is the way to name.
> - **`operationId` needs the method for a multi-verb endpoint.** The framework copies the endpoint
>   name into every operation of the endpoint, so a `MapMethods(["OPTIONS","HEAD"])` endpoint would
>   put one id on two operations, which OpenAPI forbids. The generated OpenAPI file suffixes the
>   method in that case only (`PreflightEndpointHead`).
> - **A catch-all token is an optional link parameter**, as it is to `LinkGenerator`: an omitted
>   `{**path}` generates the route without it.
> - **A MPEP012 duplicate is mapped without a name and gets no link** (the earlier endpoint keeps
>   both), so a consumer who demotes the diagnostic gets one unnamed endpoint instead of a
>   first-request crash, and never a `CS0111` in generated code.

**R7.4 — Cross-assembly contract travels as an assembly attribute.** `[assembly:
EndpointContract(typeof(GetUser), "/api/users/{id}", …)]` emitted into the server's own
generated file and read downstream via `IAssemblySymbol.GetAttributes()`. Proven to
survive the assembly boundary where the `Path` literal does not. Razor and RestEase both
use this pattern. **Do not build a JSON manifest**: Microsoft deprecated
`Microsoft.Extensions.ApiDescription.Client`, `<OpenApiReference>` and `dotnet openapi` in
.NET 10 Preview 7 and deleted the source directory.

> **Confirmed by spike S6, with one constraint that decides M9's whole shape.**
>
> **The client must reference the server assembly metadata-only, never by
> `ProjectReference`.** A `ProjectReference` propagates the server's
> `FrameworkReference Microsoft.AspNetCore.App` transitively — the client then compiles
> `typeof(WebApplication)` successfully and pulls 310 reference assemblies, and a Blazor
> WASM client does not merely bloat, it **fails to build**: `NETSDK1082: There was no
> runtime pack for Microsoft.AspNetCore.App available for the specified
> RuntimeIdentifier 'browser-wasm'`. Four mitigations were tested and **all four still
> fail**: `ExcludeAssets="runtime"`, `PrivateAssets="all"`, a server-side
> `<FrameworkReference Update="…" PrivateAssets="All" />`, and the plain control. The
> client fails in `ProcessFrameworkReferences` before the server project is even built, so
> it is not suppressible from either side.
>
> The working shape is `<Reference Include="Server"><HintPath>…</HintPath><Private>false</Private></Reference>`
> alongside a normal `ProjectReference` to Contracts. Measured cost to the client: **one
> extra compile-time reference assembly (168 → 169) and zero shipped bytes** — the
> published `wwwroot/_framework` contains no server assembly at all. And the failure mode
> is loud, not silent: hiding the server DLL gives `MSB3245` followed by `CS1061` on every
> generated method.
>
> **Emitting the attributes into Contracts instead does not work**, and was tested rather
> than assumed: the generator wired into Contracts emits nothing, because Contracts has no
> endpoint types and cannot reference Server without a cycle. It would need a
> hand-maintained attribute list or an IL-rewriting build step. Rejected.
>
> Also proven: the ordering constraint is real (`CS1730` when attributes follow a type in
> the same file) and the generator's emit order satisfies it; and a **raw, non-`partial`
> endpoint's contract crosses the boundary identically**, which is the case a type-level
> attribute could not have reached and the reason assembly-level is correct.

**R7.4a — The client generator must treat "zero contracts" as an empty client, never an
error.** dotnet/roslyn#57997's failure mode — `ReferencedAssemblySymbols` returning nothing
under the IDE's analysis service — **is not detectable from a command-line build at all**;
S6 measured `Compilation.References` and `ReferencedAssemblySymbols` as indistinguishable
there (310/310, 168/168, 169/169, 195/195). So the guard cannot be a diagnostic; it has to
be a design choice. A generator that errors on zero contracts would paint a wall of red
squiggles in the IDE while `dotnet build` stays green.

Memoise on `compilation.GetMetadataReference(assemblySymbol)`, which S6 confirmed
round-trips reference-identically (same=169, different=0). One correction to the stated
rationale: `IAssemblySymbol` identity is **not** unstable *within* a shared
`MetadataReference` — two compilations sharing one reference returned
`ReferenceEquals(sym1, sym2) == true`. The Akka.NET advice still holds for the case where
the reference set changes, but the PRD should not overstate it.

> **As built in M9 (R7.4, R7.4a):**
> - **The attribute class is generated into the server assembly, not shipped in Abstractions.**
>   Measured: an attribute whose class lives in an assembly the client does not reference is an
>   error type to Roslyn, and its arguments decode to *nothing* (0 constructor arguments, 0 named).
>   Abstractions is a Web SDK package, so the client cannot reference it (NETSDK1082, as for the
>   server). `EndpointContracts.g.cs` therefore declares `internal sealed class
>   EndpointContractAttribute` in `MintPlayer.AspNetCore.Endpoints.Generated` next to the
>   `[assembly: …]` usages: the contract is self-contained in the one assembly the client does
>   reference. Internal, so two endpoint assemblies referencing each other do not see each other's
>   copy; an internal attribute still survives into reference assemblies and decodes in full (both
>   measured). The one edge left is `InternalsVisibleTo` between two endpoint assemblies, which would
>   give CS0436 (a warning).
> - **Shape:** `EndpointContract(Type endpoint, string name, string route, string[] methods)` plus named
>   `Version`, `RequestType`, `ResponseType`, `RouteParameterNames`/`RouteParameterTypes` (every token,
>   template order) and `QueryParameterNames`/`QueryParameterTypes`; empty ones are omitted. Versioning:
>   new facts are new named arguments, which an older client ignores; a change an older client would
>   misread raises `Version` (1 today), and such a client skips the contract with MPEP021. There is no
>   `SuccessStatusCode` — the client needs none. Contracts cover exactly the typed-link endpoints (known
>   route, named, not an MPEP012 duplicate) that also have compile-time-known verbs.
> - **Opt-in is an MSBuild property, `<GenerateEndpointsClient>true</GenerateEndpointsClient>`**, made
>   compiler-visible by `build/*.props` in both packages. Not an assembly attribute: the client has no
>   package of ours with a `lib` folder to take the attribute type from, and a generator-injected one
>   would land in every server compilation too. Without opt-in, a host project referencing its endpoint
>   modules would grow unrequested clients.
> - **The client generator ships in the analyzer-only `MintPlayer.AspNetCore.Endpoints.Generator`
>   package** (no `FrameworkReference`), in the same assembly as the endpoint generator. That generator
>   now writes nothing — and MPEP006 stays silent — where `IEndpointRouteBuilder` does not resolve.
> - **The metadata-only reference ships as a targets file, not only a snippet**:
>   `<EndpointsServerReference Include="…\Server.csproj" />` builds the server in the client's
>   Configuration and TargetFramework (per-item `SetTargetFramework` override) through an MSBuild task
>   call, which is not a ProjectReference and so carries no FrameworkReference, then adds its output
>   assembly as `<Reference … Private="false">`. Chosen because a hand-written `HintPath` has to get the
>   configuration, TFM and build order right by hand. Forced references measured on the sample client
>   (`TestApp.Client`, net10.0): **168 compile references = 167 `Microsoft.NETCore.App.Ref` + the server
>   assembly**; none of the server's own dependencies; its `bin` holds only its own assembly. The raw
>   `<Reference>` snippet is documented as the fallback.
> - **URLs:** the client cannot reference the runtime library (Web SDK) for `EndpointRoute`, so the
>   substitution moved into `EndpointTemplateBinder.cs`, written in generated-code style, which the
>   runtime compiles and the generator embeds and emits into the client with only its namespace changed.
>   One source: the 43-case `LinkGenerator` battery now also runs against the emitted copy in the
>   client assembly.
> - **Client shape:** `internal sealed partial class {LastAssemblyNameSegment}Client` (full name on a
>   collision), ctor `(HttpClient, JsonSerializerOptions? = null)`, one `{Name}Async` per endpoint
>   (`{Name}{Verb}Async` per verb for a multi-verb one); parameters: route tokens typed as on the server
>   (optional for `{x?}`, `{x=1}`, catch-alls), then `body` (the request type, as JSON), then optional
>   query parameters, then a `CancellationToken`. A declared response type is read as JSON after
>   `EnsureSuccessStatusCode()` (204 → `default`); without one the `HttpResponseMessage` is returned
>   unchecked.
> - **Memoisation** is a `ConditionalWeakTable<MetadataReference, …>` holding strings only (a symbol
>   would pin its compilation). A result with an unresolvable type is never cached, so adding the missing
>   reference takes effect; accessibility is judged without `InternalsVisibleTo` so a cached answer does
>   not depend on which client asked.
> - **R7.4a held:** zero contracts is no file and no diagnostic. New diagnostics, client-side and without
>   a source location: MPEP021 (contract skipped — unresolvable or non-public type, newer format),
>   MPEP022 (a signature type is declared in the server assembly, which a metadata-only reference does
>   not deploy), MPEP023 (`System.Net.Http.Json`/`System.Text.Encodings.Web` missing).
> - A slash in a string route value is encoded `%2F` exactly as `LinkGenerator` does, but cannot
>   round-trip: ASP.NET Core does not decode `%2F` back into a route value. Framework behaviour, recorded
>   so nobody "fixes" the client.

**R7.5 — The contract snapshot is a committed OpenAPI document plus `oasdiff`, not a
reimplementation of `PublicApiAnalyzers`.** `OpenApiVersion` must be pinned explicitly —
the default moved 3.0 → 3.1 → 3.2 across three releases and an unpinned snapshot churns
catastrophically on SDK upgrade. Note build-time generation **runs `Program.cs`** against a
mock server.

> **As built in M10:**
> - **The snapshot** is `Endpoints/MintPlayer.AspNetCore.Endpoints.TestApp/openapi/MintPlayer.AspNetCore.Endpoints.TestApp.json`,
>   written on every build by `Microsoft.Extensions.ApiDescription.Server` (10.0.12 for net10.0,
>   11.0.0-rc.1.26425.128 for net11.0, matching `Microsoft.AspNetCore.OpenApi`).
> - **One snapshot, from net10.0.** The package's multi-targeting targets generate for the *first*
>   TFM only. Measured with the version pinned, the net11.0 document differs from net10.0's in two
>   ways only: the `openapi` field (`3.1.1` against `3.1.2` — the patch level is Microsoft.OpenApi 2.x
>   against 3.x) and the order of the keys under `responses` (net11.0 sorts by status code).
>   `oasdiff breaking` between the two reports no breaking change. A per-TFM snapshot would need a
>   custom target around the package's private `_OpenApiDocumentsCache`, which both inner builds share.
> - **Pinning `OpenApiVersion` takes two settings, not one.** `AddOpenApi(o => o.OpenApiVersion = OpenApi3_1)`
>   pins what `/openapi/v1.json` serves, but the build-time tool passes its own version and ignores
>   it: with only that set, net11.0 still wrote `3.2.0`. The csproj also passes
>   `<OpenApiGenerateDocumentsOptions>--openapi-version OpenApi3_1</OpenApiGenerateDocumentsOptions>`.
>   Unpinned, net10.0 writes `3.1.1` and net11.0 `3.2.0`.
> - **The `GetDocument.Insider` guard cannot skip `app.Run()`.** The tool swaps in a no-op server
>   and reads the endpoints from the running host; measured, returning before `Run()` writes a
>   document whose `paths` is empty. `Program.cs` computes `isBuildTimeDocumentGeneration` and says
>   what belongs behind it (migrations, seeding); the TestApp has no such side effect, so nothing is
>   behind it yet.
> - **CI** (`pull-request.yml`): after the Release build, `git status --porcelain` must be empty —
>   the whole tree, so any build that rewrites a tracked file fails too; then oasdiff 1.32.1
>   (release tarball, SHA-256 verified) runs `oasdiff breaking <base> <head> --fail-on ERR`
>   against the snapshot at the PR's base SHA, and skips with a notice while the base branch has no
>   snapshot (true for the PR that introduces it). Verified locally: renaming a route regenerates the
>   file (drift), removing a path is `api-path-removed-without-deprecation` and exit 1, identical
>   documents exit 0.

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

> **As built in M12:** the package README was rewritten from scratch for the as-built library (782 →
> 621 lines, covering considerably more, no `IMemberOf`, no hand-written `BindRequestAsync` for route values). The binding rule is
> one bold sentence at the top of "Route and query values"; R8.2's passthrough has its own section. It
> documents what the library does not do — no automatic `[ValidatableType]`/`AddValidation()`
> discovery, `[Range]` on a bound property not evaluated, no AOT-safe binding, no parameter
> documentation on the manual path — and the full diagnostics table through MPEP024, with MPEP003/004
> marked retired and MPEP017 reserved. Every C# block is compiled verbatim: a script extracts the
> blocks into a scratch server project (both TFMs, local runtime project + generator as analyzer, the
> OpenAPI package) and a scratch client project (`GenerateEndpointsClient`, `EndpointsServerReference`
> to the scratch server), and both build with zero warnings and zero generator diagnostics. The sample
> blocks form one coherent API (assembly `MyShop.Api`), so the `Routes.Api.Users.GetUser(...)` links and
> the `ApiClient` calls in the text are the generated ones. `.Abstractions` and `.Generator` got short
> package READMEs of their own.

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
