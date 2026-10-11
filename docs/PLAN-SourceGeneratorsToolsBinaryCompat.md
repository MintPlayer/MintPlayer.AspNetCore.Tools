# Implementation plan: rebuild against MintPlayer.SourceGenerators 12.2.0 (issue #42)

Companion to `PRD-SourceGeneratorsToolsBinaryCompat.md` (Draft 2).

- **Branch:** `fix/42-sourcegenerators-12.2`.
- **PR:** one PR against `master`, with `fixes #42` and version 11.4.1-rc.0 in the title. Claude does not
  merge it, because merging publishes to nuget.org.

## M0: Investigation (done, 2026-10-11)

Four agents investigated:
- the Tools API diff and member references;
- the `[Inject]` order;
- a guard test design, including a local consumer reproduction of MPSG001;
- release, Spark and Dotnet.Tools.

The PRD records the findings (F1 to F4).

## M1: Dependency update (R1 to R3) — [x] done

- Bumped the references in R1, the CodeFixes comment, and the Endpoints versions (R2).
- Added the README requirement note (R3).

## M2: Verification — [x]

- `dotnet build -c Release -t:Rebuild`: 0 warnings, 0 errors.
- **AC1:** packed the Endpoints packages as `11.4.1-verify.1` to a scratch feed, then built a
  Web-SDK consumer with a partial typed `IPostEndpoint<TReq, TRes>` against MintPlayer.SourceGenerators
  12.2.0, with `UseSharedCompilation=false`. Exit 0, and `EndpointMapping.g.cs` was generated.
- Full test suite passed on net10.0 and net11.0, with 0 failures:
  - Endpoints.Generator.Tests: 430 tests;
  - Tools.Tests: 1136 tests.

## Progress ledger

- 2026-10-11:
  - Investigation done, and the scope was cut to the dependency update.
  - M1 done.
  - M2 done: the rebuild, AC1 and the full test suite all passed.
  - PR opened.
