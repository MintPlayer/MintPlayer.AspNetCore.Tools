# Implementation plan: code coverage for MintPlayer.AspNetCore.Tools

Companion to [PRD-TestCoverage.md](PRD-TestCoverage.md). Requirement IDs (`R1.1`, …) and
defect IDs (`D-G1`, …) refer to that document.

Branch: `feature/test-coverage`. One PR, per the repo convention.

**Test runs are batched.** Milestones M2–M8 are verified by reading the code and building
(`dotnet build -c Release`), not by running the suite after each one. The full sweep is
M10. Exception: M1 ends with a deliberate one-off coverage run, because it exists to
validate the report-path design and there is no point building eight suites on top of a
broken premise.

---

## M0 — Spikes (done, before implementation)

Recorded here because their outcomes are load-bearing. Full detail in PRD Appendix A.

- [x] **S1** Reference implementation: `f69b852` is superseded by `827a945`/PR #170; copy
      the current state. Extracted workflows, `coverlet.runsettings`, action inputs.
- [x] **S2** Upload contract from `C:\Repos\Coverage`: `POST /api/uploads` multipart,
      50 MB/request, format sniffed by root element, `202` ≠ parsed, `fileList` drives
      path matching, `/api/uploads/*` is the stable surface.
- [x] **S3** Repo survey: **zero test projects**; no `Directory.Build.props`, no central
      package management, no `.runsettings`, no `global.json`; all `.csproj` in the `.sln`.
- [x] **S4** Build spike: Release build and `pack --no-build` both succeed; current
      `dotnet test` compiles nothing (no `bin/Debug` anywhere); SourceLink **active**;
      `ContinuousIntegrationBuild` reaches only pack; generated `.g.cs` is **in-memory
      only**.
- [x] **S5** Path-ambiguity spike: five duplicated basenames; all four generator
      basenames unique repo-wide.
- [x] **S6** Test inventories for all 13 packages, with per-group infrastructure needs and
      the 27-defect register.

---

## M1 — CI plumbing and the report-path proof

Everything here is independent of the tests existing, and lands first so the rest is built
on a verified premise.

- [x] `coverlet.runsettings` at the repo root: `Format=cobertura`,
      `UseSourceLink=false`, `DeterministicReport=false`, `IncludeTestAssembly=false`,
      `Exclude` = the TestApp assembly (R2.3). Each setting carries a comment with its
      reason and, where relevant, the coupling that would invalidate it (R1.3). Plus
      `ExcludeByFile=**/obj/**`, decided by measurement during the gate below (R1.4).
- [x] `.gitignore`: add `/coverage/` (R5.3).
- [x] Both workflows: replace the Test step with the R1.1 command; add the `Upload
      coverage` step (before `Pack` in `publish-release.yml`, R1.5); add `concurrency`;
      add `permissions`; bump `checkout@v5` / `setup-dotnet@v5` (R5.4).
- [x] CI guard: fail if no `coverage/**/coverage.cobertura.xml` was produced (R5.1).
- [x] `README.md` coverage badge (R5.5).
- [x] Two test projects, each with `IsPackable=false`, `IsTestProject=true`, the R1.2
      package set, `FrameworkReference Microsoft.AspNetCore.App`, and a note pointing at
      the R5.2 path invariant:
      - `Tests/MintPlayer.AspNetCore.Tools.Tests` — `ProjectReference` to **every**
        runtime library plus the TestApp (R2.1, R2.3).
      - `Tests/MintPlayer.AspNetCore.Endpoints.Generator.Tests` — plain
        `ProjectReference` to the generator so its DLL **and PDB** land in the output root
        (R3.7), `Microsoft.CodeAnalysis.CSharp` 4.14.0, `MintPlayer.SourceGenerators.Tools`
        referenced directly (the generator's `PrivateAssets="all"` blocks the transitive
        flow).
- [x] Both added to `MintPlayer.AspNetCore.Tools.sln` under a `Tests` solution folder.
- [x] `[assembly: InternalsVisibleTo]` on Hsts, LoggerProviders, MustChangePassword,
      SitemapXml, OpenSearch and the Generator (R3.3).
- [x] One smoke test per project, plus the `RecordingResponseFeature` (R3.1) and the
      shared `Infrastructure/` helpers the inventories call for.

**Gate — run the coverage collection once and verify the premise:**

- [x] a `coverage.cobertura.xml` is produced per test project;
- [x] paths in the runtime-library report are repo-root-relative
      (`Hsts/MintPlayer.AspNetCore.Hsts/ImprovedHstsMiddleware.cs`, not
      `ImprovedHstsMiddleware.cs`);
- [x] simulate the server's suffix match against `git ls-files` — **zero ambiguous, zero
      unmatched**;
- [x] the generator assembly appears in its report at all (proving R3.7) — it does, with
      248/445 lines covered from 5 harness tests.

**Gate result: PASS.** 65 matched, 0 ambiguous, 0 unmatched; all 14 shippable assemblies
present; TestApp correctly excluded. Two predictions were wrong and are corrected in PRD
Appendix C. The `Generate_OutputCompiles` harness test also found a new defect on its
first run (D-G25).

If any of these fail, fix the design here rather than proceeding.

---

## M2 — Middleware and small libraries

`Hsts`, `NoSniff`, `ChangePassword`, `SubDirectoryViews`.

- [x] `ImprovedHstsMiddlewareTests` + `ImprovedHstsHeaderValueTests` + `UseImprovedHstsTests`
      — ctor guards and their order, HTTPS gate, excluded-host matching (casing, port),
      header-value assembly across `MaxAge`/`IncludeSubDomains`/`Preload`, invariant
      culture under `nl-BE` (R4.2).
- [x] `NoSniffMiddlewareTests` + `UseNoSniffTests` — unconditional header, deferral,
      overwrite, `next` propagation.
- [x] `MapChangePasswordTests` + `ChangePasswordEndpointBehaviourTests` — both overloads,
      the literal `/.well-known/change-password` pattern, GET-only, 302 + `Location`,
      async awaiting, factory called per request.
- [x] `ConfigureViewsInSubfolderTests` + `…WithRealDefaultsTests` — prefixing, slash
      trimming, and the ordering trap (called before `AddControllersWithViews()` it
      silently does nothing — D-M39, the highest-value test in that library).
- [x] `TestServer` groups (R3.2): HSTS header survives a downstream `Headers.Clear()`
      (the library's entire reason to exist vs. the built-in), NoSniff on the wire and
      `OnStarting` ordering, `.well-known` routing, `POST` → 405.
- [x] Tests pinning current behaviour for D-M3, D-M4, D-M6, D-M18, D-M19, D-M20, D-M36,
      D-M37, D-M39, each named `…_KnownBug`/`…_KnownGap` so M9 can flip them.

---

## M3 — LoggerProviders

- [x] `LoggerExtensionsTests`, `FileLoggerOptionsTests`, `LoggerFileProviderTests`,
      `FileLoggerTests`, `FileLoggerEndToEndTests`.
- [x] Temp-directory fixture under `Path.GetTempPath()`; **no** `FakeLogger` — this
      library *is* the sink, so a fake logger would replace the thing under test.
- [x] Extension allowlist including the case-sensitivity gap (D-M11), the `Log.txt`
      fallback, append-not-truncate, unwritable path.
- [x] Pin the two significant gaps: **exceptions are never written** (D-M7) and no
      level/category/timestamp (D-M8).
- [x] The concurrency test (D-M14) is written `[Fact(Skip=…)]` referencing D-M14 — it is a
      bug reproduction, not a stable regression test, until M9 fixes it.

---

## M4 — MustChangePassword

- [x] `TestUserStore` + real `UserManager` (R3.4), recording `IAuthenticationService`.
- [x] `MustChangePasswordConstantsTests` — pin the wire value `"Identity.ChangePassword"`;
      changing it silently invalidates live cookies.
- [x] `AuthenticationBuilderExtensionsTests` — scheme registration, cookie name, the
      5-minute expiry, no disturbance to other schemes.
- [x] `AddMustChangePasswordServiceRegistrationTests` — including the missing
      `IHttpContextAccessor` (D-M22).
- [x] `MustChangePasswordServiceSignInTests` / `…PerformTests` — claim contents including
      the `OldPassword` claim (D-M23), every failure path, sign-out ordering, and the
      success path *not* signing out (D-M26).
- [x] `MustChangePasswordCookieRoundTripTests` on `TestServer` — the one place a real
      cookie + data protection round trip earns its keep.
- [x] `IMustChangePasswordServiceContractTests` — constraint and shape pins for the
      zero-IL Abstractions package.

---

## M5 — SitemapXml, Abstractions, Timestamps

`MintPlayer.Timestamps` gets **no** separate project or namespace — it is four interfaces
with zero executable lines, and its real contract (`T : IUpdateTimestamp` as consumed by
`GetSitemapIndex`) is exercised by the service tests. Two reflection shape-guards live in
a `Timestamps/` folder.

- [x] Serialization suites: `UrlSetSerializationTests`, `SitemapIndexSerializationTests`,
      `VideoSerializationTests`, `ImageSerializationTests`, `LinkSerializationTests`,
      `ChangeFreqTests`.
- [x] Shared helpers: `SerializeToString`/`SerializeToDocument`/`WithCulture`, `XNamespace`
      constants. Assert through `XDocument`, raw strings only where the lexical form is
      the subject (R4.3).
- [x] R4.1 discipline: no hard-coded UTC offsets; the one deliberate `Local`-kind test
      derives its expectation from `TimeZoneInfo.Local.GetUtcOffset(...)`.
- [x] `SitemapXmlServiceTests` — `PageCount` boundaries (D-S6, D-S7), `GetSitemapIndex`
      slicing, the surprising `urlFunc(perPage, page)` argument order, laziness, and the
      multi-enumeration cost (D-S8) via a counting enumerable.
- [x] `SitemapXmlOutputFormatterTests`, `AddSitemapXmlTests`,
      `MapDefaultSitemapXmlStylesheetTests`, `SitemapEndToEndTests`.
- [x] `EmbeddedResource_ExistsWithExpectedManifestName` (R4.4) — cheapest high-value test
      in the repo.
- [x] Optional, decided during the milestone: validate output against vendored
      `sitemap.xsd`/`siteindex.xsd` fixtures (embedded, never fetched). This catches D-S1
      and D-S3 automatically.
- [x] Pin current behaviour for D-S1, D-S3, D-S4, D-S6–S14, D-S24.

---

## M6 — OpenSearch and Abstractions

- [x] `OpenSearchDescriptionSerializationTests` — including `SearchForm`'s empty namespace
      (D-S15) and invariant numeric attributes under a native-digits culture.
- [x] `OpenSearchOutputFormatterTests` — notably `CanWriteType(typeof(object[])) == false`,
      which is what lets the suggest endpoint fall through to JSON.
- [x] `HttpContextExtensionsTests` — `DeclaredType = typeof(TModel)` is what makes the
      formatter match; guards and the `?? new RouteData()` branch.
- [x] `AddOpenSearchTests`, `MapOpenSearchValidationTests`.
- [x] `OsdxEndpointTests`, `SuggestEndpointTests`, `SearchEndpointTests` on `TestServer` —
      pinning D-S18 (no `{searchTerms}` macro; route value always `null`), D-S19 (the
      library's own assembly name in `Content-Disposition`), D-S20, D-S21 (browser
      `Accept: text/html` → 406, which is the primary real-world caller), D-S22, D-S23.
- [x] `OpenSearchContractTests` + a note on the `RedirectResult` leak in the abstraction.

---

## M7 — Endpoints runtime, Abstractions, and TestApp end-to-end

- [x] `HttpMethodInterfaceTests` — the verb literals across all 15 interfaces; the files
      are literal copies, so a copy-paste verb error in one arity is a real risk.
- [x] `EndpointBaseDefaultsTests`, `EndpointDescriptorTests`, `AttributeTests`,
      `NonBodyEndpointTests` (including the reflection pin that `BindRequestAsync` is
      still abstract — one deleted keyword from silently breaking).
- [x] `EndpointBaseTests` + `BodyEndpointBindingTests` — the densest real logic: JSON
      fallback vs. MVC formatter path, `CanRead` skipping, `NoValue`/`Failure` handling,
      the drained-body fallthrough (D-G7), the null-laundering (D-G5).
- [x] `EndpointRouteBuilderExtensionsTests` — route pattern, methods, attribute→metadata
      transfer, the `Configure` hook and its ordering, and that `IMemberOf<TGroup>` is
      ignored on manual registration (D-G14).
- [x] `EndpointInvocationTests` on `TestServer` — per-request resolution, scoped injection,
      disposal, and that `Dispose()` is never called for a typed endpoint (D-G6).
- [x] `TestAppEndToEndTests` on `WebApplicationFactory<Program>` — all 12 shapes the
      sample demonstrates, group tags, `Produces` metadata including the `201` override,
      the descriptor list, and the trailing-slash route question (D-G19).
- [x] Fixtures for the shapes the sample never exercises: PATCH, `IGetEndpoint<TReq>`,
      constructor injection, disposal overrides, endpoint-level `Configure`,
      `EndpointNameAttribute`, and MVC content negotiation (the sample never calls
      `AddControllers()`, so that whole branch is dead in it).

---

## M8 — The source generator

- [x] Raw `CSharpGeneratorDriver` harness (R3.5): references from
      `TRUSTED_PLATFORM_ASSEMBLIES` (R3.6), `LanguageVersion.Latest`,
      `NullableContextOptions.Enable`, and a deliberate `assemblyName:` since it drives
      `GetMethodName()`.
- [x] `AssemblyInfoTests`, `EndpointInfoTests` — the pure string functions, including the
      crash inputs (D-G12) and the collision (D-G13).
- [x] `EndpointDiscoveryTests` — every shape and every guard: abstract skip, namespace
      filters, the `StartsWith("IEndpoint")` false positive, interfaces-via-base-class
      (D-G10), split partials (D-G9), the two-group drop (D-G4), zero endpoints (D-G11).
- [x] `EndpointGroupingTests` — nesting, the root-group discovery asymmetry, the
      multi-parent group becoming a root (D-G15), and **determinism**: run the driver
      twice on the same compilation and diff the text (D-G18).
- [x] `EndpointMethodNameTests`, `EndpointMetadataEmissionTests` — including
      `Generate_OutputCompiles_WithZeroErrors`, the single highest-value generator test:
      feed the generated trees into a second compilation and assert no errors. Nothing
      else catches a malformed emit.
- [x] `EndpointGeneratorIncrementalTests` with `TrackIncrementalGeneratorSteps` — this is
      what all the `IEquatable` ceremony in `Models.cs` exists for, and it is currently
      unverified.
- [x] The cyclic-group test (D-G17) stays `[Fact(Skip=…)]` until M9 adds the guard — an
      uncatchable `StackOverflowException` would take the whole test run down with it.

---

## M2–M8 outcome

**747 tests, 745 passing, 2 deliberately skipped, 0 failures.** Coverage 88.7% (784/884
lines) max-merged as the server computes it. Report paths re-verified: 65 matched,
0 ambiguous, 0 unmatched.

Two things the suite forced back into M1's work:

- `ExcludeByFile` widened from `**/obj/**` to `**/*.g.cs,**/obj/**`. The
  MintPlayer.SourceGenerators generated documents moved from `obj/` to a TEMP directory
  between runs; sitting outside the repo they dropped `<sources>` from the repo root to
  `C:/` and broke all 49 previously-resolvable paths at once. The first pattern was
  fitted to an unstable observation — see PRD Appendix C.
- PRD R3.2's `new WebHostBuilder().UseTestServer()` is deprecated on .NET 10
  (ASPDEPR004/008); the suite uses `new HostBuilder().ConfigureWebHost(…)`.

Seven of the 27 read-derived defects did not survive execution (3 were not defects,
4 had the wrong mechanism or symptom), and 22 new ones were found. All recorded in the
PRD's corrections and additions sections.

---

## M9 — Fix the defects

Runs *after* the tests exist, so every fix shows up as a test flipping from
`…_KnownBug` to an assertion of correct behaviour.

**Scope decision (owner, after the register grew to ~50):** fix **everything**, breaking
changes included, with a **major version bump to 11.0.0** across affected packages. The
alternative — landing coverage plus a documented register and fixing later — was
considered and declined. The argument for doing it now is that the tests exist and pin
current behaviour, which is the cheapest moment a fix will ever have.

- [x] **Serious first:** D-G1 (wire up the three diagnostics via `IDiagnosticReporter` +
      `LocationExtensions.FromSymbol`), D-G17 (visited-set + diagnostic), D-S18 (both
      halves of search/suggest), D-S1 and D-S15 (XML namespaces), D-M23 (stop putting the
      plaintext password in a cookie).
- [x] **Correctness:** D-G18 (sort emission), D-G4–G16 as listed, D-S6–S8, D-M7, D-M14,
      D-M22, D-M26, D-M36, D-M37, D-M39, D-M18.
- [x] **Robustness/consistency:** the grouped list in the PRD — guards, bare `throw new
      Exception()`, `CanWriteType` subclassing, double registration, escaping,
      `XmlWriterSettings` mutation, logger ergonomics, generator `O(n²)` and metadata
      leakage.
- [x] **Docs/metadata:** the two wrong READMEs, the wrong `PackageTags`, the malformed
      `PackageProjectUrl` in every csproj.
- [x] Un-skip the D-M14 and D-G17 tests; flip every `…_KnownBug` name.
- [x] **Not** the `[decision]` items — those wait on the owner (see PRD).

---

## M10 — Measure, tune, verify

- [x] Full suite: `dotnet restore` → `dotnet build -c Release --no-restore` → the R1.1
      command. Zero failures.
- [x] **`ExcludeByFile` decided by measurement** — pulled forward into M1, because the
      gate demanded zero unmatched paths. A coverlet glob **does** match virtual paths with
      no file on disk; `**/obj/**` removes 8 files / 19 coverable lines. Measurements in
      PRD Appendix C.
- [x] Re-measure it once the full suite exists, in case the shape changed.
- [x] Re-verify acceptance criteria 2–5 against the final reports.
- [x] R4 audit grep across every new test: `UriKind`, `[A-Z]:\\`, `Environment.NewLine`,
      `ToLower()`/`ToUpper()` without a culture, hard-coded `+01:00`/`+00:00`. The sibling
      recorded that naming a suspect is not the same as auditing the class of problem —
      its predicted hazard passed and an unremarkable `Uri.TryCreate` bit instead. This
      grep comes *before* declaring done.
- [x] Record the resulting coverage figures in the PRD (`## Results`).
- [x] Open the PR.

## M9–M10 outcome

**988 tests, 0 failures, 0 skipped. 98.9% coverage (1393/1408). Zero build warnings, from 206.**
13 of the 14 instrumented assemblies are at 100%; `MintPlayer.Timestamps` has no executable IL.
Path gate: 81 matched, 0 ambiguous, 0 unmatched. `dotnet pack` verified, including that the
Endpoints package still ships `analyzers/dotnet/cs/…Generator.dll` (the 22c9cef/NU5017 area).

Fourteen packages bumped to **11.0.0**; `MintPlayer.Timestamps` stays at 10.0.0 because it has
literally no changes, and bumping it would make consumers update for nothing.

Fixing disproved four more predictions on top of the seven that executing disproved — including one
of the register's own prescribed fixes (D-S21) and one defect that turned out not to exist with a
fix that could not be written (D-G22). All recorded in the PRD's two corrections sections.

---

## Deliberately out of scope

Genuinely not being done — not a parking lot for deferred work.

- A coverage gate / merge-blocking threshold (a repo setting, once a baseline exists).
- ReportGenerator, HTML output, artifact uploads — the service renders.
- The `[decision]` breaking changes in the PRD.
- Resolving relative URLs against the request URI, adding spec values beyond what a fix
  requires, or any other capability addition dressed up as a defect fix.
- XSLT execution tests: the shipped `sitemap.xsl` declares `version="2.0"` and
  `XslCompiledTransform` is 1.0-only, so its rendered output is unverifiable in .NET.
  Noted as a finding (the declaration is likely downgradeable to 1.0, and the stylesheet
  pulls CSS from a third-party CDN — both worth their own issue).
- MSBuild-level packaging tests (pack the nupkg, assert `analyzers/dotnet/cs` contents).
  Out of xunit's reach; a CI step if wanted.
