# PRD: Endpoint type metadata — "was endpoint X mapped?" without route strings

Issue: [#38 — record the endpoint type as metadata so "is endpoint X mapped?" needs no route strings](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/issues/38).

*(Draft 3, 2026-10-02. Implemented in PR [#39](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/pull/39),
which is waiting for the owner to merge; see the "As built" blockquote after the acceptance criteria. The
issue body is the source of truth for the design; this file mirrors it.)*

*Earlier drafts:*
- *Draft 1 (`938eef4`) came from a reading of the mapping paths.*
- *Draft 2 added a four-agent investigation with spikes (I1 runtime, I2 generator, I3 consumer,
  I4 API/AOT). Its findings are kept below as blockquotes.*
- *Draft 3 added a review of a working prototype (see "Prototype review") and settled D1 and D6.*

**Affected version:** MintPlayer.AspNetCore.Endpoints 11.2.0-rc.0 (runtime, Abstractions, Generator)
**Proposed version:** 11.3.0-rc.0 (new public API; the major tracks .NET)

## Overview

Record the endpoint **class** on every endpoint this library maps. Offer one helper that answers whether a
given endpoint class, or any closing of an open generic one, is mapped in the running application.

## Problem statement

An endpoint mapped by this library carries its own attributes as metadata (`EndpointAttributes.ForMetadata`),
but not its type. So "is `ListExternalLogins<TUser>` mapped?" can only be answered by matching route strings.

**The motivating consumer** is MintPlayer.Spark's `GET /spark/auth/capabilities`
(`libs/authorization/MintPlayer.Spark.Authorization/Endpoints/GetAuthCapabilities.cs`). It tells the SPA
which account pages the server serves, so the SPA never links to a page that 404s. It builds a set of every
`RoutePattern.RawText` and matches literals such as `"/spark/auth/external-logins"` and
`"/spark/auth/passkeys/sign-in"`.

**Why that is fragile:**
- A renamed route or group prefix silently makes a flag wrong, and nothing fails to compile.
- The check lives in a different file from the mapping, so the two drift apart.

**Why the descriptor list can't replace it.** The generated `Endpoints` descriptor list shows what is
**declared**, not what is mapped. In Spark, "declared but not mapped" comes from **conditional mapping**
rather than `IsEnabled`:
- `PasskeyEndpoints.MapPasskeyApi` returns early when `Passkeys == Disabled` (`PasskeyEndpoints.cs:76-77`).
- `MapExternalLoginManagement` returns early when linking is `Disabled`
  (`SparkAuthenticationExtensions.cs:221-222`).
- Spark's IdentityProvider does use `IsEnabled` (`OidcLocalCredentialsGroup`).

Either way, the endpoint is simply absent from the data source.

## Goals

- **G1.** Every endpoint this library maps carries its closed endpoint type in its metadata, exactly once.
  That covers both the generated `Map{Assembly}Endpoints()` and the manual `MapEndpoint<T>()`.
- **G2.** A public, trim-safe helper answers "is this endpoint type mapped?", including for open generic
  definitions (`typeof(ListExternalLogins<>)`).
- **G3.** Renaming or deleting an endpoint class breaks consumers at compile time instead of silently
  changing an answer.
- **G4.** Libraries already compiled against 11.2 get the metadata when the application runs the 11.3
  runtime, without being rebuilt.

## Non-goals

- Describing declared-but-unmapped endpoints. The existing `Endpoints` descriptor list does that.
- A capability, feature-flag or tag system. Consumers decide what a mapped endpoint *means* (D8).
- Group-level metadata, or "is group G mapped?" (D9).
- Endpoints mapped outside this library, such as plain `MapGet` or `MapIdentityApi`. They carry no endpoint
  class.
- Correct answers before the host has started (R6).
- Tamper resistance against code running in-process (D7).

## Requirements

- **R1. `EndpointTypeMetadata`.**
  - A public sealed class in `MintPlayer.AspNetCore.Endpoints` (runtime assembly, next to
    `EndpointAttributes`).
  - It has a public constructor `EndpointTypeMetadata(Type endpointType)`, which throws on null, and one
    get-only property, `Type EndpointType`.
  - It holds the **closed** type. That is `typeof(TEndpoint)` on both mapping paths, which can never be
    open, including endpoints that an application closes through `[assembly: EndpointTypeArgument<,>]`.
  - The property carries no `[DynamicallyAccessedMembers]`.
- **R2. Recorded inside `ForMetadata` (D1).**
  - `EndpointAttributes.ForMetadata(endpointType)` (`EndpointAttributes.cs:23-28`) appends
    `new EndpointTypeMetadata(endpointType)` to the filtered attributes it returns.
  - Both mapping paths already call it exactly once per endpoint, before `TEndpoint.Configure`:
    `EndpointRouteBuilderExtensions.cs:120`, and in the generator `EndpointGenerator.Producer.cs:483`.
  - The mapping sites and the generator are **unchanged**, and group conventions add nothing.
  - `ForMetadata`'s `<summary>`/`<remarks>` are updated to the new contract: "the attributes of
    `endpointType`, plus one `EndpointTypeMetadata`".
- **R3. `IsEndpointMapped`.**
  - It lives in `EndpointDataSourceExtensions`, namespace `MintPlayer.AspNetCore.Endpoints`:
    - `public static bool IsEndpointMapped(this EndpointDataSource dataSource, Type endpointType)`
    - `public static bool IsEndpointMapped<TEndpoint>(this EndpointDataSource dataSource)`
  - It scans `dataSource.Endpoints` and reads `endpoint.Metadata.GetMetadata<EndpointTypeMetadata>()`, so
    the last instance wins (documented).
  - **A closed type** matches by equality.
  - **A generic type definition** matches any mapped type whose type, **or a base type**, is a closing of
    it. So `EchoString : EchoBase<string>` counts as `EchoBase<>`.
    - A closed base type does **not** match a derived endpoint; only the definition does.
  - Only the class and its `BaseType` chain are walked. **Interfaces never match**, or `IGetEndpoint<>`
    would match everything.
  - **Nested generics** follow the CLR: `typeof(Outer<>.Inner)` matches `Outer<string>.Inner`, and
    `typeof(Outer<>)` does not.
  - **Argument rules:**
    - `null` throws `ArgumentNullException`.
    - A partially open type throws `ArgumentException`, because otherwise it would silently always be
      `false`. That is a type that `ContainsGenericParameters` but is not a definition, such as
      `typeof(Derived<>).BaseType`.
  - Endpoints without the metadata never match.
  - There is no extra cache. The composite caches `Endpoints` until its change token fires, and
    `GetMetadata<T>` is cached per type.
- **R4. Disabled groups and conditional mapping.** An endpoint that was never mapped is absent from the
  data source, so the helper returns `false`. That covers both a disabled group (both paths already skip
  it) and a mapping call inside an `if`.
- **R5. AOT and trimming.**
  - The runtime and Abstractions projects keep `IsAotCompatible` with **0** warnings on net10.0 and
    net11.0.
  - The helper is not `[RequiresUnreferencedCode]`.
  - It uses no static per-process state, so two hosts in one process work.
- **R6. README.** A new section near "Attributes, `Configure` and endpoint metadata" / "Generic endpoints",
  stating:
  - **Timing.** The DI `EndpointDataSource` holds **zero** endpoints until the host starts.
    - A call in `Program.cs` before `app.Run()`, or inside `IHostedService.StartAsync`, returns `false`
      with no error.
    - Safe points are request time and `IHostApplicationLifetime.ApplicationStarted` or later.
  - **Where to read.** The DI `EndpointDataSource` is the composite over all route builders, including
    `MapGroup`.
    - Never read `IEndpointRouteBuilder.DataSources` during startup. Reading it builds the endpoints, and
      later conventions then throw `InvalidOperationException: Conventions cannot be added after building
      the endpoint`.
  - **What changes.** One new object appears in `endpoint.Metadata` and in `ForMetadata`'s result.
  - **Base-type matching.** A query for a library generic base such as `PostEndpoint<,>` matches every
    endpoint derived from it.
  - **"Trimming and native AOT"** (`:769`): one line saying the helper is trim-safe.

> **Investigation findings (Draft 2, 2026-10-02).**
> - **The `MethodInfo` already in metadata is not usable** (I1, spike `scratchpad\spike-runtime`, net10 and
>   net11).
>   - Both paths map through `MapMethods(path, methods, lambda)`, so ASP.NET adds the lambda's
>     `MethodInfo`. Its `DeclaringType` is a compiler-generated closure class, for example
>     `<>c__DisplayClass1_0`1[[Hello]]`.
>   - Recovering the endpoint type from it depends on how Roslyn lowers closures. It is also ambiguous:
>     the shadow overload's closure carries `<TEndpoint, TShadow>`.
>   - So R1 is needed.
> - **Timing** (I1 and I4, both spikes).
>   - The DI `EndpointDataSource` is a `CompositeEndpointDataSource`, and includes `MapGroup` endpoints.
>   - It is empty after mapping and before `StartAsync`, and also inside `IHostedService.StartAsync`. It is
>     complete from `ApplicationStarted` onwards, and an early read does not freeze it.
>   - Reading `IEndpointRouteBuilder.DataSources[*].Endpoints` early **does** build the endpoints. After
>     that, a convention added to an already-mapped `RouteHandlerBuilder` throws.
>   - The prototype confirmed it: `endpoints=0` before start, `endpoints=19` after.
> - **AOT** (I4, spike `scratchpad\spike-aot`).
>   - A library with the runtime csproj's settings (`Sdk.Web`, `net10.0;net11.0`, `IsAotCompatible`) gave
>     0 warnings under `-t:Rebuild -c Release`.
>   - A Native AOT publish of a consumer gave 0 ILC warnings, and every R3 case answered correctly in the
>     native exe.
>   - `[DynamicallyAccessedMembers]` on `EndpointType` produced IL2087 at the unannotated `Map<TEndpoint>`,
>     once per TFM.
> - **Groups** (I1).
>   - A disabled-group endpoint is absent on both paths, and group helpers add no per-type metadata.
>   - Group metadata is ordered before endpoint metadata. Duplicates can come only from a user's
>     `Configure`, which runs after `ForMetadata`.
> - **Test churn** (I2). None: no `.g.cs` or snapshot files are committed, and the `ForMetadata` tests use
>   only `Contains`/`DoesNotContain`.

## Acceptance criteria

- **AC1 (parity).** The same endpoint is mapped through the generated path and the manual path.
  - On each path it carries exactly one `EndpointTypeMetadata` with the closed type. Assert with
    `GetOrderedMetadata<EndpointTypeMetadata>().Count == 1`, because `GetMetadata` cannot detect duplicates.
  - Follow `GroupMembershipTests.MembershipAttribute_IsNotEndpointMetadata_OnEitherPath`
    (`GroupMembershipTests.cs:93-185`).
- **AC2.** `IsEndpointMapped` cases, asserted against a data source collected after all mapping and
  conventions:
  - true for a mapped closed type;
  - false for an unmapped type;
  - true for `typeof(Echo<>)` when `Echo<string>` is mapped. This is the Spark shape:
    `MapEndpoint<X<TUser>>()` called inside a generic method;
  - true for `typeof(EchoBase<>)` when only `EchoString : EchoBase<string>` is mapped. This needs a new
    unsealed fixture, because `Echo<T>` is sealed (`MapEndpointGenericTests.cs:20`);
  - false for `typeof(EchoBase<string>)` in that same case;
  - false for an endpoint in a disabled group;
  - false for an unmapped endpoint, while a **different class with the same simple name** in another
    namespace is mapped, and a plain `MapPost` lambda serves the same route. This is the prototype's
    impostor probe;
  - the argument rules: `null`, a partially open type, a generic interface definition, and
    `Outer<>.Inner`.
- **AC3 (end to end).** Via `WebApplicationFactory`, extending `TestLibraryEndToEndTests`:
  - TestLibrary's open `Passkeys<>`, closed by TestApp, is reported as mapped.
  - With `UseSetting(LibPasskeysGroup.EnabledKey, "false")` it is not, while the control `WhoAmI<>`
    still is.
- **AC4.** A `ForMetadata` unit test asserts exactly one `EndpointTypeMetadata` with the passed type.
  Existing tests stay green (`MapEndpointTests.cs:215-238,453-454`).
- **AC5.** The OpenAPI contract snapshot is unchanged. That is TestApp `openapi\*.json`, checked by
  `git status --porcelain` as in `pull-request.yml:56-68`.
- **AC6 (timing).** On a real host, the DI `EndpointDataSource` answers `false` for a mapped type before
  `StartAsync`, and `true` after.
- **AC7 (mixed versions, spike S1).** An endpoint library compiled against the **published 11.2.0-rc.0
  package**, run by an app on the 11.3 runtime, reports its generated endpoints as mapped.
  - This is the proof of G4 and D1.
  - It becomes a test only if it can be made hermetic, with no nuget.org fetch in CI.

> **S1 result (2026-10-02): positive.** A library built against the published 11.2.0-rc.0 package,
> without a rebuild, reported both of these as mapped on the 11.3 runtime:
> - its generated `OldHello`;
> - its open `OldOpen<>`, closed by the app.
>
> Details are in PLAN Phase 2. AC7 stays a documented spike, not a test.

> **As built (2026-10-02, PR [#39](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/pull/39),
> `6e034b7`, `19f5de1`, `86659e0`).** R1–R6 are implemented as written. AC1–AC6 have tests; PLAN Phase 3
> maps each AC to its tests.
>
> **Differences from, or additions to, the text above:**
> - `EndpointTypeMetadata` has no `ToString()` override. R1 does not ask for one.
> - **AC1** also covers a property-bound endpoint, which the generator maps through
>   `Map<TEndpoint, TShadow>`. It records `TEndpoint`, never the generated `[AsParameters]` shadow. This
>   was added after review.
> - **AC2:**
>   - The interface case is asked of an endpoint that really implements `IGetEndpoint` and a generic
>     marker interface, so it cannot pass vacuously.
>   - The disabled-group case maps a control endpoint in the same call.
>   - Two cases were added: "last instance wins" (an endpoint's `Configure` adds a second instance) and
>     the constructor's null check.
> - **R6:** the README example uses the README's own `ListPasskeys<>`. The "Groups" `IsEnabled` passage
>   and "Manual registration" link to the new section.
> - **Sweep:** 0 warnings on the Release rebuild. Tools.Tests 1097/1097 and Generator.Tests 389/389 passed
>   on both TFMs. The OpenAPI snapshot is unchanged (AC5).

## Decisions

- **D1. Record the type inside `ForMetadata`, not as an extra line at both mapping sites. Settled
  (Draft 3).** This reverses Draft 1.
  - **The decisive reason.** The 8 Spark libraries reference Endpoints 11.2.0-rc.0 without `PrivateAssets`,
    so an app easily runs the 11.3 runtime under libraries still built against 11.2.
    - Their compiled `Map…Endpoints()` calls the runtime's `ForMetadata`, so they get the metadata with no
      rebuild (G4).
    - An emitted generator line would answer `false` for them until every library is republished.
  - **Draft 1's other premises were also refuted** (I2):
    - The generator is packed and pushed as its own package, so an emitted line would need a
      `CanRecordOpenEndpoints`-style gate (`EndpointGenerator.cs:485-490`).
    - `ForMetadata`'s tests use `Contains`, so they don't break.
  - **Cost:** one wording change to `ForMetadata`'s documented contract.
  - The prototype independently made the same choice.
- **D2. Walk `BaseType` for generic definitions, never interfaces.** A derived non-generic closing is a
  mapping of the generic endpoint in every sense a consumer cares about, and the generator emits such
  closings.
- **D3. An extension on `EndpointDataSource` only.** There is no `IEndpointRouteBuilder` overload, because
  of the startup trap in R6. Tests without a started host build
  `new CompositeEndpointDataSource(((IEndpointRouteBuilder)app).DataSources)` after all conventions are
  added.
- **D4. No per-assembly generated `IsEndpointMapped`.**
  - A generated version would see declarations, missing `IsEnabled` and `if`.
  - It would need static state, which breaks with two hosts in one process.
  - It would miss manual mappings.
- **D5. Sealed class, no interface.** An interface can be added later without a break.
  - Prior art: MVC's `ControllerActionDescriptor` in metadata, and FastEndpoints'
    `EndpointDefinition.EndpointType`.
- **D6. Property name `EndpointType`. Settled (Draft 3).**
  - It matches the class name and `IsEndpointMapped`.
  - In minimal APIs "handler" means the delegate, so `EndpointDescriptor.HandlerType`
    (`Abstractions\EndpointDescriptor.cs:31`) is not followed.
- **D7. No tamper resistance** (prototype rejected).
  - The prototype hardened the token in two ways:
    - **Issued:** an internal constructor, plus a static `ConditionalWeakTable` of the instances
      `ForMetadata` issued.
    - **IssuedAndBound:** a `Finally` convention binding each token to the endpoint's final
      `RequestDelegate`.
  - Rejected:
    - Forging needs code in the same process, which can already map, remove or replace any endpoint. The
      answer is a UI hint, not an authorization decision.
    - Both layers use static per-process state (R5).
    - The bound set grows with every data-source rebuild.
    - Endpoint filters can wrap the delegate after `Finally` runs, which would make the answer silently
      `false`.
    - The internal constructor would stop tests and other mapping helpers from creating the metadata.
  - Kept: the public constructor and metadata-only matching. These already defeat the realistic mistakes,
    same-name types and same-path lambdas (AC2).
- **D8. No string feature tags** (prototype rejected).
  - In the prototype's own run, an impostor endpoint carrying the `[EndpointFeature("passkeys")]` tag made
    `passkeys` read true, while type identity correctly said false.
  - Tags are global and can collide across libraries.
  - Renaming a tag breaks nothing at compile time, which defeats G3.
- **D9. No group metadata and no `IsGroupMapped`** (prototype rejected).
  - The prototype flowed the group class's **attributes** into every member endpoint. That is a breaking
    semantic change: an `[Authorize]` on a group class would suddenly start applying.
  - It doesn't help the consumer. Spark's conditional endpoints share `SparkAuthGroup` with always-mapped
    ones (`GetAuthCapabilities`, `/me`).
  - It was wired only on the generated path, and only for exact types.
- **D10. No per-feature generated `Map…` methods** (prototype rejected). The prototype's
  `MapTwoFactorEndpoints()` / `MapPasskeyEndpoints()` were hand-invented. The real generator emits only
  `Map{Assembly}Endpoints()`, and conditional mapping is the consumer's own `if`.

## Prototype review (Draft 3)

`C:\Repos\WebApplication10\WebApplication10`, built by another session.

**How it is set up:**
- It references Endpoints 11.2.0-rc.0 with the real generator stripped.
- A hand-written `Generated/WebApplication10Endpoints.g.cs` stands in for the generator; its changes are
  marked `PROTOTYPE`.
- It has library stand-ins under `Features/`, and consumer stand-ins `AuthFeatureEndpoints.cs` and
  `SparkIdentityApi.cs`.

It was built (0 warnings) and run twice on 2026-10-02.

| Check | Result | Consequence |
|---|---|---|
| Timing: DI data source before / after start | `endpoints=0 … False` / `endpoints=19 … True` | Confirms R6 and AC6 |
| Disabled group (`Auth:TwoFactor:Enabled=false`) | `VerifyTwoFactor` false at every level | Confirms R4 |
| Same-path lambda and same-name impostor class | Type identity not fooled | Adopted as an AC2 case |
| Impostor with the same string tag | `byFeatureTag.passkeys` true (wrong) | D8 |
| Forged token via reflection / via public `ForMetadata` | Beats `MetadataOnly` / beats `Issued` | D7: accepted, not a threat model |
| Generic endpoint, manual `MapEndpoint<T>` path | **Not exercised** by the prototype | Covered by AC2/AC3 here |

**Adopted:**
- `EndpointTypeMetadata` with `EndpointType`;
- recording through `ForMetadata`;
- the R3 matching rules;
- the impostor test case.

**Rejected:** D7–D10.

## Consumer follow-up (MintPlayer.Spark, after this is published)

Done in the Spark repository, in its open PR. It is listed here so the PRD covers the whole change.

- Bump `MintPlayer.AspNetCore.Endpoints` to 11.3.0-rc.0 in all 8 csproj files: Authorization, History,
  IdentityProvider, MailManager, Moderation, Replication, SoftDelete, Spark.
- In `GetAuthCapabilities`, which already reads the DI data source per request, so the timing is safe:
  - `passkeys = endpointDataSource.IsEndpointMapped(typeof(PasskeySignIn<>))`
  - `externalLogins = endpointDataSource.IsEndpointMapped(typeof(ListExternalLogins<>))`

  Both classes are internal to MintPlayer.Spark.Authorization, the same assembly.
- These stay as they are:
  - `emailChange` comes from an option.
  - `externalProviders` comes from the authentication schemes.
  - `localCredentials`: `/login` comes from `MapIdentityApi` (a `FixedEndpointDataSource`), and `/register`
    is a Spark lambda. Neither carries an endpoint class.

  If Spark wants those route strings gone too, the answer is Spark-side marker metadata, not anything in
  this library.
- Regression guard: Spark's `AuthCapabilitiesTests` and `PasskeyEndpointTests`.
