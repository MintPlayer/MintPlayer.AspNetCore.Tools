# PRD: Service-aware hooks: `Configure` with the provider, and `GetPath`

Issues:
- [#36: `IEndpointGroup.Configure` cannot see the service provider, and the docs send you to `IsEnabled` instead](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/issues/36). Fixed here.
- [#37: `Path` is static with no `IServiceProvider`, so a configuration-driven route cannot be an endpoint class](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/issues/37). Fixed here.
- [#40: prefix-scoped middleware declared on a group](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/issues/40). Closed as not planned ([comment](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/issues/40#issuecomment-6046439281)). This PR only adds the README recipe that the closing comment proposes, and it references #40 without closing it.

*(Draft 3, 2026-10-07.*
- *Draft 1 came from a four-agent investigation: I1 = #36, I2 = #37, I3 = #40, and I4 = conventions.*
- *Draft 2 recorded the outcome of a grilling session (Q1 to Q7) with the owner.*
- *Draft 3 adds the Spark consumer review of PR #41
  ([comment](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/pull/41#issuecomment-6046759977)),
  which the owner accepted. It brings three changes:*
  - *an endpoint-level `IsEnabled` (R5);*
  - *a startup check that a configured path has the same route parameters as its default (R2.9 and
    R2.10);*
  - *a check that typed binding works through `MapEndpoint<T>()` (R6, spike S4).*
- *Draft 3 also adds an owner decision: a class must not be both a group and an endpoint (MPEP036,
  R5.5).*

*Every decision below is settled. The spikes in the PLAN check the remaining mechanics.)*

**Affected version:** MintPlayer.AspNetCore.Endpoints 11.3.0-rc.0 (runtime, Abstractions, Generator)
**Proposed version:** 11.4.0-rc.0. This release is **breaking** (see R1), but the major version tracks
.NET, and 11.1.0-rc.0 set the precedent of a breaking minor. The version is already bumped on branch
`feat/endpoint-service-aware-hooks`.

## Overview

Today the only hook that can see the application's configuration is
`IEndpointGroup.IsEnabled(IServiceProvider)`. This PRD gives the provider to the other map-time hooks:

1. **#36:** `Configure` takes the provider, on groups and on endpoints. It **replaces** the
   one-argument hook instead of overloading it, and a new error diagnostic (MPEP035) catches leftover
   one-argument hooks. The README says that `IsEnabled` decides whether a group **exists**, not what it
   carries.
2. **#37:** `IEndpointBase.GetPath(IServiceProvider)` chooses the route at map time. `Path` stays required
   and is the default. Build-time route checks still run against that default. Route-shaped outputs
   (typed links, client contracts, OpenAPI token shadowing) are left out, and the descriptor is flagged.
   If a configured path's route parameters differ from those of the default, the app fails at startup.
3. **Spark review:** `IEndpointBase.IsEnabled(IServiceProvider)` gives each endpoint its own gate.
   - A group is enabled or disabled as a whole, so a condition that applies to one endpoint belongs on
     that endpoint, and no `if` is needed around mapping.
   - A disabled endpoint is not mapped, and its `Configure` and `GetPath` are never called.
   - Typed request binding is verified, or fixed, on `MapEndpoint<T>()`, so that generic endpoints mapped
     by reflection behave like generated ones.

Every hook is `static`, is evaluated once when the routes are mapped, and receives the **root**
provider, in the same way as `IsEnabled`.

## Problem statement

**#36.** `Configure(RouteGroupBuilder)` receives no provider. A convention that depends on an option
therefore needs `((IEndpointRouteBuilder)group).ServiceProvider`, a cast needed because
`RouteGroupBuilder` implements that property explicitly. Meanwhile the README points "options-dependent"
readers to `IsEnabled`. `IsEnabled` **deletes routes**, where the reader only wanted to drop a
convention: gating CORS with it makes `POST /connect/token` disappear.

**#37.** `static abstract string Path` receives nothing. A value supplied with each registration could
only reach it through a static mutable field, which is shared by the whole process. A computed `Path`
is already allowed (MPEP011 is Info). What is missing is access to configuration. The consumer that
needs it is Spark's dev-tunnel WebSocket (`options.DevWebSocketPath`, gated on `DevelopmentAppId`).

## Goals

- **G1.** A group or endpoint convention can depend on configuration without a cast (#36).
- **G2.** The README says that `IsEnabled` decides existence, not conventions (#36).
- **G3.** An endpoint class can take its route from configuration. This works both in the generated
  mapping and in `MapEndpoint<T>()` (#37).
- **G4.** No output presents a configurable endpoint's default `Path` as *the* route. Endpoints that do
  not override `GetPath` lose nothing (#37).
- **G5.** A README recipe shows the alternative that the #40 closing comment proposes: middleware keyed
  on `Group.Prefix`.
- **G6.** Tests for all of the above. The package version is 11.4.0-rc.0.
- **G7.** One endpoint can be enabled or disabled from configuration without disabling its whole group
  (Spark review).
- **G8.** An endpoint that requires typed request binding behaves the same whether it is mapped by the
  generated method or by `MapEndpoint<T>()` (Spark review).

## Non-goals

- **Group-level middleware hooks (#40).** Closed as not planned. Middleware order can't be expressed per
  assembly, and groups are not a unit of the request pipeline.
- **A `FullPrefix<TGroup>()` helper** for the nested-group recipe. It would be new API, and the #40
  closing comment rejected it.
- **A configurable group prefix** (`GetPrefix(IServiceProvider)`, #37 shape 2), and
  **`MapEndpoint<T>(path)`** (#37 shape 3).
- **Keeping the one-argument `Configure` hooks.** The owner has explicitly chosen not to preserve
  backward compatibility here.
- **Resolving scoped services in any hook.** All hooks run before any request exists.

## Requirements

### R1: `Configure` takes the provider (#36). Breaking.

- **R1.1** Replace these hooks; they become the only `Configure` hooks:
  - `IEndpointGroup.Configure(RouteGroupBuilder)` becomes
    `static virtual void Configure(RouteGroupBuilder group, IServiceProvider services) { }`
    (`Abstractions/IEndpointGroup.cs:24-28`).
  - `IEndpointBase.Configure(RouteHandlerBuilder)` becomes
    `static virtual void Configure(RouteHandlerBuilder builder, IServiceProvider services) { }`
    (`Abstractions/IEndpointBase.cs:36`).
- **R1.2** Each hook has one call site per mapping path, and each passes `routes.ServiceProvider`, the
  root provider:
  - generated `MapGroup<TGroup>` (`Producer.cs:411-417`);
  - generated endpoint `Map` helpers (`Producer.cs:484`);
  - reflection `MapGroupCore<TGroup>` (`EndpointRouteBuilderExtensions.cs:287-293`);
  - `MapEndpoint<T>` (`EndpointRouteBuilderExtensions.cs:123`).
- **R1.3** Add **MPEP035 (Error)**: "`{0}` declares `Configure({1})` without an `IServiceProvider`
  parameter. Since 11.4 it is no longer called. Add `IServiceProvider services` as the second
  parameter."
  - It fires on a group or endpoint type that declares a static `Configure` with a single
    `RouteGroupBuilder` or `RouteHandlerBuilder` parameter.
  - Detection is syntactic, on the type's own declarations.
  - This is needed because an *implicit* implementation with the old signature still compiles as an
    unrelated method and would silently stop applying its conventions. That is #36's failure mode,
    caused this time by the upgrade. An explicit implementation already fails with CS0539.
- **R1.4** Migrate every in-repo hook to the new signature:
  - TestApp `UsersApi` and `ProductsApi`;
  - TestLibrary `LibAuthGroup`;
  - `MapEndpointTests.cs:76`;
  - all README samples (lines 110 and 527-557, and any others).
- **R1.5** README:
  - Add a paragraph after line 369: "`IsEnabled` decides whether a group **exists**, not what it carries."
    Illustrate it with the CORS example from #36, using `services`.
  - Update the `Configure` section (527-557) for the new signatures.
  - Add MPEP035 to the diagnostics table.

### R2: `GetPath(IServiceProvider)` (#37)

- **R2.1** Add `static virtual string? GetPath(IServiceProvider services) => null;` to `IEndpointBase`.
  - Every caller resolves the route as `TEndpoint.GetPath(sp) ?? TEndpoint.Path`.
  - **`null` always means "use `Path`"**, whether the method is not overridden or an override returns
    null. It never means "not mapped": disabling an endpoint is `IsEnabled`'s job. This is documented and
    tested.
  - `Path` stays `static abstract`. For an overriding endpoint, `Path` is the default.
  > I2 confirmed with a compiled prototype, and the owner confirmed independently: the issue's
  > `GetPath(sp) => Path` default fails with **CS8926**. Inside the interface there is no type parameter
  > through which the default could reach the implementer's `Path`.
- **R2.2** The generated `Map<TEndpoint>` and `Map<TEndpoint,TShadow>` (`Producer.cs:391,403,464`) map
  `TEndpoint.GetPath(routes.ServiceProvider) ?? TEndpoint.Path`. The same expression is emitted for
  every endpoint.
- **R2.3** `MapEndpoint<T>()` (`EndpointRouteBuilderExtensions.cs:92`) uses the same expression, as a
  constrained generic call with no reflection.
- **R2.4** Add a new flag, `EndpointInfo.HasPathOverride`, and include it in equality (`Models.cs:237`).
  Detection (Q6):
  1. **Syntactic first:** a `GetPath` method, or an explicit `IEndpointBase.GetPath`, among the class's
     own declarations.
  2. **Semantic fallback**, used only when the class has a base type other than `object`, **or**
     implements an interface that is not one of the library's own:
     `FindImplementationForInterfaceMember(IEndpointBase.GetPath)`. It counts as overridden when the
     result is anything other than `IEndpointBase`'s own member. This catches inherited overrides and
     overrides that come from an intermediate interface.
- **R2.5** What an override changes (Q2):
  - **Still run, against the literal `Path` (the default), at normal severity:** MPEP007, MPEP008,
    MPEP009 and MPEP010. The default is a real route that ships when nothing is configured.
  - **Left out for this endpoint:** typed links (`TypedLinks.cs:100-105`) and client contracts
    (`EndpointContracts.cs:51-55`).
  - **Treated as an unknown route:** OpenAPI route-token shadowing (`ShadowParameters.For`). Bound
    `[RouteParam]` properties still become `[FromRoute]`, as they do for a computed `Path` today.
  - **The descriptor is flagged:** the static `Endpoints` list (`Producer.cs:121-135`) still shows the
    literal path, and `EndpointDescriptor` gains `IsPathConfigurable`, set to `true` for these
    endpoints. This adds new public API.
- **R2.6** Add **MPEP034 (Info)**: "`{0}` chooses its route at map time (`GetPath`). Its `Path` is only
  the default: typed links and client contracts are not generated for it, and route checks apply to the
  default only."
  - When `Path` is also not a constant, MPEP034 is reported instead of MPEP011.
- **R2.7** Open-generic endpoints: the open-endpoint record carries `HasPathOverride`
  (`Producer.cs:568`, `OpenEndpointRecords.cs:129`, `EndpointClosing.cs:109,121`). The closing assembly
  therefore applies R2.5 and R2.6.
- **R2.8** Give `new static GetPath` the MPEP032 treatment that `Path` and `Methods` already get: the
  hidden member is ignored and a warning says so.
- **R2.9** Add a `GetPath` section to the README, built around the WebSocket example. It covers:
  - only the root provider is available;
  - null falls back to `Path`;
  - **a configurable path must keep the same route parameters as its default**, because route checks bind
    to the default. R2.10 enforces this at startup;
  - what is left out (R2.5);
  - the consumer pattern of defaulting the option to the endpoint's `Path`, so the default is written
    once.

- **R2.10 (Spark review)** Check the route parameters at map time. When `GetPath` returns a non-null
  value:
  - Both mapping paths parse it and the literal `Path` with `RoutePatternFactory.Parse`.
  - If the two sets of parameter names differ, mapping throws an `InvalidOperationException` at startup.
    The message names the endpoint type, both patterns, and the parameters missing from each side.
  - The comparison is on parameter **names**, case-insensitively, as routing does.
  - Constraints, defaults, optional markers and catch-all markers are not compared. They don't affect
    which properties bind.
  - Literal segments may differ freely; that is the point of making the path configurable.
  - The check lives in one runtime helper, `EndpointPathValidator`, called from the generated `Map`
    helpers and from `MapEndpoint<T>()`. It costs nothing for endpoints that don't override `GetPath`,
    because it runs only when the result is not null.

### R3: README recipe for #40

- **R3.1** Add a subsection, "Middleware for a group's prefix", showing
  `app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments(SomeGroup.Prefix), b => …)`. Because it is in
  the README, the sample compiles.
- **R3.2** Add a caveat: a nested group's `Prefix` is relative, so the recipe composes
  `Parent.Prefix + Child.Prefix`. If the group is moved with `[MemberOf<>]`, the routes follow but the
  middleware does not. A root group is the safe case.

### R5: Endpoint-level `IsEnabled` (Spark review)

- **R5.1** Add `static virtual bool IsEnabled(IServiceProvider services) => true;` to `IEndpointBase`.
  - Its doc comment says it is evaluated once at map time against the root provider, as the group
    version is.
  - It also says that a group is enabled or disabled as a whole, so a per-endpoint condition belongs here.
- **R5.2** Evaluation order for each endpoint:
  1. The endpoint's group chain must be enabled; this is checked as today.
  2. The endpoint's own `IsEnabled` is checked. If it returns false, nothing else happens: no
     `MapMethods`, no `GetPath`, no `Configure`, no R2.10 check.
  3. Otherwise mapping proceeds: `GetPath`, the R2.10 check, `MapMethods`, then `Configure`.
- **R5.3** In the generated mapping, each endpoint's `Map…` call is wrapped in
  `if (IsEnabled<TEndpoint>(app.ServiceProvider)) { … }`.
  - This uses a generic helper, `IsEndpointEnabled<TEndpoint>` (because of CS0117, the same reason
    `IsEnabled<TGroup>` is a generic helper, `Producer.cs:163-166`).
  - The wrap is emitted for every endpoint. Override detection isn't needed, because the default returns
    true and the JIT inlines it.
- **R5.4** `MapEndpoint<T>()` checks `T.IsEnabled(app.ServiceProvider)` after the group-chain check
  (`EndpointRouteBuilderExtensions.cs:78-83`). A disabled group makes it `return app;` without
  mapping anything (`:82`). A disabled endpoint does exactly the same, and the doc comment says so.
- **R5.5 (owner decision, D12)** A class must not be both a group and an endpoint. Today nothing
  enforces this: discovery checks `IsGroup` and the endpoint interfaces independently
  (`EndpointGenerator.cs:438-440`).
  - With R5 in place, one implicit `public static bool IsEnabled(IServiceProvider)` would silently
    implement **both** interface members. One `Configure` name would then cover two different builders.
    Groups describe organisation, and endpoints describe handlers.
  - New diagnostic **MPEP036 (Error)**: "`{0}` implements both `IEndpointGroup` and an endpoint
    interface. A class is either a group or an endpoint. Move the endpoint into its own class and join
    it with `[MemberOf<{0}>]`."
    - Detection is in `Discover`, on the type's `AllInterfaces`, so it covers interfaces inherited
      through base classes.
    - It is reported at the class identifier.
    - The type is then treated only as a group, so mapping output stays deterministic despite the error.
  - Before implementing, grep the repo, TestApp, TestLibrary, test fixtures and README for classes that
    are both, and migrate them.
- **R5.6** `IsEndpointMapped<T>` (11.3) already answers from type metadata, so a disabled endpoint
  correctly reports "not mapped". Add a test for this. The static `Endpoints` descriptor list ignores
  `IsEnabled`, as it already does for groups; document this next to the group caveat.
- **R5.7** README: add an "Enabling a single endpoint" subsection under `IsEnabled`, with the
  `/manage` example: one group carries `RequireAuthorization`, and each endpoint has its own mode
  condition.
- **R5.8** Correct the Spark consumer follow-up: the dev-tunnel endpoint's `IsEnabled` is the
  **endpoint-level** one added here.

### R6: Typed binding through `MapEndpoint<T>()` (Spark review)

- **R6.1** Typed request binding must behave the same on `MapEndpoint<T>()` as on the generated mapping,
  including for generic endpoints closed by the caller (`MapEndpoint<X<TUser>>()`). That means:
  - request-typed endpoints (`IPostEndpoint<TReq>` and the other verbs);
  - `BindRequestAsync` overrides, including form-urlencoded ones such as the OIDC token, revocation and
    introspection endpoints;
  - 400 on a malformed body, and 415 on the wrong content type.
- **R6.2 Verified by spike S4 (2026-10-07): parity already holds, and no code change is needed.**
  - Binding is done by the endpoint base class, not by either mapper: `EndpointBase<T>.HandleAsync`
    calls `BindRequestAsync`, and `BodyEndpoint<T>` returns 400 or 415.
  - The generator emits the same `partial class X<TUser> : PostEndpoint<TReq>` whichever mapper is used,
    and the two request delegates are equivalent (`EndpointRouteBuilderExtensions.cs:91-117` compared
    with `Producer.cs:462-484`).
  - A scratch TestServer ran 48 request cases (valid, malformed, wrong content type, empty, form,
    override). They gave identical results through `MapEndpoint<X<User>>()` and through the generated
    mapping, both with and without `AddControllers()`.
  - The only differences between the two mappers are in the OpenAPI shape (the `[AsParameters]` shadow
    parameter and `Produces<TResponse>`), and both are already documented.
  - No existing test sends a request body through `MapEndpoint<T>`, so R6 is delivered as **regression
    tests only** (AC12).
  - Consumer caveat: each typed endpoint must be `partial` (MPEP001 otherwise) or derive from
    `PostEndpoint<T>` explicitly.
  > Two behaviours are the same on both paths but worth knowing:
  > - an empty body with no content type gets 415, not 400;
  > - a JSON `null` body gets a bare 400, not problem+json.
  >
  > Neither changes in this release.

### R4: Release

- **R4.1** Set `<Version>` to 11.4.0-rc.0 in the three packable csprojs. **Done.**
- **R4.2** The PR title and body state plainly that the release is **breaking**, and give the MPEP035
  migration.
- **R4.3** The README samples must compile.
- **R4.4** Run the full test sweep once, locally, on net10.0 and net11.0.

## Acceptance criteria

- **AC1 (#36)** A group's `Configure(group, services)` is applied in both the generated mapping and
  `MapEndpoint<T>()`, and so is an endpoint's `Configure(builder, services)`. Each is called exactly once,
  with the root provider.
- **AC2 (#36)** A convention that depends on configuration (a tag, which is easy to assert, or CORS) is
  present when the option is on and absent when it is off. The routes exist in both cases.
- **AC3 (#36, MPEP035)** MPEP035 is reported for a group with an implicit
  `static void Configure(RouteGroupBuilder)` and for an endpoint with
  `static void Configure(RouteHandlerBuilder)`. It is not reported for the new signatures, or for a
  `Configure` with other parameter types.
- **AC4 (#37)** An endpoint whose `GetPath` returns an option value answers on that path and not on its
  literal `Path`, in both mapping paths.
  - An override that returns null maps at `Path`.
  - An endpoint without an override maps exactly as before.
- **AC5 (#37)** For an endpoint that overrides `GetPath`, whether directly, explicitly, through a base
  class or through an intermediate interface:
  - MPEP034 is reported, and MPEP011 is not;
  - MPEP009 still fires on a default `Path` whose bound property has no matching token;
  - no typed link and no client contract is generated;
  - its descriptor has `IsPathConfigurable == true`.

  A sibling endpoint that does not override `GetPath` is unaffected.
- **AC6 (#37)** An open-generic endpoint that overrides `GetPath`, once closed by the application, maps on
  the resolved path and reports MPEP034.
- **AC7** `new static GetPath` reports MPEP032.
- **AC8** The README covers the `IsEnabled`-is-existence paragraph, the `GetPath` section and the #40
  prefix recipe, and all of its samples compile.
- **AC9** Packages are versioned 11.4.0-rc.0, and the full sweep passes on both TFMs.
- **AC10 (R2.10)** Startup fails with an `InvalidOperationException` naming the endpoint and both
  patterns when a `GetPath` value's parameter names differ from the default's, for example
  `/hooks/{id}` against `/x/{key}`.
  - It fails this way in both mapping paths.
  - Startup succeeds when only literal segments, constraints or case differ, for example `/hooks/{id}`
    against `/custom/{ID:int}`.
- **AC11 (R5)** An endpoint whose `IsEnabled` returns false is not mapped (404 on its route), in both
  mapping paths.
  - Its `Configure` and `GetPath` are never invoked. Assert this with counters.
  - Its group siblings are still mapped.
  - `IsEndpointMapped<T>` returns false for it.
- **AC13 (R5.5)** MPEP036 is reported in each of these cases:
  - a class that implements both `IEndpointGroup` and an endpoint interface directly;
  - a class that implements both through base classes or intermediate interfaces.

  It is not reported for an ordinary group or an ordinary endpoint.
- **AC12 (R6)** Through `MapEndpoint<T>()`, a generic request-typed endpoint and a form-urlencoded
  `BindRequestAsync` endpoint:
  - bind a valid body;
  - answer 400 to a malformed body;
  - answer 415 to the wrong content type;
  - behave the same as the generated mapping.

## Decisions (all settled, 2026-10-07 grilling)

- **D1: One PR for #36 and #37, plus a README recipe that references #40.** This is the owner's
  instruction, and #37's comment ties it to #36's release.
- **D2 (Q3): Replace the one-argument `Configure` on groups and endpoints; do not overload.** MPEP035
  (Error) is the safety net.
  - Reason: with no need for backward compatibility, there is one signature, one call site and no
    ordering rule.
  - Cost: the release is breaking, and in-repo hooks have to migrate.
- **D3 (Q1): `GetPath` returns `string?`, and null means "use `Path`".**
  - Making `Path` virtual was rejected: a missing route would become a runtime throw instead of a
    compile error.
  - A separate `IConfiguredPath` interface was rejected: it needs a second helper per endpoint and breaks
    the pattern `IsEnabled` set.
  - An instance-based path was rejected because #37 already ruled it out.
- **D4 (Q2): Route checks stay on the default path. Route-shaped outputs are left out, and the
  descriptor is flagged.** Same tokens in the configurable path are a documented requirement.
- **D5 (Q6): Override detection is syntactic first, with a semantic fallback** when there is a base
  class or a non-library interface.
- **D6: All hooks receive the root provider.** A scoped service is documented misuse (#37 comment,
  constraint 2).
- **D7 (Q5): #40 gets only a README recipe**, with the nested-prefix caveat. No helper API.
- **D8 (Q7): Version 11.4.0-rc.0.** The major tracks .NET. MPEP035 is the upgrade warning.
- **D9 (Spark review): Endpoint-level `IsEnabled` ships in this release.** A group is enabled or disabled
  as a whole, and a per-endpoint condition lives on the endpoint. A disabled endpoint skips `GetPath` and
  `Configure`.
- **D10 (Spark review): A route-parameter mismatch from `GetPath` fails at startup.** The startup check
  replaces the documentation-only rule from D4. A first-request 400 becomes a startup failure.
- **D11 (Spark review): `MapEndpoint<T>()` must have typed-binding parity.** S4 verified that it
  already does, so this release adds regression tests only.
- **D12 (owner): A class is either a group or an endpoint, never both.** MPEP036 (Error) enforces it.
- **D13 (owner): Endpoints are fixed at startup.** `IsEnabled` (group and endpoint) and `GetPath` are
  evaluated once, when the routes are mapped. A configuration change at runtime does not map or unmap
  anything; it takes a restart.
  - The README says this plainly in the `IsEnabled` and `GetPath` sections.
  - No runtime-toggle helper and no dynamic `EndpointDataSource` will be added.
  - Rejected alternatives:
    - an `EnabledWhen<TOptions>` per-request filter helper;
    - a generated mapping that re-evaluates on options change.

## Consumer follow-up (MintPlayer.Spark, after 11.4.0-rc.0 is published)

- The OIDC CORS groups move to `Configure(group, services)` and drop the cast. Every other
  `Configure` hook also migrates; MPEP035 lists them.
- The dev-tunnel WebSocket in `MintPlayer.Spark.Webhooks.GitHub` becomes an endpoint class. Its
  `GetPath` returns `options.DevWebSocketPath`, and the endpoint-level `IsEnabled` (R5) checks
  `DevelopmentAppId`.
- The roughly 15 `/manage` account routes stay in one group, which carries `RequireAuthorization` once.
  Each route gates itself on the local-credentials mode with an endpoint-level `IsEnabled`.
- About 25 endpoints that are generic over `TUser` are mapped with `MapEndpoint<X<TUser>>()`. They rely
  on the R6 parity.
  `GitHubWebhooksOptions.DevWebSocketPath` defaults to the endpoint's `Path` constant, so the string is
  written once (Q1, condition 2).
- The `/connect` framing middleware stays as it is, keyed on `OidcConnectGroup.Prefix` (#40 closing
  comment).
