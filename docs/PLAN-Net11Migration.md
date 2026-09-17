# Implementation plan: .NET 11 support

Companion to [PRD-Net11Migration.md](PRD-Net11Migration.md). Requirement IDs (`R1.1`, …) and problem
IDs (`P1`, …) refer to that document.

Branch: `feature/net11-migration`. One PR, per the repo convention — including the CI changes, the
pack fix, and anything the spikes turn up.

**Test runs are batched.** M0 runs throwaway probes, not the suite. M1-M4 are verified by building
and by reading; the only full `dotnet test` sweep is M5. The single exception is S2, which compiles
(does not run) two specific test files early, because its answer decides whether M3 is a one-line
change or a code change.

**The spikes are not optional and they come first.** Three of them (S2, S3, S5) can each invalidate a
milestone that would otherwise be written blind, and S3 reproduces a failure that has already
happened once upstream. None of them touch tracked files — they run in a scratch directory.

---

## M0 — Spikes (done, before implementation)

- [x] **S1 — Does an empty multi-targeted project pack the analyzer twice?**
      Scratch project, `net10.0;net11.0`, copy the `PackEndpointsGenerator` target shape from
      `Endpoints/MintPlayer.AspNetCore.Endpoints.csproj:61-71`, `dotnet pack`, unzip, list entries.
      Confirms or refutes the NU5118 prediction in R3.1 **and** establishes the fix (condition on
      `'$(TargetFramework)'=='net10.0'`, or hoist to a non-TFM-specific pack target) before touching
      the real csproj. Record the actual error text.

- [x] **S2 — Are the `ASPDEPR004`/`ASPDEPR008` APIs deprecated or removed on `net11.0`?**
      Copy `Tests/…/OpenSearch/OpenSearchTestHost.cs` and
      `Tests/…/MustChangePassword/MustChangePasswordCookieRoundTripTests.cs` into a scratch
      `net11.0` test project and compile. Deprecated → the existing `#pragma` lines stand and M3 is
      mechanical. Removed → those two test hosts need a real rewrite and M3 grows a milestone.
      **This is the single largest unknown in the estimate.**

- [x] **S3 — Does the Endpoints generator leak a framework assembly to consumers?**
      Reproduce the upstream shape from PR #182 (P3). Pack `MintPlayer.AspNetCore.Endpoints` as it
      stands today, install it into a scratch `net10.0` consumer and a scratch `net11.0` consumer from
      a local feed, and inspect the resolved compile references for
      `Microsoft.Extensions.DependencyInjection.Abstractions` or any other assembly arriving from
      `analyzers/dotnet/cs`. Upstream's equivalent produced 120 errors from one line.
      If it leaks on `net11.0`, R3.4 becomes real work and moves into M4.

- [x] **S4 — Does the `GetTargetPath` cross-project call survive an outer/inner build?**
      Multi-target a scratch copy of the Endpoints csproj and confirm `RemoveProperties` still stops
      the inner build's `TargetFramework` leaking into the `netstandard2.0` generator build (R3.2).
      Failure mode to watch for: the generator being built twice, or once per TFM with the wrong
      properties.

- [x] **S5 — Can `Microsoft.AspNetCore.Mvc.Testing` be TFM-conditioned cleanly?**
      Scratch `net10.0;net11.0` test project with a TFM-conditioned `PackageReference` (10.0.x /
      11.0.x), restore, and confirm no NU1605 downgrade or NU1608 version-conflict warning on either
      inner build (R4.1, and R4.3 which forbids new warnings). Also confirm an 11.0.x
      `Mvc.Testing` exists on nuget.org at RC.

- [x] **S6 — Which `rollForward` policy survives RC → GA without a second edit?**
      Try `global.json` with the exact RC SDK plus candidate `rollForward` values against the
      installed 10.0.112 / 10.0.401 / 11.0.100-rc.1 set, and reason about what happens when 11.0.100
      GA replaces the RC on a CI runner (R1.1, R1.3). Pick the policy that does not silently fall
      back to a .NET 10 SDK, since that would rebuild P1 in a new form.

**Gate — do not start M1 until these are answered:**
- [x] S1 has a reproduced error (or a clean pack) and a chosen fix.
- [x] S2 has a compile result, and M3's size is known.
- [x] S3 has a verified answer on both TFMs, and R3.4's cost is known.
- [x] S5 confirms a warning-free TFM-conditioned reference.
- [x] S6 has a chosen `rollForward`.

If any of these fail, fix the design here rather than proceeding. A pack or leak defect found in M6
costs a release; found in M0 it costs an afternoon.

## M0 outcome

**Gate result: PASS.** All six answered; no milestone was invalidated. Two PRD predictions were
wrong, both in the safe direction, and both are corrected in the PRD.

**S1 — reproduced, but it is a warning, not an error.** The predicted duplicate-file failure (PRD
R3.1, "NU5118-class pack failure") is real but non-fatal: `dotnet pack` exits 0 and emits two
`warning NU5118` — "File '…MintPlayer.SourceGenerators.Tools.dll' is not added because the package
already contains file 'analyzers\dotnet\cs\MintPlayer.SourceGenerators.Tools.dll'". NuGet
de-duplicates, and the resulting nupkg is **correct**: `lib/net10.0`, `lib/net11.0`, and one copy of
each analyzer. So multi-targeting would not have broken the package — it would have broken the
warning-clean build (R4.3), which is a lower-severity problem than the PRD claimed. It still must be
fixed.

**S1 — the obvious fix is wrong, and fails silently.** Hoisting the analyzer out of the per-TFM
mechanism onto `BeforeTargets="GenerateNuspec"` with `<None Pack="true">` (candidate B) packs with
zero warnings and produces a nupkg containing **no analyzer at all** — `lib/net10.0` and
`lib/net11.0` only. Exactly the failure the existing csproj comments at `…Endpoints.csproj:46-56`
warn about: "producing a nupkg with no analyzer in it and no error". Rejected. This is why the gate
requires unpacking the nupkg rather than reading the log.

**S1 — chosen fix.** Keep `TargetsForTfmSpecificContentInPackage`, and condition the target on the
first TFM, derived rather than hardcoded:

```xml
<_AnalyzerPackTfm>$(TargetFrameworks.Split(';')[0])</_AnalyzerPackTfm>
…
<Target Name="PackEndpointsGenerator" Condition="'$(TargetFramework)' == '$(_AnalyzerPackTfm)'">
```

Verified: zero NU5118, target runs once (`TFM=net10.0`), nupkg contains both `lib/` folders and one
analyzer plus `MintPlayer.SourceGenerators.Tools.dll`. `$(TargetFrameworks.Split(';')[0])` is a valid
MSBuild expression. Deriving the TFM rather than hardcoding `net10.0` means a future TFM reorder or
removal cannot silently produce candidate B's analyzer-less package.

**S2 — deprecated, not removed. M3 stays mechanical.** `ASPDEPR004` and `ASPDEPR008` are still
warnings on `net11.0`; a probe compiling the `new WebHostBuilder().UseTestServer()` shape without any
pragma builds successfully on both TFMs, emitting the same two diagnostics on each. The existing
`#pragma warning disable ASPDEPR004, ASPDEPR008` lines therefore keep working unchanged and need no
TFM condition. No test host needs rewriting. **This removes the largest unknown in the estimate.**

**S3 — no leak, on either TFM.** The current Endpoints package was packed to a local feed and
installed into a scratch consumer built at `net10.0` and at `net11.0`. Both compile with zero errors
and zero warnings; nothing from `analyzers/dotnet/cs` appears as a compile reference, and
`Microsoft.Extensions.DependencyInjection.Abstractions` does not leak. The generator runs in both
consumers and emits byte-identical `EndpointMapping.g.cs`. R3.4 costs nothing; the upstream failure
does not reproduce here. The likely reason is that this repo's generator carries its dependency via
`TargetPathWithTargetPlatformMoniker` with `IncludeRuntimeDependency="false"`, and packs only to
`analyzers/dotnet/cs` with `IncludeBuildOutput=false` — so there is no `lib/` asset to leak.

*One false alarm worth recording:* an intermediate S3 run failed on `net11.0` with 14 `CS0101`/`CS0111`
duplicate-definition errors. That was an artifact of the spike, not a defect — `EmitCompilerGeneratedFiles`
was writing into the project directory, so the `net10.0` run's `.g.cs` was globbed as source into the
`net11.0` build. Re-run with the output path outside the project tree, both TFMs are clean. Noted
because it looks exactly like a real .NET 11 generator break and is not one.

**S4 — survives.** The cross-project `GetTargetPath` call with
`RemoveProperties="TargetFramework;TargetFrameworks;RuntimeIdentifier"` resolved correctly when
invoked from a multi-targeted inner build: the generator built once as `netstandard2.0` and its
output was found and packed. No change needed to `…Endpoints.csproj:62-67`.

**S5 — clean.** `Microsoft.AspNetCore.Mvc.Testing 11.0.0-rc.1.26425.128` exists on nuget.org. A
TFM-conditioned pair (10.0.11 for `net10.0`, the rc.1 build for `net11.0`) restores and builds on
both inner builds with no NU1605 and no NU1608. Incidental finding: 10.0.x has moved on to **10.0.12**
while the repo pins 10.0.11.

**S6 — `latestFeature`, and roll-forward cannot fall back to .NET 10.** Verified directly: a
`global.json` requesting an SDK that is not installed fails hard (exit 155, "A compatible .NET SDK was
not found") rather than selecting an older major. So there is no silent-fallback risk to re-create P1.
Chosen: pin `11.0.100-rc.1.26425.128` with `"rollForward": "latestFeature"` and
`"allowPrerelease": true`, which stays within 11.0 and will accept GA `11.0.100` when it replaces the
RC on a runner, without a second edit. *Not verified* — the GA roll-forward itself, since 11.0.100 GA
is not installed anywhere yet; it is reasoned from the documented `latestFeature` semantics and must
be re-checked in November.

---

## M1 — SDK pinning and CI (P1, R1.1-R1.4)

Done first and on its own, because it is a live defect today and because every later milestone is
measured against whichever SDK is in effect.

- [ ] Add `global.json` pinning the .NET 11 SDK with the `rollForward` chosen in S6.
- [ ] `.github/workflows/pull-request.yml:33-35` — `actions/setup-dotnet@v5` gets a multi-line
      `dotnet-version` installing the .NET 11 SDK (exact version, per R1.3) and the .NET 10 runtime.
- [ ] `.github/workflows/publish-release.yml:27-29` — the same change.
- [ ] Leave `nuget.config` alone (R1.4) — confirm restore succeeds without a new feed.
- [ ] Leave the `MintPlayer/MintPlayer.Spark` coverage-upload steps untouched in this milestone.

**Gate — verified by building, not by testing:**
- [ ] `dotnet --version` in the repo root reports the pinned SDK.
- [ ] `dotnet restore` succeeds from nuget.org alone.
- [ ] The solution still builds on `net10.0` only, unchanged — M1 must not alter build output.

---

## M2 — Retarget the shippable projects (R2.1, R2.4)

- [ ] 14 shippable csprojs: `<TargetFramework>net10.0</TargetFramework>` →
      `<TargetFrameworks>net10.0;net11.0</TargetFrameworks>` (note the property rename).
      ChangePassword, Endpoints, Endpoints.Abstractions, Hsts, LoggerProviders, MustChangePassword,
      MustChangePassword.Abstractions, NoSniff, OpenSearch, OpenSearch.Abstractions, SitemapXml,
      SitemapXml.Abstractions, MintPlayer.Timestamps, SubDirectoryViews.
- [ ] `Endpoints.TestApp` — retarget too, since `TestAppEndToEndTests` consumes it via
      `WebApplicationFactory<Program>` and it must match the test project's TFMs.
- [ ] **Do not touch** `MintPlayer.AspNetCore.Endpoints.Generator.csproj` (R2.3). It stays
      `netstandard2.0`. If this milestone edits that file, the milestone is wrong.
- [ ] Do not bump `MintPlayer.SourceGenerators.Tools` or `MintPlayer.SourceGenerators(.Attributes)`
      (Non-goals).

**Gate:**
- [ ] `dotnet build -c Release -t:Rebuild` succeeds; both inner builds present in the log.
- [ ] Warning count is unchanged from the `net10.0` baseline, measured per R4.3 (`-t:Rebuild`,
      deduplicated). Any new warning is triaged here, not carried to M5.

---

## M3 — Retarget the test projects (R2.2, R4.1, R4.3, R4.4)

Size depends on S2.

- [ ] `Tests/MintPlayer.AspNetCore.Tools.Tests/…csproj:4` → `net10.0;net11.0`.
- [ ] `Tests/MintPlayer.AspNetCore.Endpoints.Generator.Tests/…csproj:4` → `net10.0;net11.0`.
- [ ] `…Tools.Tests.csproj:30` — TFM-condition `Microsoft.AspNetCore.Mvc.Testing` per S5.
- [ ] Apply whatever S2 concluded about `ASPDEPR004`/`ASPDEPR008`. If those APIs are removed on
      `net11.0`, rewrite the two affected test hosts; if merely deprecated, TFM-condition the
      suppressions rather than widening them (R4.3).
- [ ] Confirm the generator harness needs no change — its reference set comes from
      `TRUSTED_PLATFORM_ASSEMBLIES` (`…/EndpointGeneratorHarness.cs:40`) and follows the TFM
      automatically. Verify by reading, then by the M5 run.

**Gate:**
- [ ] Both test projects compile on both TFMs, warning-clean.
- [ ] `CoverageShapeTests`' 14 hardcoded assembly names still match the shipped set (R4.4).

---

## M4 — Packaging (R3.1-R3.4)

- [ ] Apply the S1 fix so `PackEndpointsGenerator`
      (`Endpoints/MintPlayer.AspNetCore.Endpoints.csproj:58,61-71`) emits the analyzer exactly once
      across both inner builds.
- [ ] Confirm S4's conclusion holds on the real csproj — `RemoveProperties` at
      `…Endpoints.csproj:62-67` still isolates the `netstandard2.0` generator build (R3.2).
- [ ] Apply whatever S3 turned up, if anything (R3.4).
- [ ] Update the stale-path comments at `…Endpoints.csproj:46-56` and
      `…Endpoints.Generator.csproj:38-46` — they describe a single-TFM world and will mislead the
      next reader. Those comments exist precisely because two hardcoded paths silently diverged once
      before.
- [ ] `<Version>` → `11.0.1-rc.0` on all 14 shippable packages, including `MintPlayer.Timestamps`
      which is on its own 10.0.0 today (R5.1, R2.4).

**Gate — verified by unpacking a real nupkg, not by reading csproj (Appendix A):**
- [ ] `dotnet pack -c Release` succeeds for the whole solution.
- [ ] Each shippable nupkg contains `lib/net10.0` and `lib/net11.0`.
- [ ] The Endpoints nupkg has exactly one analyzer assembly + `MintPlayer.SourceGenerators.Tools.dll`
      under `analyzers/dotnet/cs`, no duplicates.
- [ ] `snupkg` symbol packages still produced (commit 22c9cef fixed NU5017 here once already).

---

## M5 — The single test sweep (goals 2, 6; acceptance 1-3, 8)

The only full `dotnet test` run in the plan.

- [ ] `dotnet build -c Release -t:Rebuild` → log to a file, raw, unfiltered; then grep the file for
      warnings. Assert zero.
- [ ] `dotnet test --no-build -c Release --settings coverlet.runsettings --collect:"XPlat Code Coverage"`
      → log to a file. Expect the suite to run **twice**, once per TFM.
- [ ] Assert: zero failures and zero skips on `net10.0` and on `net11.0` independently. A pass on one
      TFM and a failure on the other is the whole point of this milestone and must not be averaged away.
- [ ] Compare the total against the 988 recorded in `docs/PRD-TestCoverage.md`. It should roughly
      double; a count *below* 988 on either TFM means a test silently stopped being discovered.
- [ ] Verify coverage cobertura files are produced for both TFMs and that the "Assert tests actually
      ran" guard (`pull-request.yml:61-74`) still fires correctly.
- [ ] Verify report paths still resolve with the five duplicated basenames under doubled output
      directories (acceptance 8) — `UseSourceLink=false` was set for exactly this reason.
- [ ] Triage anything the ASP.NET Core 11 breaking changes surface: response compression now always
      emitting `Vary: Accept-Encoding`, Kestrel protocol-compliance tightening, hosting OpenTelemetry
      tags on by default.

---

## M6 — Consumer proof and the release gate (acceptance 4-7; R5.2)

The acceptance criteria that cannot be proved from inside this repository.

- [ ] Scratch `net10.0` consumer, installs `MintPlayer.AspNetCore.Endpoints 11.0.1-rc.0` from a local
      feed, compiles, generated endpoints run (acceptance 4). **This is the criterion that proves
      dropping net10 was unnecessary.**
- [ ] Same consumer retargeted to `net11.0` (acceptance 5).
- [ ] The `net10.0` consumer built with the **.NET 10 SDK**, not the .NET 11 SDK targeting net10.0 —
      proving the generator still loads in a .NET 10 compiler host and no consumer is forced onto the
      .NET 11 SDK (acceptance 6, goal 4). Easy to get wrong by testing only with the newest SDK
      installed.
- [ ] `dotnet --version` in repo root matches what the workflows install (acceptance 7).

**Release gate — a decision, not a step (R5.2):**

`publish-release.yml` pushes to nuget.org on every merge to `master`. Merging this PR ships it.
Before merge, decide explicitly:

- [ ] Ship `11.0.1-rc.0` now, built on an RC SDK under the go-live licence; or
- [ ] Hold the merge until .NET 11 GA (2026-11-10), re-run M5 against the GA SDK, and ship then.

Either is defensible. What is not defensible is discovering which one happened by looking at
nuget.org afterwards.

---

## Deliberately out of scope

Genuinely not being done — not a parking lot for deferred work.

- Dropping `net10.0`, now or as a follow-up. Rejected on the merits in PRD P4.
- Bumping `MintPlayer.SourceGenerators.Tools` past 10.16.0 or `MintPlayer.SourceGenerators(.Attributes)`
  past 10.13.0. Would raise the minimum compiler host for consumers for no gain here.
- C# 15 / `LangVersion` changes.
- Editing the historical .NET 10 statements in `docs/PRD-TestCoverage.md` and
  `docs/PLAN-TestCoverage.md`. This document supersedes them.
- Introducing `Directory.Build.props` / `Directory.Packages.props` to centralise the TFM. Tempting
  while editing 16 files, but it would move `ContinuousIntegrationBuild` semantics that
  `coverlet.runsettings` depends on (`DeterministicReport=false` is only correct because that
  property sits on the `pack` step) — a real regression risk for zero benefit to this migration.
- Republishing or fixing the unlisted 11.0.0.
