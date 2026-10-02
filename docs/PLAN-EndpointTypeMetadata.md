# PLAN: Endpoint type metadata

PRD: [PRD-EndpointTypeMetadata.md](PRD-EndpointTypeMetadata.md) · Issue [#38](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/issues/38).
Status: **not started** (2026-10-02).


One PR, not merged by Claude: merging to `master` publishes to nuget.org (`publish-release.yml`).

## Phase 1 — Metadata type and runtime path
- Add `EndpointTypeMetadata` (R1) to the runtime project, documented in the repo's XML style.
- `MapEndpoint<TEndpoint>`: `builder.WithMetadata(new EndpointTypeMetadata(typeof(TEndpoint)))` beside the `ForMetadata` call (R2).
- Tests in `MintPlayer.AspNetCore.Tools.Tests/Endpoints/MapEndpointTests.cs` and `MapEndpointGenericTests.cs`: exactly one instance; the closed type for `Echo<string>`.

## Phase 2 — Generator path
- `EmitMapBody`: emit the matching `WithMetadata(builder, new global::MintPlayer.AspNetCore.Endpoints.EndpointTypeMetadata(typeof(TEndpoint)));` (R2).
- Generator text test (AC4) and parity test (AC1) in `MintPlayer.AspNetCore.Endpoints.Generator.Tests`.

## Phase 3 — `IsEndpointMapped`
- The extension methods (R3, D2, D3), trim-safe (R5).
- Unit tests for every AC2 case, including the disabled group and the derived closing. End-to-end test (AC3) in `TestLibraryEndToEndTests`.

## Phase 4 — Docs and version
- README sections (R6). Generator README if the emitted code is documented there.
- Version bump to 11.3.0-rc.0 in the runtime, Abstractions and Generator csproj files.
- Confirm the OpenAPI contract snapshot is unchanged (AC5) and that both projects build with zero AOT warnings.

## Phase 5 — Consumer (separate repository, after publish)
- MintPlayer.Spark: bump the package and switch `GetAuthCapabilities` to `IsEndpointMapped` (see "Consumer follow-up").
