# PRD: Open-generic endpoint classes in MintPlayer.AspNetCore.Endpoints

Issue: [#34 — an open-generic endpoint class emits uncompilable code instead of being skipped](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/issues/34).
Affected: `MintPlayer.AspNetCore.Endpoints` **11.1.0-rc.0** (both `net10.0` and `net11.0`), the first
release of the redesign (PR #32).

*(Draft 1, written from the issue before the investigation. Claims marked **[verify]** are
hypotheses the investigation must confirm or correct; corrections are appended as blockquotes, as
in `PRD-EndpointsErgonomics.md`.)*

## Overview

The generator discovers every class in the assembly that implements an endpoint interface and
emits, per endpoint, a factory field in `EndpointMapping.g.cs`, an `[assembly: EndpointContract]`
in `EndpointContracts.g.cs`, and (since M8) a typed link in `EndpointRoutes.g.cs`. None of those
places has the endpoint's own type parameters in scope. For an endpoint class with a type parameter,
the generator writes that type parameter anyway, and **the whole assembly stops compiling**:

```csharp
[MemberOf<ApiGroup>]
public class Echo<TPayload> : IPostEndpoint where TPayload : class
{
    public static string Path => "/echo";
    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(typeof(TPayload).Name));
}
```

```
error CS0246: The type or namespace name 'TPayload' could not be found
  EndpointMapping.g.cs(18,109)      ObjectFactory<global::Echo<TPayload>> _f0 = ...
  EndpointContracts.g.cs(12,108)    [assembly: EndpointContract(typeof(global::Echo<TPayload>), ...)]
```

## Problem statement

**P1 — One generic class breaks every endpoint in the project.** Discovery is not opt-in, so the
error is not confined to the generic class: the other, valid endpoints in the assembly stop
compiling with it. There is no exclusion attribute.

**P2 — No diagnostic explains it.** The error points at a generated file the consumer cannot edit
and names a type parameter the consumer never wrote there. This is the same class of defect as the
CS0122 fixed by MPEP024 in M12: a shape the generator must not map, mapped anyway.

**P3 — The manual door is closed too.** `MapEndpoint<Echo<string>>()` is the natural registration
for a generic endpoint whose type argument is only known at the call site. That call site is
unreachable while the assembly does not compile.

**P4 — Libraries are the real case.** A closed derived type (`class EchoString : Echo<string>`)
already works, including inherited `[MemberOf<T>]` (reported in the issue; **[verify]**). That is a
workaround for an application, which knows its closed types. It is not one for a library:
MintPlayer.Spark's 14 auth endpoints are generic over `TUser : SparkUser`, and the closed type is
chosen by the consuming application through `MapSparkIdentityApi<TUser>()`. This issue blocks
Spark's adoption of the generator.

## Goals

1. An assembly containing an open-generic endpoint class **compiles**, and its other endpoints are
   mapped exactly as before.
2. The open-generic endpoint is **not mapped, linked or contracted** by generated code, and a
   diagnostic says so and tells the consumer what to do instead.
3. `MapEndpoint<Echo<Closed>>()` on the manual path **works** for such an endpoint: route, group
   chain, name, binding, validation and OpenAPI metadata behave as for a non-generic endpoint.
   Whatever does not, is documented.
4. A library can expose its generic endpoints to an application with no per-endpoint boilerplate in
   the application (the Spark shape, `MapSparkIdentityApi<TUser>()`).
5. A red test reproduces the issue before any fix; the same test is green afterwards.

## Non-goals (draft; the investigation may move items)

- **`typeof(Echo<>)` in the contract.** A client needs closed types for its method signatures;
  an open contract has no consumer. (Agrees with the issue.)
- **Mapping every closed construction automatically** (scanning the compilation for
  `Echo<X>` usages). Constructions can come from other assemblies at runtime; the generator
  cannot see them.

## Requirements

**R1 — Detection.** A candidate is *open* when the class itself, or **any containing type**, has
type parameters (`Outer<T>.Inner` is as open as `Echo<T>`). An open **group** class is treated the
same way, since a group's `Prefix` is read through its closed type. **[verify]** which of these
shapes break today, and whether a closed class deriving from an open generic base is and stays
fine.

**R2 — Skip, do not emit wrong code.** An open endpoint gets no factory field, no `Map…` call, no
`EndpointDescriptor`, no typed link, no contract, no OpenAPI hook. Other endpoints are unaffected,
and route diagnostics that compare endpoints (MPEP007 duplicate route, MPEP012 duplicate name)
ignore it.

**R3 — Keep what the manual path needs.** Whatever the generator emits *inside* the endpoint's own
partial declaration (the typed-level base class, `BindParameters` for `[RouteParam]`/`[QueryParam]`,
the raw `IParameterBinder`) is still emitted for an open endpoint, with the type parameters (and
constraints, where C# requires them) repeated correctly. This is what makes `MapEndpoint<Echo<X>>()`
bind route values. **[verify]** whether the partial emission handles the endpoint's own type
parameters today.

**R4 — Diagnostic.** A new **Info** diagnostic, next free id (**MPEP025** unless taken), on the class
identifier, in the style of MPEP016:
*"Endpoint class 'Echo<TPayload>' has type parameters and is not mapped by the generated method;
map a closed type with MapEndpoint<T>()."* Info rather than Warning: the shape is legitimate and
the consumer's next step is intentional, not a fix. **[decide]** whether an explicitly generic
*group* gets the same id or its own.

**R5 — Manual path correctness for closed generics.** For `MapEndpoint<Echo<X>>()`:
- the route and group prefix come from the closed type (static abstract `Path`/`Prefix` read through
  `TEndpoint`);
- `[MemberOf<T>]` inherited through the generic definition resolves (the runtime walks `BaseType`);
- **the endpoint name**: `.WithName()` must not produce `Echo\`1`, and two closings of the same
  definition (`Echo<A>`, `Echo<B>`) must not collide into the same name, which ASP.NET Core rejects
  at the first request. **[verify]** what the name is today and whether it collides.
- OpenAPI `operationId` uniqueness holds for the same reason.

**R6 — The library shape.** **[decide after investigation]** between:
- **(A) Skip + diagnostic only.** The library writes `MapSparkIdentityApi<TUser>()` by hand as a
  list of `MapEndpoint<X<TUser>>()` calls. Smallest change; the issue's suggested fix.
- **(B) A generated generic mapping method** per distinct type-parameter list (name, arity and
  constraints), e.g. `MapMyLibEndpoints<TUser>(this IEndpointRouteBuilder) where TUser : SparkUser`
  calling `MapEndpoint<X<TUser>>()` for each open endpoint that shares it. Removes the hand-written
  list; raises questions of naming, constraint equality and endpoints with differing type
  parameter names.
- **(C)** (A) now, with (B) recorded as rejected or deferred with a reason.

**R7 — Documentation.** The README gains a "Generic endpoints" section: closed derived types for
applications, `MapEndpoint<T>()` (or the R6 outcome) for libraries, and the diagnostic in the table.

**R8 — Version.** A fix release, proposed **`11.1.1-rc.0`** for the three Endpoints packages; the
release remains the owner's decision.

## Acceptance criteria

1. A generator test with the issue's exact repro is **red** on `master` (CS0246 in generated code)
   and **green** after the fix: the compilation has zero errors and the new Info diagnostic.
2. The same assembly's non-generic endpoints produce byte-identical mapping, contract and route
   output with and without the generic class present.
3. Every open shape in R1 is covered by a test (own type parameter; nested in a generic container;
   typed levels whose `TRequest`/`TResponse` is the type parameter; bound properties on a generic
   partial; generic group).
4. A runtime test maps `MapEndpoint<Echo<A>>()` and `MapEndpoint<Echo<B>>()` in one app: both
   answer on their routes, route values bind, names are distinct, and the OpenAPI document has
   unique `operationId`s.
5. A closed derived type keeps working, including inherited `[MemberOf<T>]` (regression test).
6. Both TFMs green; solution `-t:Rebuild` warnings at the 36 CS1591 baseline.

## Version

Created 2026-09-25 from issue #34. Branch `fix/endpoints-open-generics` (from `master` at `c04ffac`).
