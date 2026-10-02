# Implementation plan: endpoint type metadata (issue #38)

Companion to `PRD-EndpointTypeMetadata.md` (Draft 3; the issue body is the source of truth).

- **Branch:** `feat/endpoint-type-metadata`.
- **PR:** [#39](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/pull/39), against `master`, not
  merged by Claude: merging publishes to nuget.org through `publish-release.yml`.
- **Testing rule:** test runs are batched into Phase 5. Earlier phases verify with targeted builds only.

## Phase 0 — Investigation: done (2026-10-02)

Four agents (I1 runtime, I2 generator, I3 Spark consumer, I4 API/AOT) and a prototype review. Their
findings are in the PRD blockquote and in "Prototype review". These spikes are settled:

- **Type from `MethodInfo`.** Refuted: the `DeclaringType` is a compiler closure (`scratchpad\spike-runtime`).
- **When the data source is complete.** The DI data source is empty until `StartAsync`, and a
  route-builder read freezes route-handler conventions. The prototype confirmed it: 0 endpoints before
  start, 19 after.
- **AOT.** 0 warnings on both TFMs, and 0 ILC warnings on a Native AOT publish. `[DynamicallyAccessedMembers]`
  on the property produces IL2087, so it is left off (`scratchpad\spike-aot`).
- **Test churn.** None (I2).

## Phase 1 — Decide: done (Draft 3, `474ffe1`)

- **D1:** the type is recorded inside `ForMetadata`.
- **D6:** the property is `EndpointType`.
- **D7–D10:** the prototype's tamper hardening, string tags, group metadata and per-feature `Map…`
  methods are rejected.

The docs were brought in line with the issue and committed as `474ffe1`.

## Phase 2 — Spike S1, mixed versions (AC7): done (2026-10-02), positive

> **Result** (`scratchpad\spike-s1`, net10.0).
> - `OldLib` was built against the **published 11.2.0-rc.0 package**, with 0 warnings. Its generated
>   `MapOldLibEndpoints()` calls
>   `global::MintPlayer.AspNetCore.Endpoints.EndpointAttributes.ForMetadata(typeof(TEndpoint))`.
> - `NewApp` ProjectReferences this repo's runtime and generator, and references `OldLib.dll` as built, with
>   no rebuild against 11.3. It closes `OldOpen<TUser>` through `[assembly: EndpointTypeArgument]`.
> - Output after start:
>   - `GET /old/hello -> OldLib.OldHello`
>   - `GET /old/open -> OldLib.OldOpen`1[NewApp.AppUser]`
>   - `IsEndpointMapped<OldHello>()` = **True**
>   - `IsEndpointMapped(typeof(OldOpen<>))` = **True**
>
> D1 holds. S1 stays a documented spike rather than a test: a hermetic test would need the 11.2 package,
> or a prebuilt DLL, committed to the repo.
>
> **Order changed.** S1 needs the runtime change it tests, so Phase 4 was written before Phase 3's tests.
> As a result the new tests were never seen red.

## Phase 3 — Tests: done (`6e034b7`, `19f5de1`, `86659e0`)

Written after Green, for the reason given in Phase 2.

| AC | Test | File |
|---|---|---|
| AC1 | `EndpointType_IsRecordedOnce_OnEitherPath` (5 group shapes) and `EndpointType_IsRecordedOnce_ForAPropertyBoundEndpoint_OnEitherPath` | `Generator.Tests/GroupMembershipTests.cs` |
| AC2 | 11 tests | `Tools.Tests/Endpoints/IsEndpointMappedTests.cs` |
| AC3 | `ClosedLibraryEndpoint_IsReportedAsMapped_ByItsOpenDefinition` and `DisabledGroup_IsReportedAsNotMapped` (with the `WhoAmI<>` control) | `TestLibraryEndToEndTests.cs` |
| AC4 | `ForMetadata_RecordsTheEndpointType_Once` | `MapEndpointTests.cs` |
| AC6 | `ContainerDataSource_IsCompleteOnlyAfterStart` | `IsEndpointMappedTests.cs` |

- **The property-bound AC1 test** uses its own fixture assembly, so the 5-route parity fixture is
  unchanged. It first asserts that the generated source calls `Map<global::Fixtures.Bound, …>`, then
  checks that the recorded type is `TEndpoint`, never the shadow.
- **AC2 covers:**
  - a closed type;
  - a generic definition;
  - a derived closing vs. a closed base;
  - a nested generic;
  - a disabled group, with a control endpoint;
  - same-name and same-route impostors;
  - interfaces, asked of an endpoint that really implements them;
  - argument validation;
  - last instance wins;
  - the metadata constructor's null check.
- **The impostor class** is `Tools.Tests/Endpoints/Impostors/SignIn.cs`.

## Phase 4 — Green: done (`6e034b7`)

1. **`EndpointTypeMetadata`** in `Endpoints/MintPlayer.AspNetCore.Endpoints/` (R1). It has a null-checked
   constructor and a get-only `EndpointType`. `ToString()` was dropped in `19f5de1` because it was
   unspecified and untested.
2. **`EndpointAttributes.ForMetadata`** appends the metadata, and its `<summary>`/`<remarks>` state the new
   contract (R2). The mapping sites and the generator are untouched.
3. **`EndpointDataSourceExtensions.IsEndpointMapped`** plus its generic overload, in
   `Endpoints/MintPlayer.AspNetCore.Endpoints/Extensions/` (R3). Its XML docs state the R6 timing rule.

## Phase 5 — README, version, sweep, PR: done; PR #39 waiting for the owner to merge

1. **README** (`Endpoints/MintPlayer.AspNetCore.Endpoints/README.md`, R6):
   - New section "Is an endpoint mapped?" after "Attributes, `Configure` and endpoint metadata". It
     covers the matching rules, the timing trap, the composite data source, base-type matching, and older
     libraries getting the metadata without a rebuild.
   - Its example uses `ListPasskeys<>` from "Generic endpoints".
   - Cross-links from the `IsEnabled` part of "Groups" and from "Manual registration".
   - A trim-safe line in "Trimming and native AOT".
2. **Version:** 11.3.0-rc.0 in the runtime, Abstractions and Generator csproj files, kept in lockstep.
3. **Sweep** (on `6e034b7`):

   | Check | Result |
   |---|---|
   | Solution `-t:Rebuild -c Release` | 0 warnings, 0 errors |
   | Tools.Tests | 1097/1097 on net10.0 and net11.0 |
   | Generator.Tests | 389/389 on net10.0 and net11.0 |
   | OpenAPI snapshot (AC5) | Content unchanged; the rebuild only rewrote line endings, so it was restored |

   The later commits (`19f5de1`, `86659e0`) touch tests, docs and an unused method only. They were
   verified by targeted runs of the touched classes on both TFMs; CI on #39 runs everything.
4. **PR #39** is open, linked to #38.
5. **Review** ([#39 review](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/pull/39#pullrequestreview-5396048336)):
   ready to merge, with three non-blocking nits, all fixed in `86659e0`:
   - a control endpoint in the disabled-group unit test;
   - parity coverage of the `Map<TEndpoint, TShadow>` path;
   - a fixed `EchoBase<T>.Path` instead of a per-process string hash.

## Phase 6 — Consumer (MintPlayer.Spark, after publish): open

This is done in the Spark repo's open PR; see PRD "Consumer follow-up".

- Bump the 8 libs to 11.3.0-rc.0.
- Switch `passkeys` and `externalLogins` to `IsEndpointMapped`. The other three flags stay as they are.
