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

> **Closed after the build (2026-09-25): `Methods` resolution and generic constraint types.**
> - **`Methods` resolves like `Path`** (supersedes the "`Methods` is not" sentence under "`Prefix` too"
>   above). `MethodsLiteral` takes the implementation of `IEndpointBase.Methods`
>   (`FindImplementationForInterfaceMember`), which is what `TEndpoint.Methods` dispatches to. An
>   implementation that is a verb interface's own default (`IGetEndpoint`'s explicit
>   `IEndpointBase.Methods`) stands for that verb; one on a class is read as before (a literal, or
>   unknown). The nearest declaration is only the fallback. So a derived `new static Methods` that does
>   not re-implement the interface no longer changes MPEP007's verb comparison, the contract's methods
>   or the recorded `Methods` of an open endpoint: they say what the runtime maps.
> - **MPEP032 covers both members** rather than a new ID for `Methods`: the cause (a `new static` member
>   does not re-implement the interface), the effect (the runtime keeps the base's value) and the fix
>   (list the interface again, or remove the member) are the same, so one ID is one thing to learn and
>   to suppress. The message names the member and the value in force (`'/base'`, or `GET, HEAD`); a
>   class hiding both gets two MPEP032s, one per member. Title and message were generalised.
> - **Generic constraint types (D2 made precise).** Binding stays constraint *equality*. A closed
>   generic constraint is an ordinary key: `Profile<TUser> where TUser : IUser<Guid>` is bound by
>   `EndpointTypeArgument<IUser<Guid>, AppUser>`, from the same compilation and from a reference (it
>   already worked; now tested). A constraint that mentions another type parameter
>   (`Passkeys<TUser, TKey> where TUser : IUser<TKey>`) equals no key, and `TKey` is **not inferred** by
>   unifying `IUser<TKey>` with the key's `IUser<Guid>`: a key would then bind a parameter it does not
>   name, and a second key or constraint on `TKey` would make the rule order-dependent. Before this, a
>   key on the same generic type was silently ignored for such an endpoint (and, alone, reported only as
>   MPEP030's misleading "no open endpoint has a type parameter constrained to …"). Now it is **MPEP033,
>   Warning, on the attribute**: it names the parameter, its constraint, the parameters that constraint
>   depends on and the explicit form to write (`typeof(Lib.Passkeys<,>)`). The attribute counts as used
>   (no MPEP030), and MPEP031 is not reported on top, since the explicit form fixes both. Warning, like
>   MPEP030/MPEP031, because nothing wrong is emitted, only nothing. MPEP033 applies only to keys on the
>   same generic definition as a dependent constraint, and only when no explicit form targets the
>   endpoint (the explicit form wins, so the usual pairing, a key for `Profile` plus the explicit form
>   for `Passkeys`, is silent).
> - **The explicit form closes such an endpoint**:
>   `EndpointTypeArgument(typeof(Passkeys<,>), typeof(AppUser), typeof(Guid))` maps
>   `Passkeys<AppUser, Guid>` as `Passkeys_AppUser_Guid`, with its binder, link and contract. Every
>   constraint is checked after substitution, so `IntUser : IUser<int>` there is MPEP026 naming
>   `IUser<System.Guid>`. Both already worked; now tested, same compilation and cross-assembly.
> - **Tests:** 12 new generator test cases in `EndpointTypeArgumentTests` (1 for `Methods`, 11 for
>   constraint types). Red before the fix: the `Methods` test (contract said `POST` for an endpoint the
>   runtime maps as `GET`) and both MPEP033 cases (no diagnostic); the others passed at once. The README's
>   new block was compiled with the other blocks in the scratch server, as before.

> **Dependency update (2026-09-25, owner's instruction), and the compiler floor it sets.**
> - **Versions:** `MintPlayer.SourceGenerators.Tools` 10.16.0 → 11.0.0 (Generator, runtime package
>   with `ExcludeAssets="all"`, generator tests); `MintPlayer.SourceGenerators` and `.Attributes`
>   10.13.0 → 11.0.0 (MustChangePassword, SitemapXml); `Microsoft.CodeAnalysis.CSharp` /
>   `.Workspaces.Common` (CodeFixes) and `.CSharp.Workspaces` (generator tests) 4.14.0 → 5.9.0;
>   `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` 1.1.2 → 1.1.4; `Microsoft.NET.Test.Sdk` 18.9.0 →
>   18.10.1; `Microsoft.AspNetCore.Mvc.Testing` (net10.0) 10.0.11 → 10.0.12.
> - **Owner decision: Roslyn 5.9 is the minimum; older Roslyn versions are not supported.** Tools
>   11.0.0 binds `Microsoft.CodeAnalysis 5.9.0`, and so does the generator it ships beside. The
>   requirement is **.NET SDK 10.0.400+ or 11.x, or Visual Studio 2026 (with Roslyn 5.9+)**, stated in
>   both package READMEs. No multi-targeted analyzer folders, fallbacks or shims. Measured with a local
>   Release pack and a scratch `net10.0` web consumer (private `globalPackagesFolder`, SDK pinned by a
>   scratch `global.json`): SDK 10.0.401 (Roslyn 5.9.0) builds clean. SDK 10.0.112 (Roslyn 5.0.0) does
>   not run the generator:
>   `CSC : warning CS9057: Analyzer assembly '…\MintPlayer.AspNetCore.Endpoints.Generator.dll' cannot be
>   used because it references version '5.9.0.0' of the compiler, which is newer than the currently
>   running version '5.0.0.0'.` followed by `Program.cs(4,5): error CS1061: 'WebApplication' does not
>   contain a definition for 'MapConsumerEndpoints' and no accessible extension method
>   'MapConsumerEndpoints' accepting a first argument of type 'WebApplication' could be found`. CI is
>   unaffected: `global.json` selects 11.0.100-rc.1 (Roslyn 5.11) for every build, and the `10.0.x`
>   that setup-dotnet also installs, for the net10.0 test runtime, resolves to the newest 10.0 SDK
>   (10.0.4xx, Roslyn 5.9).
> - **`ProduceCode` and `IConditionalDiagnosticReporter` adopted.** The generator avoided
>   `ProduceCode` because in Tools 10.x it combined every producer with `CompilationProvider`. In 11.0.0
>   it registers one output per provider with no compilation, and `Producer.Produce` reports a throwing
>   producer as `MPSG001` instead of swallowing it — the two reasons for the hand-rolled
>   `EndpointsProducer.Emit`, which is deleted. `EndpointDiagnosticReporter` implements
>   `HasDiagnostics` by running its own checks without a compilation (locations left null), so a
>   project with nothing to report keeps the reporter out of the per-compilation combine. Every
>   existing Cached/Unchanged assertion holds, and a new test,
>   `EditingAHandlerBody_InADiagnosticFreeProject_RunsNoOutputStep`, asserts on the raw output steps,
>   which were not assertable before: after a handler-body edit there are exactly the four file
>   outputs, each with a Cached/Unchanged input and output. Negative control: with the reporter
>   declared as a plain `IDiagnosticReporter` the test fails (5 output steps; the reporter step is
>   combined with the new compilation). Its own output reason alone cannot show this: a reporter that
>   re-runs and again reports nothing yields an equal, empty output and is marked Unchanged.
> - **Packaging:** the Tools 11 props add their copies under `analyzers/dotnet/roslyn5.9/cs` (was
>   `roslyn4.0`/`roslyn4.9`); `SuppressToolsAnalyzerCopies` matches any `analyzers/` path, so both
>   packages still ship exactly one folder, `analyzers/dotnet/cs`, with the Generator, CodeFixes and
>   Tools dlls. No Tools API used here is `[Obsolete]` in 11.0.0 (the only one, the two-argument
>   `Producer.Produce`, was not called); the Release rebuild has no warnings from Endpoints.
> - **Verification:** generator tests 375 per TFM (374 + the new one), runtime tests 1085 per TFM;
>   rebuild warnings unchanged at 36 CS1591, all in MustChangePassword/SitemapXml generated code.

## Addendum — Tools 12 and generated model equality (MintPlayer.Dotnet.Tools #184 / #185)

*(Added 2026-09-25. Same PR: #35 is still open, and the one-PR rule applies.)*

### Background

`ClosedGenericInfo`, a model added for #34, had to choose between hand-written `IEquatable<T>` and
`[AutoValueComparer]`. Only `IEquatable<T>` is safe at every Roslyn step: Roslyn compares pipeline
outputs with `EqualityComparer<T>.Default`, and a generated comparer is a separate object. That led to
MintPlayer/MintPlayer.Dotnet.Tools#184, and #185 (merged 2026-09-25) shipped the fix as
`MintPlayer.SourceGenerators.Tools`, `MintPlayer.ValueComparerGenerator` and
`MintPlayer.ValueComparerGenerator.Attributes` **12.0.0**:
- `[GenerateEquality]` generates `IEquatable<T>`, `Equals` and `GetHashCode` on the partial model itself.
- The value-comparer runtime (`ValueComparer<T>`, `ComparerRegistry`, the generated `.WithComparer()`
  extensions, `ICompilationCache`) is deleted.
- `LocationKey`, `PathSpec` and `PathSpecElement` implement `IEquatable<T>`.

#185 proposed six downstream steps for this repo:
1. Bump Tools and add the two ValueComparerGenerator packages.
2. Convert the 14 hand-written models.
3. Delete `SequenceComparer<T>` and the 3 `.WithComparer()` calls.
4. Delete `LocationKeys.AreEqual`/`PathSpecs.AreEqual`.
5. Drop `ICompilationCache`.
6. Pack the attributes dll in `GetDependencyTargetPaths` and guard it.

### Investigation (three agents, 2026-09-25)

Three agents looked into it:
- **U** read #185's diff and the 12.0.0 packages.
- **D** mapped this repo read-only.
- **S** did the migration end to end in a worktree. Its patch is kept at
  `scratchpad\spike185\spike.patch`.

Findings:

- **Tools 12 breaks exactly three things here** (S, verbatim):
  - `CS0534 'EndpointGenerator' does not implement inherited abstract member
    'IncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext, IncrementalValueProvider<Settings>)'`
  - `CS0246 … 'ICompilationCache'`
  - `CS0234 … 'ValueComparers' does not exist in the namespace 'MintPlayer.SourceGenerators.Tools'`

  `Producer`, `ProduceCode`, `IDiagnosticReporter`/`IConditionalDiagnosticReporter`, `LocationKey`,
  `PathSpec`, `GetPathSpec`, `OpenPathSpec` and `GetAllBaseTypes` are unchanged. The client generator
  (a plain `IIncrementalGenerator`) is unaffected.
- **13 of the 14 models already compare every stored member** (D). **`ClientServer` deliberately
  excludes `IsCacheable`,** which drives only the reference-read memo, so a generated `Equals` would
  change its meaning.
- **`[GenerateEquality]` compares computed get-only properties too:** `EndpointInfo.EffectiveDescriptorName`,
  `ClientServer.IsEmpty`, and `AssemblyInfo.MethodNameWasSanitised`/`RequestedMethodName`. The last two
  would re-run the method-name sanitising on every comparison. These are derived values, so equality is
  unchanged, but it costs work.
- **The helpers are equivalent:**
  - `ValueEquality.ImmutableArray` treats `default` as equal only to `default` and hashes it to 0,
    exactly as `SequenceComparer` did.
  - Tools 12's `LocationKey.Equals` is the same as `LocationKeys.AreEqual`.
  - `PathSpec.Equals` additionally compares the derived `AllPartial` and each element's
    `GenericTypeParameters`, which the old comparer missed. That is stricter and correct.
- **`.WithComparer()`:** `.WithComparer()` after `Collect()` can simply be deleted. S measured that all
  16 incremental tests and the client cache test stay green. The step that *builds* a new
  `ImmutableArray<string>` (`openEndpointNamesProvider`) must return `EquatableArray<T>`
  (`.ToEquatableArray()`); otherwise it and the closing step after it re-run on every edit.
- **Two tests pin the old one-field hash codes and fail:**
  - `EndpointInfoTests.GetHashCode_IsDerivedFromTheFullyQualifiedNameOnly`
  - `AssemblyInfoTests.GetHashCode_IsDerivedFromTheAssemblyNameOnly`

  The first test's comment claims a `GroupBy` depends on that hash. It does not: every `GroupBy` keys
  on the name string. Nothing hashes models into collections. All other tests pass (373/375 on
  net10.0).
- **Packaging needs *less* than #185 proposed.** `MintPlayer.ValueComparerGenerator`'s own
  `build/*.targets` already adds the attributes dll to `GetTargetPath`, and both of this repo's pack
  targets read `GetTargetPath`. So the dll lands in `analyzers/dotnet/cs` next to the Generator,
  CodeFixes and Tools dlls with no new line, and a second line would duplicate it (NU5118 risk). The
  package's `roslyn5.9/cs` copy is already removed by `SuppressToolsAnalyzerCopies`, and the
  stray-folder guard stays quiet.
- **Two things the proposal did not foresee** (S):
  1. The package's target adds the attributes dll with `IncludeRuntimeDependency="true"`. That breaks
     the test project's plain `ProjectReference` with `MSB4018: The "GenerateDepsFile" task failed
     unexpectedly … An item with the same key has already been added`. A local target setting it to
     `false` fixes it.
  2. The ValueComparerGenerator emits an empty **public** class
     `Microsoft.CodeAnalysis.IncrementalValueProviderAdditionalEx` (`JoinMethods.g.cs`) into our
     generator dll. That adds one CS1591, making 37 against the 36 baseline.
- **Is the attributes dll needed at load time? S measured no.** A scratch consumer built against packages
  *without* the dll loads and runs the generator: no CS8032/CS8784, and the same files are emitted, also
  with the shared compiler off. The attribute is metadata on internal types that the analyzer loader
  never resolves. This contradicts the load-time claim in Dotnet.Tools' `valuecomparergenerator.targets`.
  IDE hosts that reflect over attributes were not tested.
- **Roslyn floor:** every 12.0.0 package targets Microsoft.CodeAnalysis **5.9.0**, and the attributes dll
  references only netstandard, so the owner's Roslyn 5.9 floor holds.

### Decisions

- **D10 — Adopt Tools 12 and `[GenerateEquality]` in this PR.** All 14 models become
  `[GenerateEquality] internal sealed partial class`, and every hand-written `Equals(T)`,
  `Equals(object)` and `GetHashCode()` is deleted. Keeping any of them suppresses generation (MINT002),
  and keeping half of the object pair is a warning.
- **D11 — `[EqualityIgnore]`:**
  - on `ClientServer.IsCacheable`, to preserve today's semantics;
  - on the four computed properties, to keep the work out of every comparison.

  Any other difference from today's equality is a bug.
- **D12 — Pipeline comparers:**
  - Delete `SequenceComparer<T>` and the two `.WithComparer()` calls after `Collect()`.
  - `openEndpointNamesProvider` returns `EquatableArray<string>`.
  - `EndpointClosing.Build` keeps its `ImmutableArray` signature, and the call site unwraps with
    `AsImmutableArray()`.
- **D13 — Delete `LocationKeys.AreEqual` and `PathSpecs.AreEqual`;** Tools 12's `IEquatable<T>`
  replaces them. Drop the `ICompilationCache` parameter and the `using …ValueComparers`.
- **D14 — Ship the attributes dll** (6.6 KB) even though S showed consumers load without it:
  - Upstream ships it the same way.
  - An IDE host was not tested.
  - Removing it later is cheaper than debugging a missing-assembly load failure in a user's IDE.

  It arrives through the package's own target, so this repo adds **no** `GetDependencyTargetPaths` line
  (that corrects #185's step 6). It gets pack guards like the code-fix dll:
  - in both packages' pack targets, present and only in `$(EndpointsAnalyzerPackPath)`;
  - an error if `$(PkgMintPlayer_ValueComparerGenerator_Attributes)` is empty. The attributes package
    reference gets an explicit `GeneratePathProperty="true"`; its own props' `Update` runs too early.
- **D15 — The `IncludeRuntimeDependency` workaround** is a named target with a comment that cites the
  MSB4018. The upstream defect is filed as an issue on MintPlayer.Dotnet.Tools.
  *Resolved upstream in 12.1.0 (MintPlayer.Dotnet.Tools#187/#188): the package's own target now sets
  `IncludeRuntimeDependency="false"`, so the workaround target is removed.*
- **D16 — The warning baseline must not grow.** The empty public `IncrementalValueProviderAdditionalEx`
  is an upstream defect: a generator should not add public API to its consumer. It is filed upstream.
  *Resolved upstream in 12.1.0 (MintPlayer.Dotnet.Tools#187/#188): `JoinMethods.g.cs` is no longer emitted
  without `[GenerateJoinMethods(n >= 6)]`, is `internal` when it is, and no generated member raises CS1591.
  The `BuildSuppressions` project and its `DiagnosticSuppressor` are removed.*
  - If the generated class is `partial`, this repo documents it with a one-line partial declaration,
    which is honest and removable.
  - Otherwise the CS1591 is suppressed for that type only, by a targeted `#pragma` in a partial or an
    `.editorconfig` rule scoped to generated code. Never project-wide.
- **D17 — Tests:**
  - The two hash-pinning tests are rewritten to assert the contract the model actually needs: equal
    models have equal hashes, and a changed member changes `Equals`.
  - A new test pins `ClientServer` ignoring `IsCacheable`.
  - The incremental and cache tests are the migration's proof; none may change its expectations.
- **D18 — Docs and comments** that describe the hand-written equality or its reasons are updated:
  - `Models.cs`, around the old `SequenceComparer`, `PathSpecs` and `LocationKeys` remarks;
  - `EndpointGenerator.cs` and `EndpointsProducer.cs` ("Tools 11.0.0");
  - the CodeFixes csproj comment;
  - the test comments;
  - `PRD-EndpointsErgonomics.md` R6.7 and `PRD-TestCoverage.md` ("IEquatable ceremony"), with
    appended blockquotes, never rewritten.

### Acceptance criteria (addendum)

12. The generator builds against Tools 12.0.0. `dotnet list package --outdated` reports nothing.
13. No hand-written `IEquatable<T>`, `SequenceComparer`, `LocationKeys`/`PathSpecs` helper or
    `.WithComparer()` remains in the generator.
14. All incremental and cache tests are green with unchanged expectations. The rewritten hash tests and
    the `IsCacheable` test are green on net10.0 and net11.0.
15. Both nupkgs carry exactly one analyzer folder:
    `analyzers/dotnet/cs/{Generator, Generator.CodeFixes, SourceGenerators.Tools, ValueComparerGenerator.Attributes}.dll`.
    The new pack guards fail when the attributes dll is removed or misplaced (triggered deliberately).
16. Solution `-t:Rebuild -c Release` stays at the 36 CS1591 baseline.
17. Two upstream issues are filed on MintPlayer.Dotnet.Tools: the `IncludeRuntimeDependency` MSB4018,
    and the public `JoinMethods.g.cs` type. A third item goes in the same issue or its own: the
    load-time claim contradicted by S's measurement.

> **As built (Phase 5, 2026-09-25, committed as `d1ccf1e`):**
> - **12.0.1 throughout** (D26): Tools in all three references, ValueComparerGenerator(.Attributes) in the
>   generator, MintPlayer.SourceGenerators(.Attributes) in MustChangePassword and SitemapXml. The spike patch
>   applied cleanly; its `SpikeDropVcAttributes` switch is gone.
> - **Models, comparers, tests** as D10–D13, D17: 14 `[GenerateEquality]` partials, `[EqualityIgnore]` on
>   the five members of D11, no `SequenceComparer`/`LocationKeys`/`PathSpecs`/`.WithComparer()`, and
>   `openEndpointNamesProvider` returns `EquatableArray<string>`. The two hash tests assert
>   equal-models-equal-hashes; `ClientServer_Equality_IgnoresIsCacheable` is new. No incremental or cache
>   test changed its expectations.
> - **Deviation from D16:** the generated class is not partial, and neither lighter mechanism works. Measured:
>   `[assembly: SuppressMessage(…, Target = "~T:…IncrementalValueProviderAdditionalEx")]` (with and without
>   `Scope = "type"`) does not suppress a compiler warning, and an `.editorconfig` severity is not applied to a
>   source-generated tree even under `[*]`. The CS1591 is therefore suppressed by a `DiagnosticSuppressor`
>   that matches that one type: a new non-packable project,
>   `Endpoints/MintPlayer.AspNetCore.Endpoints.Generator.BuildSuppressions`, referenced by the generator as an
>   analyzer (`ReferenceOutputAssembly="false"`, `PrivateAssets="all"`), never shipped. Baseline: 36 CS1591
>   (16 MustChangePassword, 20 SitemapXml).
> - **Packaging** (D14, D15): the named `DemoteValueComparerAttributesRuntimeDependency` target cites the
>   MSB4018; `CheckValueComparerAttributesPathProperty` errors on an empty
>   `$(PkgMintPlayer_ValueComparerGenerator_Attributes)`; both pack targets guard presence and placement of the
>   attributes dll. Each guard was triggered once deliberately (removed from `GetTargetPath`, misplaced into
>   `analyzers/dotnet/cs` in each package, `GeneratePathProperty` dropped): each fails the pack with its message.
> - **Not done here:** the upstream issues of acceptance 17 (the caller files them).

> **Superseded by 12.1.0 (commit `1fa1b75`, 2026-09-26).** The upstream defects were filed as
> MintPlayer/MintPlayer.Dotnet.Tools#187 and fixed there in #188, released as **12.1.0**. The package's own
> target now uses `IncludeRuntimeDependency="false"`, and no MintPlayer generator emits undocumented public
> code any more. So:
> - The `DemoteValueComparerAttributesRuntimeDependency` target (D15) is removed.
> - The `…Generator.BuildSuppressions` project with its `DiagnosticSuppressor` (the D16 deviation above) is
>   removed, and dropped from the solution.
> - All MintPlayer packages are at 12.1.0: Tools in all three references, ValueComparerGenerator(.Attributes)
>   in the generator, MintPlayer.SourceGenerators(.Attributes) in MustChangePassword and SitemapXml.
>   `dotnet list package --outdated` reports no updates for all 21 projects.
> - **The warning baseline is now 0, not 36.** The 36 CS1591 (16 MustChangePassword, 20 SitemapXml) came from
>   the same upstream-generated code. Measured 2026-09-26: `dotnet build MintPlayer.AspNetCore.Tools.sln -c
>   Release -t:Rebuild` gives `0 Warning(s)`, `0 Error(s)`. Acceptance 16 and every "36 CS1591 baseline" in this
>   PRD and the PLAN read as 0 from here on.
> - **Unchanged and still required:**
>   - `CheckValueComparerAttributesPathProperty` and the pack guards on the attributes dll's presence and
>     placement stay, because the dll still ships beside the generator (D14).
>   - The analyzer folder stays `analyzers/dotnet/roslyn5.9/cs` (D25).
> - PR #35's check passed on `1fa1b75`.

## Addendum 2 — Generator performance, following MintPlayer.Dotnet.Tools #183 / #185 / #186

*(Added 2026-09-25. Same PR #35.)* #186, published as 12.0.1, made the equality generator write one fixed
`GeneratedEquality.g.cs` instead of a file per model, and added `FixedFileSetGuardTests`. Together with
#183 (caching) and #185 (generated equality), it sets the rules Dotnet.Tools' generators now follow. The
owner asked for this repo's generators to be checked against them. Three agents did this: **U** built
an upstream checklist, **A** audited this repo read-only, and **B** benchmarked it in a worktree. B's
benchmark source is `scratchpad\bench\ZzGeneratorBenchmark.cs` and its logs are `bench\final.log` and
`bench\breakdown.log`.

### What the upstream rules require (U), and where this repo stands (A, B)

| # | Upstream rule | This repo |
|---|---|---|
| 1 | Outputs through `ProduceCode`, with no `Compilation` in any output step | Done (commit `7e280e6`) |
| 2 | `IConditionalDiagnosticReporter`, diagnostics computed without the compilation | Done. B confirmed the reporter does not run on unrelated edits |
| 3 | Models value-equal under the default comparer, via `[GenerateEquality]` | Planned: Phase 5, D10–D18 |
| 4 | A step that builds a collection returns `EquatableArray<T>` | Planned: D12 (`openEndpointNamesProvider`) |
| 5 | No Roslyn objects in models (MINT001/MINT005) | Clean (A) |
| 6 | A model carries a location only when it reports a diagnostic | **Not met.** `EndpointInfo`, `BoundProperty` and `GroupInfo` carry `LocationKey`s into the producers' model, so a line shift above an endpoint re-runs all four producers (A) |
| 7 | A fixed set of output files, never a hint name derived from the input | **Server met:** 4 constant names, the same at N=10/100/500 (B). **Client not met:** one `<ServerName>Client.g.cs` per referenced server, unbounded in length and renamed when the server is (A). Dotnet.Tools' own PRD names this exception |
| 8 | Caching proven per output step over real keystrokes, with a coverage guard | Partly: 16 incremental tests and the client cache test. There is no file-set guard, no line-shift test, and the `openEndpointNames` step is untracked |
| 9 | Robust producers (cancellation, MPSG001) | Inherited from `Producer` |
| 10 | Cheap discovery (`ForAttributeWithMetadataName`; heavy work after filtering) | Endpoints are found by interface, so `CreateSyntaxProvider` is required. The broad predicate costs **nothing measurable** (B, S3). **The transform's semantic work is the hotspot** (see below) |
| 11 | Settings from the parse/config options, never from walking the syntax trees | Inherited |
| 12 | Deterministic output, generated code verified to compile | Met |
| 13 | CI allocation benchmark gate (head against base, allocations only) | Not present |
| 14 | Package the generator in the **versioned** `analyzers/dotnet/roslyn5.9/cs` folder, never the unversioned `analyzers/dotnet/cs` (an older host would load it and fail with CS8032) | **Conflicts** with this repo's deliberate choice of the unversioned folder, made so that an older SDK *names* the problem (CS9057) instead of silently skipping the generator |

### Measurements (B; Release, net10.0, median of 9 runs after warm-up)

| Scenario | N=10 | N=100 | N=500 |
|---|---|---|---|
| Cold | 5.0 ms | 23.8 ms | ≈130–230 ms, 33 MB |
| Method-body edit | 5.0 ms | 17.8 ms | 80.6 ms, 16.8 MB |
| Unrelated class with a base list added | 4.6 ms | 15.7 ms | 74.8 ms |
| One `Path` literal changed | 5.0 ms | 23.7 ms | 105.5 ms, 32.8 MB |
| Client cold / unrelated-edit rerun | 1.8 / 0.59 ms | 3.5 / 0.62 ms | 18.2 / 0.70 ms |

- **Unrelated edits:** every step downstream of the syntax providers is Cached or Unchanged, and every
  output is skipped.
- **Hotspot: the per-endpoint `Endpoints` transform re-runs for every endpoint on every edit.** A new
  `Compilation` re-runs a semantic `CreateSyntaxProvider` transform on every node; value equality then
  saves the model, but not the time.
  - It is 85–90% of every rerun: about 0.14 ms per endpoint, 69 of 81 ms at N=500.
  - The per-component breakdown (first pass / warm, over 500 endpoints):
    - `RouteLiteral.ReadImplementation` 88–96 / 19–22 ms. Most of it is `FindImplementationForInterfaceMember`
      (45 ms) plus a new `GetSemanticModel` + `GetConstantValue` per endpoint (31 ms). A syntactic literal
      read takes 2.2 ms.
    - `MethodsLiteral.Read` 27–40 ms.
    - `BoundProperties.Collect` 20–27 ms.
    - `ConstructedGroupChain` 12–23 ms.
    - Everything else ≤15 ms.
- **Output volume:** 1.45 MB at N=500 (about 2.9 KB per endpoint; `EndpointMapping.g.cs` 783 KB,
  `EndpointRoutes.g.cs` 427 KB). The client's `ApiClient.g.cs` is 884 KB. Parsing the server output alone
  takes 64 ms, which the consumer's compile pays.

### Also found (A)

- **Probable bug: `IsGroupCandidate` requires `IEndpointGroup` directly in the base list.** So
  `class ApiGroup : MyGroupBase` (inheriting it) is never discovered. The endpoint predicate had this blind
  spot removed; the group predicate did not. No test covers it. Not yet reproduced.
- **`EndpointMappingPlan.From` runs up to six times per model change:** once in each of the four
  producers, plus twice in the reporter. `ShadowParameters.For` runs twice per endpoint. Each run is
  linear, so the waste is small but pure.

### Decisions

- **D19 — Endpoint transform fast paths** (the hotspot, which grows linearly with every endpoint on every
  keystroke):
  - Read `Path`/`Prefix`/`Methods` **syntactically first**, when the class itself declares an
    expression-bodied or getter-returning literal or constant-concatenation.
  - Otherwise use the transform's own `SemanticModel` (`GeneratorSyntaxContext.SemanticModel`) instead
    of a `GetSemanticModel` per endpoint.
  - Call `FindImplementationForInterfaceMember` only when the class does *not* declare the member
    itself, or declares it `new` (the D7 case).
  - Results must be byte-identical: every existing route/diagnostic test and the committed OpenAPI
    snapshot stay green.
  - Target: at least 3× faster per endpoint on a body edit at N=500, measured with B's benchmark before and
    after.
- **D20 — Location-free producer input** (rule 6):
  - The producers consume a `Select`ed projection of `EndpointModel` without `LocationKey`s. The
    reporter keeps the located model.
  - Typing a line above an endpoint then leaves all four output steps Cached. A new line-shift test
    proves it.
- **D21 — Build the plan once:** `EndpointMappingPlan` becomes one pipeline step, or a lazily cached
  value on the immutable model, shared by the producers and the reporter. `ShadowParameters` are cached in
  it.
- **D22 — Group discovery through base classes:** reproduce first with a red test. If confirmed, the
  group predicate becomes "class with a base list" plus the existing semantic check (the endpoint
  predicate's own cost, measured at zero).
- **D23 — Guards, copying upstream rule 8:**
  - a **fixed-file-set test** for both generators (1 vs 5 inputs across namespaces, with and without
    OpenAPI, and 1 vs 5 referenced servers for the client);
  - a **line-shift test** (D20);
  - tracking and an assertion for the `openEndpointNames` step.
- **D24 — Typed client file set [owner's decision]:**
  - *Recommended:* every client class goes in one fixed `EndpointClients.g.cs` next to
    `EndpointClientUrl.g.cs`. Class names stay derived from the server assemblies. This removes the
    unbounded, rename-sensitive hint names, and the guard (D23) can then hold.
  - The alternative is to keep one file per server and exempt the client from the guard.
- **D25 — Analyzer folder [owner's decision]:**
  - *Recommended:* keep the unversioned `analyzers/dotnet/cs`, recorded as a deliberate deviation from
    upstream rule 14. The owner decided older Roslyn is unsupported, and an unsupported host should say
    why (CS9057, measured) rather than silently produce no `Map…Endpoints()`.
  - Upstream's CS8032 concern applies to hosts that *load* the dll. Here they reject it by version first,
    with CS9057, as measured on SDK 10.0.112.
  - The alternative is to follow upstream and move to `roslyn5.9/cs`, accepting a silent skip on older
    hosts.
- **D26 — Package versions:** Phase 5 targets **12.0.1**, not 12.0.0:
  - `MintPlayer.SourceGenerators.Tools`, `MintPlayer.ValueComparerGenerator(.Attributes)`;
  - `MintPlayer.SourceGenerators(.Attributes)` for MustChangePassword and SitemapXml.

  The only consumer-visible change from 12.0.0 is the equality output's file name
  (`GeneratedEquality.g.cs`). Wait until nuget.org indexes 12.0.1; it was pushed at 19:38Z and not yet
  visible at 19:43Z.
- **Not done, with reasons:**
  - **Narrowing the endpoint predicate:** measured at zero cost (S3 = S2).
  - **`ForAttributeWithMetadataName` for assembly attributes:** `AssemblyInfo` and `ClosedEndpoints`
    measure 0.4–2.6 ms and are Unchanged on edits.
  - **Hoisting `GetTypeByMetadataName` calls:** Roslyn caches them per compilation.
  - **Shrinking generated output (1.45 MB at N=500):** a real downstream compile cost, but it needs its
    own design across all four files and the client, and nobody has reported it.
  - **A CI allocation benchmark gate (upstream rule 13):** upstream runs BenchmarkDotNet head-against-base
    on one runner. This repo would first need that infrastructure. B's benchmark is kept as
    documentation of the method, not as a gate.

> **Owner decisions, 2026-09-25:**
> - **D24 — The typed client writes one fixed `EndpointClients.g.cs`** next to `EndpointClientUrl.g.cs`. Class
>   names stay derived from the server assemblies. The fixed-file-set guard (D23) covers the client with no
>   exemption.
> - **D25 — The analyzer moves to the versioned `analyzers/dotnet/roslyn5.9/cs` folder,** following upstream
>   rule 14. The owner: "we should probably just support Visual Studio 2026 and .NET 10 + 11".
>   - `$(EndpointsAnalyzerPackPath)` becomes `analyzers/dotnet/roslyn5.9/cs`. Every pack guard follows
>     that property, so they move with it. The Generator, CodeFixes, Tools and ValueComparerGenerator
>     attributes dlls all ship there.
>   - `SuppressToolsAnalyzerCopies` and the stray-folder guard must still reject any second analyzer
>     folder. Upstream's own `roslyn5.9/cs` copies now target the *same* path as ours, so the guard must
>     deduplicate, not merely forbid.
>   - On a host older than Roslyn 5.9 the generator is now skipped silently. The first symptom is a
>     missing `Map…Endpoints()` (CS1061), not CS9057. The README "Requirements" section and the Generator
>     package README state that symptom and the supported hosts: Visual Studio 2026, .NET SDK 10.0.400+
>     or 11.x. The earlier measurement (CS9057 on SDK 10.0.112) is superseded; re-measure it on 10.0.112
>     for the README.
>   - Supersedes the "stays unversioned" reasoning recorded with commit `7e280e6`.

### Acceptance criteria (addendum 2)

18. **Measured speed-up:** B's benchmark before and after D19–D21 at N=500. The body-edit rerun is at
    least 3× faster. Cold time, the Path-edit rerun and allocations are recorded.
19. **Byte-identical output:** generated output is byte-identical for the TestApp (the OpenAPI snapshot is
    unchanged) and for the generator test corpus.
20. **Line shift:** a line inserted above an endpoint leaves every output step Cached.
21. **Guards green:** the fixed-file-set guard is green for both generators (client per D24), and the
    `openEndpointNames` step is tracked.
22. **Group discovery:** D22 is either reproduced and fixed (red→green) or disproved, with the test kept.
23. **Package versions:** Tools, ValueComparerGenerator and MintPlayer.SourceGenerators are at 12.0.1,
    and `dotnet list package --outdated` is empty.

> **Status 2026-09-26:**
> - **D26 and acceptance 23** are met at **12.1.0**, which superseded 12.0.1. See the "Superseded by 12.1.0"
>   note after the Phase 5 as-built notes.
> - **Acceptance 17** is met: the upstream defects were filed as MintPlayer/MintPlayer.Dotnet.Tools#187,
>   and fixed there.
> - **Acceptance 18** is the only one not met: the body-edit rerun measured 1.7× faster, against a 3×
>   target, with the ~26 ms binding floor recorded in the Phase 6 as-built notes. Whether 1.7× is
>   accepted is the owner's decision.

> **As built (Phase 6, 2026-09-25, committed as `d1ccf1e`):**
> - **Red first**, then green: the D22 group test (`GroupInheritingIEndpointGroupFromItsBaseClass_IsDiscovered`)
>   failed with `info MPEP016: Endpoint group 'RootGroup' is not joined…` (the group was mapped as a root
>   group: its `[MemberOf<RootGroup>]` and prefix were never read). D22 is reproduced and fixed. The line-shift
>   test failed with 7 of 8 output-step reasons `Modified`; the `OpenEndpointNames` tracking tests found no
>   such step; the client guard found `["ApiClient.g.cs", "EndpointClientUrl.g.cs"]`. The server
>   fixed-file-set guard passed from the start (rule 7 was already met).
> - **D19 as built:** one discovery transform for endpoints and groups (both use the same "class with a base
>   list" predicate now, so D22 and the merge are one change); `Path`/`Prefix`/`Methods` read from the class's
>   own declaration syntax when it has no base class or lists the interface again, with the interface map only
>   otherwise; string literals folded from syntax, the transform's `SemanticModel` reused for anything else;
>   syntactic pre-checks that skip `GetAttributes()` on a type with no attribute lists and skip binding the
>   member list for bound properties when no property has an attribute; namespace checks without
>   `ToDisplayString`. The plan is built once per model (`EndpointModel.GetPlan`, D21) with shadow parameters
>   cached in it; the producers read `EndpointModel.WithoutLocations()` (D20, tracked as `ProducerModel`).
> - **Byte-identical output (acceptance 19):** generated files and diagnostics were dumped for the corpus,
>   the corpus with OpenAPI, a shapes corpus (const concatenation, `nameof`, explicit implementations, getter
>   forms, raw literals, parentheses, computed and interpolated paths, `new static` and re-listed interfaces,
>   every `Methods` spelling, a user interface default, partial declarations, nested and open generics) and
>   the benchmark corpus at N=100, from the base commit and from this tree: `diff -r` is empty. The TestApp
>   OpenAPI snapshot is unchanged.
> - **D24:** `EndpointClients.g.cs` holds every client class; its content is the per-server texts
>   concatenated, with the header and `global using` once. **D25:** both nupkgs carry exactly
>   `analyzers/dotnet/roslyn5.9/cs/{Generator, Generator.CodeFixes, SourceGenerators.Tools, ValueComparerGenerator.Attributes}.dll`.
>   The stray-folder guard now drops same-path copies (packing with `SuppressToolsAnalyzerCopies` disabled
>   still yields that one folder) and still errors on any other analyzer folder (triggered deliberately).
>   Re-measured with a scratch consumer on the packed packages: SDK 10.0.112 prints no CS9057 or CS8032 and
>   fails with `CS1061 'WebApplication' does not contain a definition for 'MapConsumerEndpoints'` (or, with a
>   typed endpoint, `CS0535`/`CS0115` first); SDK 10.0.401 builds clean. The READMEs say so.
> - **Measurements (acceptance 18):** B's benchmark, before (base commit) and after (this tree) interleaved
>   twice on one machine, Release net10.0, median of 7 at N=500:
>
>   | Scenario, N=500 | Before | After | Speed-up |
>   |---|---|---|---|
>   | Cold (OpenAPI on) | 122–146 ms, 33.0 MB | 84–87 ms, 24.9 MB | 1.5× |
>   | Method-body edit | 76.5–79.1 ms, 16.8 MB | 44.9–46.6 ms, 10.6 MB | **1.7×** |
>   | Unrelated class added | 75.3–76.3 ms | 42.5–45.6 ms | 1.7× |
>   | One `Path` literal changed | 97.5–101.2 ms, 32.8 MB | 51.8–60.5 ms, 24.6 MB | 1.8× |
>   | Discovery step alone (body edit) | 69.9 ms | 28.2 ms | 2.5× |
>   | `DescribeDeclaredEndpoint`, 500 endpoints, fresh compilation | 9.9–11.9 ms | 4.6–4.9 ms | 2.2× |
>   | Client cold / unrelated edit | 17.9 / 0.64 ms | 15.0–18.4 / 0.57 ms | = |
>
>   N=100 body edit 15.8 → 10.0 ms; N=10 4.9 → 3.5 ms.
> - **Acceptance 18 is not met: 1.7×, not 3×.** A floor measurement explains why: a generator with the same
>   predicate whose transform does nothing but `GetDeclaredSymbol` + `AllInterfaces` costs 5.6–6.0 ms per body
>   edit at N=500; adding `GetAttributes()` makes it 12.4–14.5 ms, and adding `GetMembers()` 25.9–28.0 ms. The
>   generator needs both for every endpoint (group membership and descriptor names are attributes, bound
>   properties and `Path` are members), so the binding it cannot avoid on a fresh compilation already costs
>   about the 26 ms a 3× target allows. Getting below it needs work the transform does not own: not
>   re-running the semantic transform per node at all, which `CreateSyntaxProvider` does on every compilation.
>   The target in D19 was set before this floor was measured.

## Version

Created 2026-09-25 from issue #34. Branch `fix/endpoints-open-generics` (from `master` at `c04ffac`).

Implemented on `fix/endpoints-open-generics`; PR [#35](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/pull/35), CI green, awaiting the release decision (proposed `11.2.0-rc.0`).

Updated 2026-09-26: the addenda (Tools 12 / generated equality, generator performance) are committed as
`d1ccf1e`. Everything moved to MintPlayer.Dotnet.Tools 12.1.0 without the upstream workarounds in `1fa1b75`,
and the docs were brought in line in `b2b323b`. CI is green. Open: the release version, and acceptance 18.
