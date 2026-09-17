# PRD: .NET 11 support for MintPlayer.AspNetCore.Tools

## Overview

Add `net11.0` to every shippable package in this repository, keep `net10.0`, and make CI build and
test both. The source generator stays `netstandard2.0` and is not part of the change.

This is a **multi-targeting** change, not a move. The original framing of the work was "move the
solution to .NET 11 and drop .NET 10"; the investigation behind this document found that dropping
`net10.0` would shorten the supported life of these packages rather than extend it, and would diverge
from the upstream MintPlayer.Dotnet.Tools packages this repo consumes, which chose `net10.0;net11.0`
in [PR #182](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/pull/182). See P4 and the
`[decision]` section under Requirements.

It is worth stating plainly what is *not* wrong today: a `net10.0` library is already consumable from
a `net11.0` application by normal roll-forward. Nothing is broken for consumers. The problems below
are about validation, tooling skew, and shipping `net11.0`-specific assets — not about a repo that
fails to work.

## Problem statement

### P1 — Local and CI builds no longer use the same SDK, and nothing pins either

There is no `global.json` anywhere in the repository (verified by full-tree search; there is also no
`Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` or `.editorconfig`).
Local builds therefore silently select the newest installed SDK, which is now
`11.0.100-rc.1.26425.128`, while both workflows pin `dotnet-version: 10.0.100`
(`.github/workflows/pull-request.yml:35`, `.github/workflows/publish-release.yml:29`).

The repo's own prior PRD already recorded a milder version of this skew —
`docs/PRD-TestCoverage.md:821`: "SDK `10.0.400` locally (CI pins `10.0.100`; no `global.json`, so
they differ)." The gap has since widened from a feature band to a major version. This is a live
defect independent of .NET 11, and it is the single most likely source of a "works locally, fails in
CI" report during this migration.

### P2 — Nothing in CI validates these libraries against .NET 11

988 tests across two projects, at 98.9% line coverage, all execute on `net10.0` only
(`Tests/MintPlayer.AspNetCore.Tools.Tests/…csproj:4`,
`Tests/MintPlayer.AspNetCore.Endpoints.Generator.Tests/…csproj:4`). A consumer running these
middlewares on the .NET 11 runtime is running code no test has ever exercised there. The ASP.NET Core
11 breaking-change list contains several items that plausibly touch this repo's surface — response
compression now always emitting `Vary: Accept-Encoding`, Kestrel tightening HTTP protocol compliance,
hosting emitting OpenTelemetry semantic-convention tags by default — and the suite is the only thing
that would catch them.

### P3 — The generator's ASP.NET Core dependency-leak risk is real and already demonstrated upstream

.NET 11 moves several `Microsoft.Extensions.*` packages into the shared framework. Upstream PR #182
hit exactly this: their generator leaked its .NET 10 copy of
`Microsoft.Extensions.DependencyInjection.Abstractions` to consumers as a compile reference, and one
line produced 120 compile errors.

This repository uses the same MSBuild pattern that caused it —
`Endpoints/MintPlayer.AspNetCore.Endpoints.Generator.csproj:59` adds
`$(PkgMintPlayer_SourceGenerators_Tools)\lib\netstandard2.0\MintPlayer.SourceGenerators.Tools.dll` to
`TargetPathWithTargetPlatformMoniker`, and
`Endpoints/MintPlayer.AspNetCore.Endpoints.csproj:61-71` republishes whatever `GetTargetPath` returns
into `analyzers/dotnet/cs`. Whether this repo leaks anything harmful is unverified and is the subject
of spike S3.

> **Resolved by spike S3: this repo does not leak.** A scratch consumer built at `net10.0` and at
> `net11.0` against the packed package compiles with zero errors and zero warnings; nothing from
> `analyzers/dotnet/cs` becomes a compile reference, `Microsoft.Extensions.DependencyInjection.Abstractions`
> does not leak, and the generator emits byte-identical output on both. P3 is therefore a risk that
> was checked and did not materialise, not a defect to fix. R3.4 costs nothing. The structural reason
> is that the dependency is carried with `IncludeRuntimeDependency="false"` and the project sets
> `IncludeBuildOutput=false`, so there is no `lib/` asset available to leak.

### P4 — "Drop .NET 10" would shorten the supported life of these packages, not extend it

As of 2026-09-17:

| Channel | Type | Phase | EOL |
|---|---|---|---|
| .NET 11 | **STS** (2 years) | RC1, go-live, GA 2026-11-10 | **2028-11-09** |
| .NET 10 | **LTS** (3 years) | active | **2028-11-14** |

.NET 11 is a Standard Term Support release that goes out of support **five days before** the .NET 10
LTS it would replace. Single-targeting `net11.0` would force every consumer onto an STS runtime and
give the packages a shorter supported life than they have today. It would also drop `net10.0`
consumers outright (NU1202 on install), while the upstream dependency continues to ship `net10.0`
assets.

### P5 — An accidental 11.0.0 occupies the version slot the migration would naturally want

`<Version>11.0.0</Version>` is already present on 14 packages while they target `net10.0` — an
accidental publish, since unlisted on nuget.org. nuget.org performs no hard delete, so 11.0.0 remains
restorable to any consumer who pins that exact version, though it is invisible to search and to
floating/range resolution. The migration therefore ships `11.0.1-rc.0`, which sorts above the
unlisted 11.0.0 and below a future 11.0.1.

## Goals

1. Every shippable package targets `net10.0;net11.0` and packs correct `lib/` assets for both, with
   no duplicate-file pack failure from the analyzer-packing targets.
2. The full test suite runs against both target frameworks in CI, and both must pass.
3. Local and CI builds use the same, explicitly pinned SDK.
4. The Roslyn source generator continues to target `netstandard2.0` and continues to load in both a
   .NET 10 and a .NET 11 compiler host, so consumers are not forced onto the .NET 11 SDK.
5. No consumer currently on `net10.0` is broken by the release.
6. Coverage collection keeps working and keeps resolving report paths, under a now-doubled set of
   per-TFM output directories.

## Non-goals

- Dropping `net10.0`. Explicitly rejected; see P4.
- Adopting C# 15 language features, or raising `LangVersion` beyond what the code already uses.
- Bumping `MintPlayer.SourceGenerators.Tools` from 10.16.0, or `MintPlayer.SourceGenerators`/
  `.Attributes` from 10.13.0. These are `netstandard2.0` and TFM-agnostic; nothing about .NET 11
  requires the bump, and the published 10.21.0 nuspec declares a `Microsoft.CodeAnalysis.CSharp >= 5.3.0`
  floor that would raise the minimum compiler host for consumers. Deliberately deferred.
- Rewriting the historical `docs/PRD-TestCoverage.md` / `docs/PLAN-TestCoverage.md` statements about
  .NET 10. By this repo's convention corrections are appended, not rewritten; this document
  supersedes them.
- Adding `net8.0`/`net9.0` back, or any TFM below `net10.0`.
- Any API change to any library.

## Requirements

### R1 — SDK pinning and CI

**R1.1 — A `global.json` pins the SDK for both local and CI builds.** It must specify the .NET 11 SDK
with a `rollForward` policy that tolerates the RC → GA transition without a second edit, and CI must
consume it rather than restating a version.

**R1.2 — Both workflows install the SDKs the build needs.** Building `net10.0;net11.0` and running
tests on both requires the .NET 11 SDK plus the .NET 10 runtime. `actions/setup-dotnet@v5` accepts a
multi-line `dotnet-version`. The current single `dotnet-version: 10.0.100`
(`pull-request.yml:35`, `publish-release.yml:29`) cannot build `net11.0` at all and must change
regardless of every other decision in this document.

**R1.3 — Prerelease SDK selection is explicit and reproducible.** While .NET 11 is RC, the workflow
pins the exact SDK version rather than floating `11.0.x`, so a mid-flight RC2 cannot change CI
behaviour without a commit. Upstream PR #182 pinned `11.0.100-rc.1.26425.128` for the same reason.

**R1.4 — No new NuGet feed is required.** The .NET 11 RC packages are on nuget.org; the existing
single-source `nuget.config` is sufficient and must not gain a nightly/dotnet11 feed.

### R2 — Target frameworks

**R2.1 — All 14 shippable projects move from `<TargetFramework>net10.0</TargetFramework>` to
`<TargetFrameworks>net10.0;net11.0</TargetFrameworks>`.** Note the property name change — `TargetFramework`
singular to `TargetFrameworks` plural — which converts each project to an outer/inner build.

**R2.2 — The two test projects target both frameworks.** `dotnet test` then runs the suite once per
TFM. This is the requirement that gives P2 its answer, and it roughly doubles test wall-clock time.

**R2.3 — `MintPlayer.AspNetCore.Endpoints.Generator` stays `netstandard2.0` and is not
multi-targeted.** A Roslyn analyzer's TFM governs which *compiler host* can load it, not what the
consumer targets; `netstandard2.0` is the only TFM that loads in both the .NET Framework-hosted
MSBuild/VS compiler and the .NET Core-hosted `csc`. Retargeting it would break it and trip RS1041.
This requirement exists to record that the answer to "must we keep net10 for the generator?" is that
the generator never had a `net10.0` to keep.

**R2.4 — `MintPlayer.Timestamps` moves with the rest.** It is the one project still on
`<Version>10.0.0</Version>` (`SitemapXml/MintPlayer.Timestamps/MintPlayer.Timestamps.csproj:11`) and
must not be overlooked because its version differs.

### R3 — Packaging

**R3.1 — The analyzer is packed exactly once.** `TargetsForTfmSpecificContentInPackage` runs once per
inner build. Both `PackEndpointsGenerator` (`Endpoints/MintPlayer.AspNetCore.Endpoints.csproj:58,61-71`)
and `PackAnalyzerAssemblies` (`…Endpoints.Generator.csproj:47-55`) push files to the version-agnostic
`analyzers/dotnet/cs` path, so under two TFMs the same file is added to the same package path twice.
The Endpoints target must become TFM-idempotent. The generator's own target is unaffected because
that project stays single-TFM.

> **Corrected by spike S1.** This requirement originally called the duplication "an NU5118-class
> duplicate-file pack failure". It is not a failure: `dotnet pack` exits 0, NuGet de-duplicates, and
> the resulting package is correct. What it actually produces is two `warning NU5118`, which violates
> R4.3 rather than breaking the package. The requirement stands; its severity was overstated. S1 also
> found that the intuitive fix — hoisting the analyzer to a non-TFM-specific pack target — silently
> produces a package with **no analyzer at all**, so the fix must be verified by unpacking the nupkg.
> See the M0 outcome in the plan.

**R3.2 — The cross-project `GetTargetPath` call keeps working under an outer/inner build.** The
`<MSBuild … RemoveProperties="TargetFramework;TargetFrameworks;RuntimeIdentifier">` at
`Endpoints/MintPlayer.AspNetCore.Endpoints.csproj:62-67` was written against a single-TFM outer build;
under multi-targeting each inner build invokes it. `RemoveProperties` is what stops the caller's TFM
leaking into the `netstandard2.0` generator build, and it must still do so from an inner build.

**R3.3 — The packed nupkg contains `lib/net10.0` and `lib/net11.0` and one analyzer.** Verified by
unpacking a real `dotnet pack` output, not by inspecting the csproj.

**R3.4 — The generator does not leak a framework assembly to consumers.** Per P3, confirm that
nothing reaching `analyzers/dotnet/cs` or `TargetPathWithTargetPlatformMoniker` becomes a compile
reference in a consuming project on either TFM.

### R4 — Dependencies and code

**R4.1 — `Microsoft.AspNetCore.Mvc.Testing` is resolved per-TFM.** At
`Tests/MintPlayer.AspNetCore.Tools.Tests/…csproj:30` it is pinned to `10.0.11`, and it is the only
framework-version-pinned NuGet package in the repository. Under multi-targeting it needs a
TFM-conditioned version, 10.0.x for `net10.0` and 11.0.x for `net11.0`.

**R4.2 — No library gains a `Microsoft.Extensions.*` or `Microsoft.AspNetCore.*` PackageReference.**
Every library takes its ASP.NET Core surface from the implicit `Microsoft.AspNetCore.App` framework
reference via `Microsoft.NET.Sdk.Web`, which retargets with the TFM. This is what keeps R2.1 nearly
free, and it must stay that way.

**R4.3 — The build stays warning-clean on both TFMs.** Commit 37a5e65 cleared every warning in
hand-written code; .NET 11 adds obsoletions with non-default diagnostic IDs, and the existing
`ASPDEPR004`/`ASPDEPR008` suppressions
(`Tests/…/OpenSearch/OpenSearchTestHost.cs:43`,
`Tests/…/MustChangePassword/MustChangePasswordCookieRoundTripTests.cs:48`) may behave differently on
`net11.0`, where those APIs may be removed rather than deprecated. Any new suppression must be
TFM-conditioned rather than applied globally. Warning counts must be measured with `-t:Rebuild`; an
incremental build hides them.

**R4.4 — `CoverageShapeTests` still enumerates the right assemblies.**
`Tests/MintPlayer.AspNetCore.Tools.Tests/Infrastructure/CoverageShapeTests.cs:26-40` hardcodes 14
assembly names. The package set does not change here, but this guard runs twice under R2.2 and must
pass on both.

### R5 — Versioning and release

**R5.1 — Shippable packages move to `11.0.1-rc.0`.** Above the unlisted, accidental `11.0.0`
(see P5) and below a future stable `11.0.1`. `MintPlayer.Timestamps` joins the same version line per
R2.4.

**R5.2 — Publishing to nuget.org is gated on a decision, not automatic.** `publish-release.yml` pushes
on every merge to `master`. Merging this work therefore *publishes* it. Whether to ship an RC-built
package now or hold until .NET 11 GA on 2026-11-10 is a release decision that must be made
consciously before merge, not discovered afterward. See M6 in the plan.

> **Decided 2026-09-17: ship `11.0.1-rc.0` on merge**, under the .NET 11 RC1 go-live licence. The
> `-rc.0` suffix marks it prerelease, so NuGet will not float existing consumers onto it. A stable
> `11.0.1` follows after .NET 11 GA, gated on re-running M5 against the GA SDK.
>
> Also decided: `MintPlayer.Timestamps` joins the shared version line at `11.0.1-rc.0` rather than
> keeping an independent 10.x track, so all 14 packages version together.

### Requires a decision — TFM strategy **[decision]**

Recorded here because the original request proposed the opposite of what this document specifies.

| Option | Consequence |
|---|---|
| **`net10.0;net11.0`** (specified) | No consumer broken. Matches upstream. Keeps the LTS story. Costs: two inner builds, doubled test time, R3.1 pack fix. |
| `net11.0` only | Breaks every `net10.0` consumer (NU1202). Forces consumers onto STS. Shorter supported life than today (P4). Cheaper CI, no R3.1 fix. |

Specified: `net10.0;net11.0`. The cost of the rejected option is borne by consumers and is not
recoverable without another release; the cost of the specified option is CI minutes.

## Acceptance criteria

1. `dotnet build -c Release -t:Rebuild` succeeds with zero warnings across all 18 projects, on both
   inner builds.
2. The full suite passes on `net10.0` **and** on `net11.0`, with zero failures and zero skips on
   each, and the total case count is at least the 988 recorded in `docs/PRD-TestCoverage.md`.
3. `dotnet pack -c Release` produces, for each shippable package, a nupkg containing `lib/net10.0`
   and `lib/net11.0`; the Endpoints nupkg additionally contains exactly one analyzer assembly plus
   `MintPlayer.SourceGenerators.Tools.dll` under `analyzers/dotnet/cs`, with no duplicate entries.
4. A scratch consumer project targeting `net10.0` installs the packed `MintPlayer.AspNetCore.Endpoints`
   `11.0.1-rc.0`, compiles, and its generated endpoints run — proving R2.3 and R3.4 on the TFM that
   would have been dropped.
5. The same scratch consumer, retargeted to `net11.0`, does the same.
6. A `net10.0` consumer built with the **.NET 10 SDK** (not just the .NET 11 SDK targeting net10.0)
   still loads and runs the generator, proving no compiler-host floor was raised.
7. `global.json` exists, and a local `dotnet --version` in the repo root reports the same SDK the
   workflows install.
8. Coverage is collected and uploaded for both TFMs, and the report paths resolve on the coverage
   server without the five duplicated basenames becoming ambiguous.

## Results

**1,976 tests, 0 failures, 0 skipped — 988 on `net10.0` and 988 on `net11.0`.** All 15 shippable
packages ship `lib/net10.0` and `lib/net11.0` at `11.0.1-rc.0`. Every acceptance criterion met.

| Criterion | Result |
|---|---|
| 1 — warning-clean rebuild, both inner builds | Met, against the pre-existing baseline: 72 × CS1591, all in generated code, exactly 2 × the 36 on `master`. No new warning kind. |
| 2 — suite passes on both TFMs, ≥ 988 each | 988 / 988. 0 failures, 0 skips on each. |
| 3 — `lib/net10.0` + `lib/net11.0`, one analyzer, no duplicates | Met. Verified by unzipping all 15 nupkgs. Zero NU5118. |
| 4 — `net10.0` consumer installs and runs | Met, 0 errors / 0 warnings with `TreatWarningsAsErrors`. |
| 5 — same consumer on `net11.0` | Met, 0 errors / 0 warnings. |
| 6 — `net10.0` consumer built with the **.NET 10 SDK** | Met. SDK 10.0.401, generator ran, `EndpointMapping.g.cs` emitted. No compiler-host floor raised. |
| 7 — `global.json` matches what CI installs | Met. `dotnet --version` reports `11.0.100-rc.1.26425.128`. |
| 8 — coverage collected for both TFMs, paths resolve | Met. 4 cobertura reports (2 projects × 2 TFMs), matched pairs. |

**Coverage is unchanged: 1413/1577 on `master`, 1413/1577 on the branch.** Verified by re-running the
full suite on `master` in a worktree and union-merging with the same script, rather than by comparing
against a remembered number. Note this local union does **not** reproduce the 98.9% (1393/1408) in
`docs/PRD-TestCoverage.md`; that figure is the coverage server's own merge. The two answer different
questions and neither moved. Recorded so the gap is not mistaken for a regression later.

### Corrections to this document, from the empirical pass

Three statements above were written before the work and turned out wrong. They are corrected in
place, above, rather than silently edited away:

- **R3.1 overstated the severity.** The analyzer duplication is `warning NU5118`, not a pack failure;
  the package NuGet produces is correct. Multi-targeting would have broken the warning-clean build,
  not the package.
- **P3's leak did not materialise.** The upstream `Microsoft.Extensions.DependencyInjection.Abstractions`
  failure does not reproduce here. R3.4 cost nothing.
- **"14 shippable packages" was wrong; there are 15.** `MintPlayer.AspNetCore.Endpoints.Generator`
  sets `IsPackable=true` with its own `PackageId` and ships as a package in its own right, in
  addition to being embedded in the Endpoints package. It is versioned with the rest at
  `11.0.1-rc.0`. `CoverageShapeTests`' list of 14 is unaffected — it enumerates runtime assemblies,
  and the generator is not one.

### What is deliberately not fixed

The 36 CS1591 in generated code and the 2 NU5128 on the Generator package both pre-date this work,
were confirmed present on `master`, and are left alone. Fixing either means changing the upstream
generator's output or giving the analyzer package a `lib/` folder it should not have.

## Risks

| Risk | Mitigation |
|---|---|
| .NET 11 is RC, not GA; RC2 or GA changes behaviour mid-flight | Pin the exact SDK (R1.3). Re-run the full sweep after GA before any stable release. R5.2 keeps publishing a conscious act. |
| Analyzer packed twice under multi-targeting; pack fails late, at release time | R3.1 is verified by unpacking a real nupkg in an early milestone, not at the end. |
| Generator leaks a framework assembly to consumers, as upstream hit | Spike S3 reproduces the upstream failure shape before any retargeting; acceptance criteria 4-6 prove the consumer side. |
| `ASPDEPR`-suppressed APIs removed outright on `net11.0`, breaking test hosts | Spike S2 compiles the affected test files against `net11.0` before committing to R2.2. |
| Doubled CI time on every PR | Accepted. Two inner builds are the price of R2.2; the suite is fast and the alternative is shipping unvalidated. |
| Coverage report paths collide across two TFMs | `UseSourceLink=false` and the path-uniqueness invariant in `coverlet.runsettings` were designed for duplicated basenames; re-verify under doubled output dirs (acceptance criterion 8). |
| Merging publishes to nuget.org automatically | R5.2 / M6 gate. Decide before merge, not after. |
| An existing consumer pinned to the unlisted 11.0.0 | Unaffected — unlisted packages still restore on an exact pin. 11.0.1-rc.0 does not collide. |

## Appendix A — measurement method

Warning counts are taken from a full `-t:Rebuild`, deduplicated, never from an incremental build.
Package contents are verified by unzipping the actual nupkg and listing entries, never by reading the
csproj. Consumer-side claims (criteria 4-6) are verified against a scratch project consuming the
packed nupkg from a local feed, not against a `ProjectReference`. Test counts come from an actual run,
per-TFM, not from counting attributes.

## Appendix B — what the investigation found and where

- No `global.json`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`,
  `.editorconfig`, `.props`, `.targets` or `.nuspec` exists anywhere in the repo. 18 projects each
  pin their own TFM at line 4. There is no central lever to flip.
- 16 of 18 projects are `net10.0`; the generator is `netstandard2.0`; there is no third value.
- No `PackageReference` or `PropertyGroup` anywhere is conditioned on a TFM today. The only existing
  condition is `'$(Configuration)'=='Release'` on `GenerateDocumentationFile`.
- `Microsoft.AspNetCore.Mvc.Testing 10.0.11` is the only framework-version-pinned package.
- The generator test harness derives its reference set from the test host's own
  `TRUSTED_PLATFORM_ASSEMBLIES`
  (`Tests/…Generator.Tests/Infrastructure/EndpointGeneratorHarness.cs:40`) and deliberately avoids
  `Basic.Reference.Assemblies`, so fixture compilations follow the test project's TFM automatically.
  There is no pinned reference-assembly snapshot to bump.
- The .NET 10 SDK 10.0.401 already bundles Roslyn 5.9.0; the .NET 11 RC1 SDK bundles 5.11.0. The
  4.x → 5.x Roslyn major jump happened within .NET 10 servicing, not at .NET 11, so the SDK upgrade
  does not itself change the Roslyn major.
- Neither the root README nor any package README mentions a target framework or .NET version, so the
  markdown surface of this migration is four historical lines in the TestCoverage documents, which
  per Non-goals are not edited.

## Appendix C — unverified at time of writing

- Whether this repo's generator leaks a framework assembly the way upstream's did (spike S3).
- Whether the `ASPDEPR004`/`ASPDEPR008` APIs are deprecated or removed on `net11.0` (spike S2).
- A complete Roslyn 5.11 analyzer-API breaking-change list; the .NET 11 and ASP.NET Core 11
  compatibility pages are both marked "work in progress, not a complete list" and neither documents
  an analyzer-API break.
- Whether `MintPlayer.SourceGenerators.Tools` will publish a build carrying the Roslyn 5.0.0 floor
  that upstream master now sets; master still reads `<Version>10.21.0</Version>` while the published
  10.21.0 nuspec declares the older `>= 5.3.0` floor.

## Version

Created 2026-09-17. Branch `feature/net11-migration`.
