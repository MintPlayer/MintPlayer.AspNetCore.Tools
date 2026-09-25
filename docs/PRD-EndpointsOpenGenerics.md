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

> **Investigation findings (2026-09-25, I1–I3; measured on net10.0 against `c04ffac`).**
> - **R1:** every open shape breaks the build: own type parameter; nested in a generic container;
>   typed levels whose `TRequest`/`TResponse` is the parameter; bound properties. The issue's five
>   CS0246 are reproduced at the exact reported lines. An `abstract` generic class is already
>   skipped. A closed class derived from a generic base works, including an inherited
>   `[MemberOf<T>]`.
> - **R3 [verify] answered: no.** `ClassName` is `symbol.Name`, so the partial for `GetById<T>` is
>   written as `partial class GetById`, a separate, non-generic phantom type. The real class never
>   gets its base class (CS0534/CS0115/CS0535) or `BindParameters` (CS1061). Containers are
>   reopened correctly with their type parameters (`OpenPathSpec`). Repeating the endpoint's own type
>   parameter list, without constraints (C# allows that on a partial part), was measured to fix it:
>   binding returns 200/400 on the manual path.
> - **Open groups:** `[MemberOf<Api<T>>]` is illegal C# (CS8968), so an open group can only arise
>   by nesting. A non-generic endpoint in a *closed* generic group, `[MemberOf<Api<string>>]`,
>   works at run time, but group keys are the open FQN (`Api<T>`) while membership is the closed
>   one. The generator therefore silently drops its link and contract, skips it in MPEP007, and
>   misfires MPEP016 on `Api<T>`.
> - **Existing diagnostics on open endpoints:** MPEP007 and MPEP012 fire with an open endpoint as
>   a participant; MPEP012 treats `Echo<T>`, `Echo` and `Echo<T1,T2>` as one name. Filtering in
>   `EndpointMappingPlan`'s mappable set (next to MPEP024) removes them from both checks. MPEP008
>   still applies usefully, since the partial is kept.
> - **R5, manual path:** route, prefix, inherited `[MemberOf<T>]` (also through a generic base),
>   typed levels, validation, 415 and the library shape (`MapEndpoint<Passkeys<TUser>>()` inside a
>   generic method) all work. **Naming is broken:** `EndpointNameOf` uses `Type.Name`, so every
>   closing is named `Echo`1`, and two closings throw `Duplicate endpoint name 'Echo`1'` on the
>   first request, `/openapi/v1.json` included. `[EndpointDescriptorName]` on a generic class
>   collides the same way. The manual path also declares no route parameters in OpenAPI (a
>   documented limit, which matters for Spark's `{id}` routes), and it carries
>   `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`.
> - **R6:** Spark's 14 generic routes share one list (`TUser : SparkUser, new()`) but are mapped
>   **conditionally, in clusters** (passkeys and external-login linking are behind options). A
>   generated unconditional generic method would map disabled routes. Option B would also have to
>   copy constraints exactly and handle constraint-type accessibility (CS0703) and
>   same-arity-different-constraint collisions (CS0111), and is estimated at 2–3 days.
> - **Found, outside #34:** `new static Path` on a closed derived type makes the generated links
>   and contracts say the new route, while at run time the base's `Path` is used unless the derived
>   type lists the endpoint interface again.

> **Decided 2026-09-25 (grill session, supersedes R2's "skip only", R4, R6 and R8).** The owner rejected
> "skip + let the library call `MapEndpoint<T>()`" and proposed closing open endpoints **in the
> application**. The application's generated code is the one place where the open endpoint and its type
> argument are both known. A prototype (`C:\Repos\WebApplication9`: `Level1.Libraries.cs`,
> `Level2.Generated.cs`, `Program.cs`) builds and serves all three closed endpoints with 200. The owner
> left the open questions to Claude; the decisions:
>
> **D1 — The attribute** (Abstractions, public):
> ```csharp
> [assembly: EndpointTypeArgument<Spark.SparkUser, AppUser>]              // primary: keyed on a constraint type
> [assembly: EndpointTypeArgument(typeof(Spark.Echo<>), typeof(string))]   // explicit: per open endpoint
> ```
> `EndpointTypeArgumentAttribute<TConstraint, TArgument> where TArgument : TConstraint`, so a wrong
> argument is a compiler error (measured: `<SparkUser, string>` → CS0311). The sketched
> `EndpointTypeArgument<TEndpoint<TArg1>, TArg1>` is not expressible (no higher-kinded generics;
> `Echo` without arguments is CS0305).
>
> **D2 — Binding rules.** A type parameter is bound by a constraint-keyed attribute when one of its
> constraint types **equals** `TConstraint`. An endpoint is closed only when **every** type parameter,
> including those of containing types, is bound. The explicit form binds one open endpoint and wins over
> constraint keys for that endpoint. Violated constraints (`new()`, `class`, `struct`, `unmanaged`,
> `notnull`, other constraint types), two attributes matching one parameter, and explicit-form arity
> mismatches are **errors on the attribute**. They are never emitted as uncompilable code.
>
> **D3 — Discovery.** Every assembly's generator stamps a marker attribute on its assembly when it declares
> open endpoints. The application's generator reads only marked references, and only when the compilation
> declares at least one `EndpointTypeArgument`. Reads are memoised per `MetadataReference`, as in M9. Open
> endpoints in the application's own compilation are closable too. Library endpoints, and the groups on
> their chain, must be `public`; an inaccessible one is a diagnostic.
>
> **D4 — A closed endpoint is an ordinary endpoint.** It gets mapping, descriptor, typed link, contract,
> OpenAPI hooks and duplicate route/name checks like a hand-written one. Its name, and everything derived
> from it (`WithName`, `operationId`, link method), is `{Name}_{TypeArgumentNames}`, e.g.
> `Passkeys_AppUser`. The runtime's `EndpointNameOf` uses the same rule for the manual path, so two
> closings never collide.
>
> **D5 — Conditional mapping** (Spark maps passkey and linking routes only when enabled):
> `IEndpointGroup` gains `static virtual bool IsEnabled(IServiceProvider services) => true`. Generated
> mapping wraps a group, and everything nested in it, in `if (G.IsEnabled(app.ServiceProvider))`,
> evaluated once at map time. This applies to all groups, generic or not. The condition is group-level
> only: an endpoint that needs its own condition goes in its own group.
>
> **D6 — In the assembly that declares it,** an open endpoint is not mapped (R2 stands there), and its
> partial repeats its type parameters (R3). An Info diagnostic says it is closed by an application with
> `[assembly: EndpointTypeArgument<…>]`. An application attribute that closes nothing gets a warning.
>
> **D7 — Also fixed, since they came up:** closed generic groups (`[MemberOf<Api<string>>]`) are keyed by
> their closed type, so they keep their links and contracts and MPEP016 stops misfiring. A derived
> endpoint's `new static Path` no longer diverges from the runtime route: the generator resolves `Path`
> through the interface implementation the runtime uses, and a Warning says the `new` member is ignored
> unless the interface is listed again.
>
> **D8 — Option B** (the declaring library generates its own generic `Map…<TUser>()`) is **not done**.
> D1–D5 give the application a one-line closing and keep conditions in the library, which B could not.
>
> **D9 — Version:** proposed **`11.2.0-rc.0`**, a new feature on top of `11.1.0-rc.0`. The release stays
> the owner's decision.

> **D3a — What the library marker must carry (found while mapping the generator, 2026-09-25).** A symbol
> constructed from the application's own source hands back its original definition's syntax, so
> everything recovers as usual. A symbol from a **referenced library** has no syntax: `Path` and group
> `Prefix` (read by `RouteLiteral` from syntax) come back null, which would silently drop links, contracts
> and route checks. `IsPartial` comes back false, so bound properties lose their types. Library groups
> are not discovered at all. So the library generator emits, per open endpoint, an assembly-level record
> carrying: the open type (`typeof(Passkeys<>)`), its `Path` literal, a custom `Methods` literal, whether it
> has a generated binder, and for each group on its chain the group type, `Prefix` and parent. The
> application's generator reads the records (the M9 reference loop and `ConditionalWeakTable` memo, strings
> only), resolves and `Construct`s the type per compilation, and never runs per-declaration diagnostics
> (MPEP001/002/011/013/014/019/020) on a closed endpoint: those belong to the declaring assembly. Closing
> diagnostics are located on the application's `EndpointTypeArgument` attribute. "Accessible" for a library
> endpoint means `public`, not `internal`.
>
> Also from the mapping:
> - `new static Path`: `RouteLiteral` takes the nearest `Path`, while the runtime uses the interface map.
>   `FindImplementationForInterfaceMember(IEndpointBase.Path)` gives the runtime's choice, and a
>   difference between the two is the D7 warning.
> - A static virtual `IsEnabled` cannot be called as `G.IsEnabled(...)` on a class that does not declare it,
>   so the generated code uses an `IsEnabled<TGroup>()` helper, like the existing `Prefix<TGroup>()`.
> - The static `Endpoints` descriptor list cannot honour `IsEnabled`; it lists what is declared.

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

> **Added with D1–D9:**
> 7. A test library project declares `Passkeys<TUser> where TUser : LibUser, new()` and friends. The
>    TestApp references it and writes one `[assembly: EndpointTypeArgument<LibUser, AppUser>]`.
>    `MapTestAppEndpoints()` then maps the closed endpoints, and they answer end to end with route binding,
>    names `…_AppUser`, typed links, contracts and a valid OpenAPI entry (path parameters included).
> 8. A group whose `IsEnabled` returns false maps none of its endpoints or nested groups (runtime test,
>    both states).
> 9. Every D2 error has a generator test, and none of them produces a compile error in generated code.
> 10. The 12 investigation red tests are green, with their expectations updated to D6.
> 11. The README's "Generic endpoints" section compiles, like every other block.

> **As built (2026-09-25, Phases 2–4).** D1–D9 and D3a are implemented as decided. Decisions the PRD left
> open, and corrections:
> - **The records are public Abstractions attributes**, `[assembly: OpenEndpoint(typeof(X<>), Path = …,
>   Methods = …, HasBinder = …, Version = 1)]` and `[assembly: OpenEndpointGroup(typeof(G), Prefix = …)]`
>   (`[EditorBrowsable(Never)]`), not an internal attribute generated per assembly like the M9 contract:
>   the application references Abstractions anyway, since it writes `EndpointTypeArgument`. Their presence
>   is the D3 marker. They are written at the top of `EndpointMapping.g.cs`, only for open endpoints
>   generated code can name, and only when the compilation resolves `OpenEndpointAttribute` (an older
>   Abstractions gets no records rather than a compile error). When an assembly has any open endpoint,
>   every nameable declared group is recorded, not only those on an open chain: a chain can pass through
>   a construction (`Api<string>`) whose declaration is the open `Api<T>`, and prefixes are looked up by
>   definition. A record with a newer `Version` is ignored.
> - **Diagnostics.** MPEP025 Info (open endpoint not mapped here; suppressed when the same compilation
>   closes it), MPEP026 Error (constraint violated; one per closing, the first violation, checked in the
>   order `class`, `unmanaged`, `struct`, `notnull`, `new()`, constraint types), MPEP027 Error (two
>   attributes bind one parameter), MPEP028 Error (explicit arity), MPEP029 **Warning** (endpoint, group on
>   its chain or type argument cannot be named by the application — a warning, not an error, because an
>   application cannot fix a library's internal endpoint except by dropping the attribute), MPEP030 Warning
>   (attribute closes nothing), **MPEP031 Warning (new: some type parameters bound, others not)**, MPEP032
>   Warning (`new static Path` ignored). MPEP026–MPEP031 are located on the application's attribute.
>   An attribute whose type arguments do not resolve is ignored (its own CS0246 already fires; the IDE
>   can briefly lack references).
> - **Closed endpoints in the route checks.** MPEP007 and MPEP012 run on them (D4) and name them by
>   their closed name (`Echo_String`), since closings share a class name; MPEP009 and MPEP010 are
>   declaration checks and are skipped for them, like the other per-declaration diagnostics.
> - **The name rule (D4)**, identical in `GenericTypes.NameSuffix` and the runtime's `EndpointNameOf`:
>   `{Name}` plus `_{Argument}` for every type argument, outermost containing type first, each by its CLR
>   name without arity; a generic argument is followed by its own arguments (`Echo_List_Int32`), an array
>   by `Array` (`Echo_Int32Array`). `[EndpointDescriptorName("x")]` keeps the suffix (`x_String`).
> - **Open groups** (a group with type parameters of its own or of a container) are left out of the plan;
>   the constructions endpoints join are described by those endpoints (D7) or by the closing step (a
>   library's groups, from the records). A group joined only by open endpoints is not MPEP016 in the
>   declaring assembly. MPEP024 for a group is now reported from the declarations, so an inaccessible open
>   group still gets it.
> - **`Prefix` too** is resolved through the interface implementation, like `Path` (D7), without a
>   warning of its own. `Methods` is not: it still takes the nearest declaration (unchanged; the same
>   divergence is possible there and is not addressed).
> - **IsEnabled (D5)** wraps every group block, which changes the generated text of every grouped
>   endpoint (`{` becomes `if (IsEnabled<G>(app.ServiceProvider)) {`), not its behaviour. The manual path
>   checks every group on the chain before creating any, and maps nothing when one is disabled.
> - **Acceptance 2** holds for the mapping, contract and route output of the non-generic endpoints; the
>   mapping file additionally carries the open class's records (D3a). A test strips the record lines and
>   compares the rest byte for byte.
> - **Acceptance 4**, the OpenAPI half: the manual-path test in Tools.Tests asserts distinct endpoint
>   names and both routes answering, not the document, because calling `AddOpenApi()` there switches on
>   the OpenAPI package's XML-comment interceptors (CS9137). Closed endpoints' `operationId`s are asserted
>   on the TestApp's document instead.
> - **Test library shape.** `MintPlayer.AspNetCore.Endpoints.TestLibrary` answers with BCL types only
>   (`string`), so the TestApp's typed client (which does not reference the library) resolves every
>   contract without MPEP021.
> - **Order of work.** The Abstractions API (attributes, `IsEnabled`) was added before the red run, so the
>   red tests fail on behaviour rather than on unresolved attribute types. Red: 35 of 360 generator tests
>   (the 11 investigation tests plus all 24 new ones); the TestLibrary did not build (CS0246, CS0534).

## Version

Created 2026-09-25 from issue #34. Branch `fix/endpoints-open-generics` (from `master` at `c04ffac`).
