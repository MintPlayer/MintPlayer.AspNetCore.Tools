# Implementation plan: endpoint type metadata (issue #38)

Companion to `PRD-EndpointTypeMetadata.md` (Draft 3; the issue body is the source of truth). Branch
`feat/endpoint-type-metadata`. One PR against `master`, not merged by Claude: merging publishes to
nuget.org through `publish-release.yml`.

Testing rule: test runs are batched into Phase 5. Earlier phases verify with targeted builds only.

## Phase 0 — Investigation: done (2026-10-02)

Four agents (I1 runtime, I2 generator, I3 Spark consumer, I4 API/AOT) and a prototype review. Their
findings are in the PRD blockquote and in "Prototype review". These spikes are settled:

- **Type from `MethodInfo`.** Refuted. The `DeclaringType` is a compiler closure (`scratchpad\spike-runtime`).
- **When the data source is complete.** The DI data source is empty until `StartAsync`. A route-builder
  read freezes route-handler conventions. Confirmed by the prototype (0 endpoints before start, 19 after).
- **AOT.** 0 warnings on both TFMs, and 0 ILC warnings on a Native AOT publish. DAM on the property
  produces IL2087, so it is left off (`scratchpad\spike-aot`).
- **Test churn.** None (I2).

## Phase 1 — Decide: done (Draft 3)

- **D1:** the type is recorded inside `ForMetadata`.
- **D6:** the property is `EndpointType`.
- **D7–D10:** the prototype's tamper hardening, string tags, group metadata and per-feature `Map…`
  methods are rejected.

The docs were brought in line with the issue and committed on this branch.

## Phase 2 — Spike S1, mixed versions (AC7): open

Run this before Green, because a negative result reopens D1.

- Build a throwaway endpoint library in the scratchpad against the **published 11.2.0-rc.0 package**, so
  its `Map…Endpoints()` body is old generator output.
- Run it from an app that ProjectReferences this repo's runtime, with assembly unification to the higher
  version.
- Assert that `IsEndpointMapped` is true for one of the library's generated endpoints, and for an
  open-generic endpoint closed by the app.
- Record the result in the PRD. Keep it as a test only if it can be made hermetic.

## Phase 3 — Red: open

Add a stub `EndpointTypeMetadata` and an `IsEndpointMapped` that throws `NotImplementedException`, so the
suite compiles and fails for the right reason. Then:

1. **AC4:** in `Tests/MintPlayer.AspNetCore.Tools.Tests/Endpoints/MapEndpointTests.cs`, next to the
   existing `ForMetadata` tests.
2. **AC1:** in `Tests/MintPlayer.AspNetCore.Endpoints.Generator.Tests/GroupMembershipTests.cs`, using the
   `:93-185` pattern (`GeneratedEndpointHost.MapAndCollectRoutes`, `MapManually` at `:108`).
3. **AC2:** next to `MapEndpointGenericTests.cs`, reusing `Echo<T>`, `Outer<T>.Inner` and `DisabledGroup`
   (`:20-54`).
   - Add an unsealed `EchoBase<T>` / `EchoString`.
   - Add a same-name impostor class in another namespace, and a `MapPost` lambda on the same route.
   - Add a helper that builds a `CompositeEndpointDataSource` after all mapping.
4. **AC3:** in `TestLibraryEndToEndTests` (helper at `:25`).
5. **AC6:** a real host, false before `StartAsync` and true after.

## Phase 4 — Green: open

1. **`EndpointTypeMetadata`** in `Endpoints/MintPlayer.AspNetCore.Endpoints/` (R1), with XML docs in the
   repo's style.
2. **`EndpointAttributes.ForMetadata`:** append the metadata and update its docs (R2). The mapping sites
   and the generator stay untouched.
3. **`EndpointDataSourceExtensions.IsEndpointMapped`** plus its generic overload (R3). Its XML docs state
   the R6 timing rule.

## Phase 5 — README, version, sweep, PR: open

1. **README:** the sections listed in R6.
2. **Version:** 11.3.0-rc.0 in the runtime, Abstractions and Generator csproj files, kept in lockstep.
3. **One sweep:**
   - the solution `-t:Rebuild -c Release`: 0 warnings, and no AOT warnings on either TFM;
   - the Endpoints test projects, with output redirected to a log file;
   - `git status --porcelain` over TestApp `openapi\*.json` (AC5).
4. **PR:** open it with the docs and link #38. Do not merge.

## Phase 6 — Consumer (MintPlayer.Spark, after publish): open

Done in the Spark repo's open PR; see PRD "Consumer follow-up".

- Bump the 8 libs to 11.3.0-rc.0.
- Switch `passkeys` and `externalLogins` to `IsEndpointMapped`. The other three flags stay as they are.
