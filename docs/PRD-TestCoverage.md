# PRD: Code coverage for MintPlayer.AspNetCore.Tools

## Overview

Add code-coverage collection to CI and upload the report to `coverage.mintplayer.com`,
mirroring what `MintPlayer/MintPlayer.Dotnet.Tools` did in `f69b852` (superseded by
`827a945` / PR #170 — the *current* state of that repo is what is copied here, not the
first cut).

The difference from the sibling repo is the starting point. There, coverage plumbing was
added to an existing suite of 22 test projects and 1,723 tests. **Here there are no tests
at all.** `dotnet test` has been in both workflows since they were written and has never
executed anything.

So this is not a CI change with some tests attached. It is a test suite with a CI change
attached, and most of the risk is in the parts that fail *silently*.

## Problem statement

### P1 — CI has been green by vacuity

Both workflows run `dotnet test --no-restore --verbosity normal`. Verified locally on the
.NET 10 SDK (10.0.400): with zero test projects in the solution, that command resolves the
`VSTest` target against the `.sln`, finds no project carrying `IsTestProject`, exits **0**,
and prints **nothing at all** — not even a "no test projects found" diagnostic.

```
$ dotnet test --no-restore --no-build --configuration Release
=== EXIT CODE: 0 === SECONDS: 3.70 ===
(zero lines of output)
```

The only real gate in CI today is that `dotnet build --configuration Release` succeeds.

This matters beyond the missing tests: **a misconfigured test project is
indistinguishable from a passing one.** A test project added to disk but not to the
`.sln` produces exactly the output above. That failure mode has to be closed by a
positive assertion in CI, not by trusting a green check.

### P2 — ~2,330 lines of shipped library code has never been executed by a test

75 tracked `.cs` files across 13 shippable packages:

| Area | LOC | Notes |
|---|---:|---|
| Endpoints (runtime + abstractions + generator) | 1,172 | includes a Roslyn incremental generator |
| SitemapXml (+ abstractions, + Timestamps) | 450 | XML serialization, date formatting |
| OpenSearch (+ abstractions) | 277 | XML serialization, endpoint routing |
| MustChangePassword (+ abstractions) | 171 | ASP.NET Identity, auth cookies |
| Hsts | 98 | middleware |
| LoggerProviders | 84 | file logging |
| NoSniff | 30 | middleware |
| ChangePassword | 28 | endpoint mapping |
| SubDirectoryViews | 21 | Razor view-location expander |

Reading this code to plan the tests turned up **27 defects**, several of which make a
shipped feature non-functional. Executing it then added **21 more** and disproved **3** — see
"Corrections to this register" below. They are all registered there. The coverage number is the
means; finding these is the actual value.

### P3 — Two structural traps that fail silently

Both were found by investigation, not by reasoning from the sibling repo, and both would
have produced a plausible-looking green build with wrong data.

**P3.1 — Per-library test projects would silently delete files from the report.**
Coverlet sets the Cobertura `<source>` element to the longest common directory prefix of
all instrumented documents. A test project covering exactly one library makes that prefix
the library's own folder, so every path in the report becomes a bare filename. The
coverage server suffix-matches report paths against `git ls-files` and requires a
**unique** match; ambiguous paths are dropped, silently, and excluded from the totals
(`CoverageMerger.Summarize(files.Where(f => f.Matched))`).

This repo has five duplicated basenames:

| Basename | Locations |
|---|---|
| `ServiceCollectionExtensions.cs` | `MustChangePassword/…/Extensions/`, `SubDirectoryViews/…/` |
| `Image.cs` | `OpenSearch/…/Data/`, `SitemapXml.Abstractions/Data/` |
| `Url.cs` | `OpenSearch/…/Data/`, `SitemapXml.Abstractions/Data/` |
| `StringExtensions.cs` | `OpenSearch/…/Extensions/`, `SitemapXml/…/Extensions/` |
| `XmlSerializerOutputFormatter.cs` | `OpenSearch/…/Formatters/`, `SitemapXml/…/Formatters/` |

The worst case is `SubDirectoryViews`, whose **only** source file is
`ServiceCollectionExtensions.cs`: a dedicated `SubDirectoryViews.Tests` project would
make the entire library vanish from the report with no error anywhere.

**P3.2 — `DefaultHttpContext` cannot observe the headers these middlewares set.**
Both `ImprovedHstsMiddleware` and `NoSniffMiddleware` assign their header inside
`Response.OnStarting(...)`. The default `HttpResponseFeature.OnStarting(callback, state)`
is an **empty method** — the callback is discarded and never invoked. So:

- a test asserting the header *is* set fails, though the middleware is correct;
- a test asserting the header is *absent* passes **for the wrong reason**.

The second is the dangerous one: it is how you ship a test suite that proves nothing.

## Goals

1. Coverage collected on every PR and every push to `master`, uploaded to
   `coverage.mintplayer.com`, with the check runs the service publishes.
2. A test suite over **all 13 shippable packages**, such that no shipped assembly is
   missing from the report without a documented reason.
3. Report paths that the coverage server can resolve — repo-root-relative, zero unmatched
   files.
4. The defects found during investigation fixed, so the tests pin correct behaviour
   rather than blessing bugs.
5. A coverage number that means what it says: measured against the Release binaries that
   ship, with nothing in the denominator that isn't shipped code.

## Non-goals

- A coverage *gate* / merge-blocking threshold. The service publishes `coverage/project`
  and `coverage/patch` check runs; whether branch protection requires them is a repo
  setting, decided separately once a baseline exists.
- ReportGenerator, HTML reports, or artifact uploads. Raw Cobertura goes straight to the
  service, which parses, merges and renders. Nothing to build here.
- Chasing 100%. Several branches are unreachable by construction and are documented as
  such rather than contorted around.
- Breaking API changes. Registered below under "Requires a decision", not implemented.

## Requirements

### R1 — Coverage plumbing

**R1.1 — `dotnet test` must run against the Release build, without rebuilding.**
`--no-restore` does **not** imply `--no-build`. Without `--no-build`, `dotnet test`
rebuilds the whole solution in **Debug** and coverage is measured against those binaries
while `dotnet pack` ships the Release ones.

In the sibling repo this was a measured 88s → 32s regression on an existing suite. **Here
it is preventive, not a saving**: with zero test projects the current Test step compiles
nothing (verified: no `bin/Debug` or `obj/Debug` directory exists anywhere after a full
CI sequence). The Debug rebuild is what we would acquire the moment the first test
project lands. The final form:

```
dotnet test --no-restore --no-build --configuration Release
  --settings coverlet.runsettings
  --collect:"XPlat Code Coverage" --results-directory coverage
```

`--verbosity normal` is dropped; in the sibling it produced 1.1 MB of raw `csc` command
lines per run.

**R1.2 — collector, not MSBuild.** `coverlet.collector` + `--collect:"XPlat Code
Coverage"` on the classic VSTest path. This is a greenfield choice here (no test project
exists to constrain it); it is chosen to match the sibling repo so the runsettings and
workflow are portable, and because Microsoft.Testing.Platform changes the shape of both
`--settings` and the collector arguments.

Versions, verified together in the sibling repo and confirmed present on nuget.org:

| Package | Version |
|---|---|
| `Microsoft.NET.Test.Sdk` | 18.9.0 |
| `xunit` | 2.9.3 |
| `xunit.runner.visualstudio` | 4.0.0 |
| `coverlet.collector` | 10.0.1 |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.11 |
| `Microsoft.CodeAnalysis.CSharp` | 4.14.0 (generator tests — must match the generator's own transitive version) |

**R1.3 — `coverlet.runsettings` at the repo root.** Settings live in a checked-in file,
not on a CI command line, so local runs and CI agree. Decisions, with the reasoning
recorded in comments in the file itself:

- `Format=cobertura` — the only format the service parses that coverlet emits.
- `UseSourceLink=false`, **pinned explicitly**. SourceLink is active here: all 16
  projects emit `obj/**/*.sourcelink.json`, via the SDK-bundled SourceLink triggered by
  the `RepositoryUrl`/`RepositoryType` metadata in every packable csproj (no
  `Microsoft.SourceLink.*` package reference anywhere). `true` rewrites `filename` to
  `raw.githubusercontent.com` URLs, and a URL tail makes every duplicated basename
  ambiguous — see P3.1.
- `DeterministicReport=false`, pinned with a comment naming the coupling. It is safe only
  because `ContinuousIntegrationBuild=true` currently lives on the `dotnet pack
  --no-build` command, where it never reaches the compiler. Verified: rebuilding one
  project with that flag collapses its document paths from
  `C:\Repos\MintPlayer.AspNetCore.Tools\*` to `/_/*`. If that property ever moves to a
  `Directory.Build.props`, this must flip to `true`.
- `IncludeTestAssembly=false` — test fixtures must not dilute the number.
- `ExcludeByFile` — **to be decided by measurement in M10, not copied.** See R1.4.
- No `Include` allowlist. Instrumentation is already scoped by "has a PDB in the test
  output"; an allowlist is a second place to forget a new package.
- No `ExcludeByAttribute` unless measurement justifies it. The sibling measured the usual
  snippet as actively harmful: `Obsolete` deleted 125 lines (Roslyn puts a synthetic
  `[Obsolete]` on `readonly ref struct`), `CompilerGeneratedAttribute` deleted 194 (every
  async state-machine body), `GeneratedCodeAttribute` 0.

**R1.4 — generated-code exclusion, measured here rather than inherited.** The sibling's
`ExcludeByFile=**/*.g.cs` removed 6 real files / 738 coverable lines. **That does not
transfer.** In this repo the external `MintPlayer.SourceGenerators` (10.13.0, consumed by
LoggerProviders, MustChangePassword, NoSniff, SitemapXml) writes **nothing to disk** —
verified: the only 16 `*.g.cs` files under `obj/` are the SDK's own `*.GlobalUsings.g.cs`,
and the `obj/Release/net10.0/MintPlayer.SourceGenerators/…/ClassNames.g.cs` paths that
appear in build warnings **do not exist as files**. `EmitCompilerGeneratedFiles` is set
nowhere.

Those synthetic paths still appear in the PDB and do show up in the report. Whether a
coverlet file-glob matches a path with no file behind it was flagged as behaviour to be
tested rather than assumed — **it was tested, in M1, and it does match.** Settled:
`ExcludeByFile=**/obj/**`, with the measurement in Appendix C. `ExcludeByAttribute` is
not needed.

**R1.5 — the upload step must never block a release.** In `publish-release.yml` the
upload sits before `Pack`/`PushNuget`. A coverage-service outage or a missing token must
not stop a NuGet publish, so `fail-ci-if-error: false`. On PRs, additionally
`continue-on-error: true`, and the step is skipped for fork PRs, which get no secrets.

Both steps guard on `hashFiles('coverage/**/coverage.cobertura.xml') != ''` so a run that
produced no report is a no-op rather than an upload of nothing. `disable-search: true`
is not cosmetic: with search on, a glob matching nothing falls back to auto-detection
over nine well-known patterns and would upload stray unparsable reports.

**R1.6 — auth: `COVERAGE_TOKEN`.** A `covt_…` token minted in the coverage UI, stored as
a repository secret, matching the sibling repo. This repo is public and the service also
supports tokenless GitHub Actions OIDC (`use-oidc: true` + `permissions: id-token:
write`, auto-provisioning public repos); that was considered and **not** chosen, to keep
one auth story across the two repos. Consequence to accept: the secret must exist before
CI works, and fork PRs cannot upload.

Note `url:` must be exactly `https://coverage.mintplayer.com` — the OIDC audience is the
server base URL, so the value must not be "normalised" even on the token path.

### R2 — Report paths the server can resolve

**R2.1 — one test project spanning all runtime libraries.** Direct consequence of P3.1.
Because the instrumented documents span `ChangePassword/`, `Endpoints/`, `Hsts/`,
`Logging/`, `MustChangePassword/`, `NoSniff/`, `OpenSearch/`, `SitemapXml/` and
`SubDirectoryViews/`, the longest common prefix is the repository root, so every report
path keeps its `<TopFolder>/<Project>/…/<File>.cs` shape and all five duplicated
basenames stay unambiguous — permanently, including future ones.

**R2.2 — the generator gets its own test project, and that is safe by verification.**
`MintPlayer.AspNetCore.Endpoints.Generator.Tests` instruments only the generator, so its
report paths are bare. Verified that all four generator basenames
(`DiagnosticDescriptors.cs`, `EndpointGenerator.cs`, `EndpointGenerator.Producer.cs`,
`Models.cs`) are unique repo-wide, so bare paths suffix-match uniquely.

This safety is luck, not structure. **The invariant is written down and guarded**: see
R5.2.

**R2.3 — the sample app must not enter the denominator.** The end-to-end tests need a
`ProjectReference` to `MintPlayer.AspNetCore.Endpoints.TestApp` for
`WebApplicationFactory<Program>`, which copies its DLL+PDB into the test output and makes
it instrumentable. It is `IsPackable=false` sample code and is excluded by assembly name
in the runsettings. This is the one deliberate `Exclude` entry, justified because the
sibling's worst measurement was exactly this failure: `SlnLaunch` had 48% of its
denominator made of unreachable generator code, reporting 46.9% where the honest figure
was 88.5%.

### R3 — Test infrastructure

**R3.1 — a recording `IHttpResponseFeature`.** Required by P3.2. A shared
`RecordingResponseFeature : IHttpResponseFeature` stores the `(callback, state)` pairs
`OnStarting` is handed and lets the test fire them explicitly. This keeps the HSTS and
NoSniff suites as fast unit tests instead of forcing a server.

Callback *ordering* must **not** be asserted against the fake. Real servers fire
`OnStarting` in reverse registration order (a stack in Kestrel); that fact is only
trustworthy against a real server loop, so it is asserted in the `TestServer` group.

**R3.2 — `Microsoft.AspNetCore.TestHost`, not `WebApplicationFactory`, for the library
integration tests.** Inline `new HostBuilder().ConfigureWebHost(w => w.UseTestServer())`
pipelines need no entry-point assembly and no content-root probing — the latter being a genuine
Windows-vs-Linux hazard (`UseContentRoot(@"..\..\..\App")` fails outright on Linux).
`WebApplicationFactory` is used in exactly one place, the TestApp end-to-end tests, where
a real entry point is the thing under test.

Note the builder shape: the older `new WebHostBuilder().UseTestServer()` is **deprecated on
.NET 10** (ASPDEPR004/ASPDEPR008) and emits warnings on every use, so the `HostBuilder` +
`ConfigureWebHost` form above is the one to copy.

**R3.3 — `InternalsVisibleTo` on four assemblies.** Hsts, LoggerProviders,
MustChangePassword and the Endpoints Generator expose their interesting types as
`internal`. Without it roughly a third of the reachable code in the first three is
untestable, and the generator's two pure string functions — which carry real bugs
(D-G12, D-G13) — cannot be unit-tested at all.

**R3.4 — no mocking library.** `UserManager<TUser>` has a nine-parameter constructor and
mocking it is the classic source of brittle Identity tests. A hand-written
`TestUserStore : IUserStore<T>, IUserPasswordStore<T>, IUserEmailStore<T>` behind a real
`UserManager` gives genuine `CheckPasswordAsync`/`ChangePasswordAsync` behaviour. Same
for `IOpenSearchService` (two methods) and `IAuthenticationService`.

**R3.5 — the generator is tested with a raw `CSharpGeneratorDriver`, not snapshots.**
Four reasons specific to this generator:

1. Its output order is **currently nondeterministic** (D-G18: `rootGroups` comes from
   iterating a `HashSet<string>`; nothing is sorted). Snapshot tests against unstable
   output produce red builds that are not bugs, and people start accepting snapshots
   reflexively.
2. The highest-value assertion is "the emitted code compiles" — feed the generated trees
   into a second `CSharpCompilation` and assert no errors. `Verify` cannot express that;
   the driver makes it three lines.
3. Diagnostics (once D-G1 is fixed) assert far more precisely as
   `Assert.Equal("MPEP001", d.Id)` plus a location check.
4. The crash cases (D-G12a `IndexOutOfRangeException`, D-G17 `StackOverflowException`)
   need `runResult.Results[0].Exception` inspection.

`Verify.Xunit` may be added later for three or four large golden files, **after** D-G18
is fixed.

**R3.6 — `Basic.Reference.Assemblies` is not sufficient and is not used.** It ships no
ASP.NET Core reference assemblies, and every generator fixture needs `HttpContext`,
`IResult`, `RouteHandlerBuilder`, `IEndpointRouteBuilder`. The reference set is built from
the test host's own `TRUSTED_PLATFORM_ASSEMBLIES` instead, with the test project carrying
`FrameworkReference Microsoft.AspNetCore.App`.

**R3.7 — generator coverage attribution.** Coverlet's collector scans the test host's
output directory **root** and does not recurse, so the generator DLL *and its PDB* must
land there. A plain `ProjectReference` (default `ReferenceOutputAssembly=true`) does
exactly that in both configurations, with no custom target —
`IncludeBuildOutput=false`/`DevelopmentDependency=true` in the generator csproj are
pack-time only and do not suppress the copy.

Critically: **any copy target must be unconditional, never
`Condition="'$(Configuration)'=='Debug'"`.** CI tests Release; a Debug-only condition
silently zeroes generator coverage. (This is the one place the upstream reference
implementation is wrong *for this repo*.)

Also: generator coverage can only come from tests that instantiate `new
EndpointGenerator()` in-process. The generator runs inside `csc` at build time, and an
analyzer execution is not observed by the test host's profiler — so the TestApp
end-to-end tests contribute **zero** generator coverage by construction.

### R4 — Cross-platform correctness

CI runs `ubuntu-latest`; development is Windows. Hazards found, ranked:

**R4.1 — `xsd:dateTime` with `DateTimeKind.Local` (will bite).**
`Video.ExpirationDate`/`PublicationDate` carry no `DataType`, so `XmlSerializer` writes
them via `RoundtripKind`, appending the **machine's** UTC offset: `+01:00` locally,
`+00:00` on the runner. **Rule: never hard-code an offset.** Use `Utc` (→ `Z`) or
`Unspecified` (→ none) in fixtures, or derive the expectation from
`TimeZoneInfo.Local.GetUtcOffset(value)`.

**R4.2 — culture.** `InvariantGlobalization` is off, so ICU is live on Linux and its data
differs from Windows NLS. A Belgian dev box defaults to `nl-BE`, the runner does not — so
a test that *accidentally* depends on `CurrentCulture` passes on one and fails on the
other. Every formatting test sets its culture explicitly. `Video.Rating` (`double?`) under
`nl-BE` is the highest-value case.

**R4.3 — assert through `XDocument`, not raw strings.** `\r\n` vs `\n`, and
`XmlWriterSettings.NewLineHandling` interacting with parser newline normalisation, make
raw-text comparison of multi-line XML content platform-dependent. Raw-string assertions
are confined to cases where the *lexical* form is the thing under test (dates, decimals,
escaping).

**R4.4 — case-sensitive embedded-resource lookup.** `MapDefaultSitemapXmlStylesheet`
looks up `"MintPlayer.AspNetCore.SitemapXml.Assets.sitemap.xsl"` as a hard-coded string
with no null check, and the csproj `Include` uses a backslash path. On Linux, case
matters. A one-line `GetManifestResourceStream(...) is not null` test is the cheapest
high-value test in the repo.

**R4.5 — do not fix URL validation with `Uri.TryCreate`.** Neither library uses `Uri`
today. The sibling repo's only CI-only failure was exactly this: `Uri.TryCreate("/p2",
UriKind.Absolute, out _)` is `false` on Windows and **`true` on Linux**, where a leading
slash is a valid Unix path and the value parses as `file:///p2`. The leading-slash
validation gaps below are fixed with `string`/`PathString` checks.

**R4.6 — `FileShare` is not enforced on Unix.** Added *after* it failed CI, not before. On
Windows the OS enforces share modes, so a foreign writer holding a file with `FileShare.Read`
makes the next writer fail with `IOException`; on Unix .NET does not translate share modes into
advisory locks, so the second writer simply succeeds. A test asserting the Windows behaviour
unconditionally passes locally and fails on the runner.

Where a platform genuinely differs, **split the assertion and say why** rather than skipping the
test: the logger's real contract — never silently drop a line — holds on both platforms, and only
the mechanism differs.

Before the suite is called done, grep every new test for `UriKind`, drive letters,
`Environment.NewLine`, culture-sensitive casing, **and `FileShare`/`FileAccess` assertions**. The
sibling recorded that naming a suspect is not the same as auditing the class of problem — its
predicted hazard passed and an unremarkable `Uri.TryCreate` bit instead.

**That lesson then repeated here, one level up.** The R4 list was written from the sibling's
experience; the audit grepped exactly the patterns it named; it came back clean; and CI failed on
a fifth pattern nobody had listed. **A grep for known patterns audits the patterns, not the class.**

The habit this project keeps arriving at from different directions is the durable one: *a claim
about framework behaviour is a hypothesis until the other platform has executed it.* The cheap way
to buy that is to let CI run once before believing the suite is finished — which is what happened,
and it cost one commit rather than a bad merge.

### R5 — Repo hygiene and guards

**R5.1 — a positive assertion that tests actually ran.** P1 means a green `dotnet test`
proves nothing. CI must fail if no coverage report was produced, and the report must
contain a plausible number of files. Without this, a test project dropped from the `.sln`
reads as a pass forever.

**R5.2 — the path-shape invariant, written down and guarded.** The rule is: *the
instrumented libraries in one test project must have a longest common directory prefix
short enough that every remaining relative path is a unique suffix within `git
ls-files`.* Its correctness currently depends on basename trivia nobody will re-check on
the next PR, so it gets a comment in the runsettings, a note in each test csproj, and a
check that no report path is ambiguous.

**R5.3 — `/coverage/` in `.gitignore`.** The existing entries (`coverage*.json`,
`coverage*.xml`, `coverage*.info`) do not cover a results *directory* named `coverage`.

**R5.4 — `concurrency` on both workflows.** Neither has one today, so superseded pushes
keep burning runners and racing uploads for the same commit. Also bump
`actions/checkout@v4`→`v5` and `actions/setup-dotnet@v4`→`v5`, and add explicit
`permissions:` blocks (least privilege; the upload action needs no `checks: write` — the
service publishes check runs itself through its GitHub App).

**R5.5 — a coverage badge** in `README.md`, linking to the report.

**R5.6 — no coverage on feature-branch pushes.** Only `pull-request.yml` and
`publish-release.yml` collect and upload. Otherwise a feature-branch push reports a third
build for the same commit.

## Out-of-band prerequisites

These are not code and must be done by a human, or CI's upload step will warn and skip:

1. **Mint a `covt_…` upload token** in the coverage UI (account- or repo-scoped) and add
   it as the repository secret **`COVERAGE_TOKEN`**.
2. **Install the Coverage GitHub App** on `MintPlayer/MintPlayer.AspNetCore.Tools` with
   **Checks: Read & write** and **Pull requests: Read & write**, and accept the
   permissions on the installation. Without it the `coverage/project` and
   `coverage/patch` check runs come back unavailable. Note `FeedbackState: Failed` is
   terminal after 5 attempts and fixing the App later does **not** retroactively add
   check runs to existing builds.

## Defect register

Found by reading the code while planning the tests. Numbered by area:
`D-S*` SitemapXml/OpenSearch, `D-M*` middleware/logging/identity, `D-G*` Endpoints and
the generator.

Per the repo's one-PR convention these are fixed in this PR, **except** those marked
**[decision]**, which are breaking API changes on published packages and are the owner's
call.

### Serious — a shipped feature does not work

| ID | Defect |
|---|---|
| D-G1 | **The generator's three declared diagnostics are never reported.** `DiagnosticDescriptors` is `internal static` and referenced from nowhere; `<NoWarn>RS2008</NoWarn>` hid it. `README.md` documents MPEP001/002/003 as emitted. All three conditions *are* computed (`IsPartial`, `HasExistingBaseClass`, `HasMultipleGroups`) and used only to silently skip emission, leaving the user a raw `CS0115`/`CS0263`. The Tools package already provides `IDiagnosticReporter` + `ReportDiagnostics` and `LocationExtensions.FromSymbol`, so this is wiring, not new machinery. |
| D-S18 | **OpenSearch search and suggest are non-functional, both halves.** The OSDX templates are emitted with no `{searchTerms}` macro, so a client has nowhere to put the query; *and* the handlers read `context.GetRouteValue("searchTerms")` on routes registered as literal patterns with no `{searchTerms}` token, so it is `null` on every request — including `?q=abc`. Fix together: emit `?q={{searchTerms}}` and read `Request.Query["q"]`. |
| D-G17 | **Cyclic groups stack-overflow `csc`.** `EmitGroupTree` recurses through `childGroups` with no visited set. `A : IMemberOf<B>` + `B : IMemberOf<A>` ⇒ unbounded recursion ⇒ uncatchable `StackOverflowException` that kills the process. Must be fixed *before* it can be tested. |
| D-M23 | **The user's plaintext current password is stored as an `"OldPassword"` claim in a client-side cookie.** Data-protection-encrypted, but the live credential round-trips to the browser and sits in the cookie jar for 5 minutes. Prefer holding the state server-side, or requiring re-entry. |
| D-G25 | **The generated code only compiles in a `Microsoft.NET.Sdk.Web` project with implicit usings on.** It fully qualifies every *type* with `global::` — correctly immune to the consumer's imports — and then calls the *extension methods* `MapMethods`, `MapGroup` and `Produces`, which cannot be resolved from a `global::` type name; the declaring namespace has to be in scope. The emitted file contains no using directives at all. A consumer on plain `Microsoft.NET.Sdk` + `FrameworkReference Microsoft.AspNetCore.App`, or with `<ImplicitUsings>disable</ImplicitUsings>`, gets three `CS1061` errors pointing at generated source they cannot edit. Invisible until now because the sample TestApp is a Web SDK project. Fix: emit the usings, or call the extensions as static invocations. **Found by the "does the emitted code compile" test on its first run** — see R3.5 reason 2. |

### Correctness

| ID | Defect |
|---|---|
| D-G18 | Emission order is nondeterministic — `allGroupFqns` is a `HashSet<string>` iterated to build `rootGroups`; `valid`/`groups` are never sorted. Route order, factory field numbering and the descriptor list are all unstable. Blocks snapshot testing; bad for reproducible builds. |
| D-G5 | `EndpointBase.HandleAsync(HttpContext)` launders a null request through `HandleAsync(request!, …)`. Empty body, malformed JSON (`JsonException`) and wrong content type (`InvalidOperationException`) all become an unhandled **500** instead of 400/415. |
| D-G7 | MVC formatter fallthrough reads a drained body: a formatter whose `CanRead` is true but that returns `NoValue`/`Failure` leaves the stream consumed, then the JSON fallback silently yields `null` → D-G5. The `ModelStateDictionary` holding the validation errors is local and discarded. |
| D-G6 | `Dispose()` is unreachable for every typed endpoint — `EndpointBase<T>` implements both `IDisposable` and `IAsyncDisposable`, and both disposal sites test `IAsyncDisposable` first. |
| D-G10 | `symbol.Interfaces` is direct-only while the gate uses `AllInterfaces`, so an endpoint inheriting its interfaces via a user base class silently loses its verb, request type, `Produces` and generated base class. |
| D-G9 | Partial classes split across files produce duplicate, order-dependent `EndpointInfo`s; `GroupBy(FQN).First()` picks whichever came first, so the generator either misses the user's base class (`CS0263`) or wrongly suppresses emission. |
| D-G4 | An endpoint with two `IMemberOf<T>` is dropped entirely — no route, no descriptor, **and no partial base class** ⇒ unexplained `CS0115`. |
| D-G15 | A group with two `IMemberOf<T>` silently becomes a *root* group, dropping the parent prefix from its endpoints' routes. |
| D-G12 | `GetMethodName()` does not sanitise identifiers: an empty segment (`"My..Api"`, `".My"`) throws `IndexOutOfRangeException` and crashes the generator; hyphens/spaces pass through into uncompilable code; `[assembly: EndpointsMethodName("")]` emits a nameless method. |
| D-G13 | `GetSafeClassName()` can collide with a shipped type — `"MapEndpointRouteBuilder"` ⇒ `EndpointRouteBuilderExtensions` in the library's own namespace ⇒ `CS0101`. |
| D-G11 | Zero endpoints ⇒ no extension method emitted at all ⇒ `CS1061` on `app.MapMyAppEndpoints()`. Should emit a no-op. |
| D-S6 | `PageCount(0, perPage)` returns **1** (integer division truncates toward zero). Public on `ISitemapXml`. |
| D-S7 | `PageCount(total, 0)` throws `DivideByZeroException`; negative `perPage` returns nonsense. No argument validation. |
| D-S8 | `GetSitemapIndex` enumerates its source `2 + pageCount` times (`Any()` + `Count()` + per-page `Skip/Take`). N+2 round-trips on an `IQueryable`; wrong or throwing on a one-shot iterator. Also lazily evaluated, so it happens at the caller's `foreach`. |
| D-M7 | **The file logger never writes exceptions.** `Log` writes only `formatter(state, exception)`, and the framework's default formatter ignores the exception argument — so `LogError(ex, "boom")` writes `boom` and loses the stack trace entirely. |
| D-M14 | `FileLogger.Log` is not thread-safe: a fresh `FileStream(…, Append, Write)` with exclusive sharing per call, so concurrent loggers race to `IOException`. |
| D-M22 | `AddMustChangePassword<,>` does not register `IHttpContextAccessor`, which the service it registers depends on. |
| D-M26 | On the success path the change-password cookie is never signed out — the 5-minute ticket stays valid and replayable. |
| D-M36 | `ConfigureViewsInSubfolder` rewrites only `ViewLocationFormats`; Areas and Razor Pages silently keep looking in the root. |
| D-M39 | Same extension is an `IConfigureOptions` callback, so calling it **before** `AddControllersWithViews()` prefixes an empty list and does nothing, with no error. `PostConfigure` makes it order-independent. |
| D-M37 | …and it is not idempotent: two calls yield `/b/a/Views/…`. |
| D-M18 | `MapChangePassword` returns `IEndpointRouteBuilder`, not `RouteHandlerBuilder`, so consumers cannot call `.AllowAnonymous()` — and `/.well-known/change-password` is then blocked by exactly the global authorization fallback this package gets deployed behind. |

### Robustness and consistency

Lower severity, grouped for brevity: missing null/empty guards
(D-M3 `ExcludedHosts`, D-M6 `next`, D-M19/20 redirect URL and factory, D-M24/25 `Email`
and `HttpContext`, D-S20 null `ImageUrl` producing a bare-host image URL);
bare `throw new Exception()` in four places (D-S17, D-M27 ×3);
unreachable or misordered guards (D-S16, D-S24, D-M4);
exact-type `CanWriteType` rejecting subclasses (D-S11);
double registration without a guard (D-S10, D-M10);
unescaped interpolation into a processing instruction (D-S12) and into a
`Content-Disposition` filename (D-S19, which also uses the *library's* own assembly name
as the fallback);
mutating shared `XmlWriterSettings` per request (D-S13);
missing leading-slash validation (D-S14);
case-sensitive extension check (D-M11), `IsEnabled` always true including `LogLevel.None`
(D-M12), `BeginScope` returning null (D-M13), per-call revalidation on the hot path
(D-M15), category ignored and no caching (D-M17);
`O(n²)` `IndexOf` in the generator (D-G21), compiler-generated attributes leaking into
endpoint metadata (D-G8), dead `EndpointNameAttribute` (D-G3), reference-based
`EndpointDescriptor` equality (D-G16), group-relative descriptor `Path` (D-G20),
trailing-slash-only routes from `Path => "/"` (D-G19);
two divergent mechanisms packing the same generator DLL (D-G23, D-G24).

Docs/metadata: `MustChangePassword/README.md` describes **HSTS**;
`SubDirectoryViews/README.md` describes **SitemapXml**; three packages carry
`<PackageTags>ASP.NET Core, Razor views</PackageTags>` wrongly; every
`<PackageProjectUrl>` is a malformed 404.

### Corrections to this register, from the empirical pass (M2–M8)

The register above was written from **reading** the code. Writing the tests executed it, and
three entries did not survive. They are corrected here rather than quietly edited, because the
reasoning that produced them was wrong in a way worth remembering.

**D-S1 — WRONG as stated. Latent, not a shipped-output defect.**
Claimed: `Url`'s `loc`/`lastmod`/`changefreq` serialize as `<loc xmlns="">`, producing an invalid
sitemap. Measured: nested inside a `UrlSet` they land correctly in the sitemap namespace. An
`[XmlElement]` with no namespace **inherits the namespace of the mapping it is reached through**,
and `UrlSet.Urls` supplies it. The empty namespace appears only with
`XmlSerializer(typeof(Url))` as a document root — which `CanWriteType` makes unreachable. Both
facts are now asserted, the valid nested shape as a regression guard and the root case as a
`_KnownBug`, so a blind "fix" breaks a test.

**D-S15 — WRONG. Does not reproduce. Struck.**
Claimed: `OpenSearchDescription.SearchForm` has the same empty-namespace defect. Measured: it
serializes as `<SearchForm xmlns="http://a9.com/-/spec/opensearch/1.1/">`. Same resolution rule as
above — `OpenSearchDescription` carries `[XmlRoot(Namespace = …a9…)]`, so its unqualified members
inherit it. Adding an explicit `Namespace` would be a no-op. The tests assert the correct
namespace with remarks saying so.

**D-S21 — WRONG as stated. The predicted 406 does not happen.**
Claimed: a browser fetching the OSDX gets 406, because `RespectBrowserAcceptHeader = true` and the
only capable formatter is registered for one media type. Measured against a live `TestServer`:
**200 OK** with the correct content type and body. Two independent reasons, both verified — a
browser's `Accept` ends in `*/*;q=0.8`, which matches the OSDX media type, and
`MvcOptions.ReturnHttpNotAcceptable` **defaults to false**, so negotiation failure falls back to
the first capable formatter instead of answering 406. `RespectBrowserAcceptHeader` only makes MVC
*try* to honour the header; it is not the same switch.

What survives is narrower and still worth fixing: the handler's manual `Response.ContentType` is
dead code (negotiation sets it, and coincidentally picks the same value), and the endpoint works
only because `ReturnHttpNotAcceptable` sits at its default. Setting that option — the documented
way to make negotiation strict — gives a wildcard-free client a 406 with an empty body and no way
to opt out. That coupling is what is now pinned.

**D-G19 — WRONG. Not a defect. Struck.**
Claimed: a group prefix plus `Path => "/"` composes to `/api/users/`, which does not match
`/api/users`. Measured: **both match, 200 OK.** The composed pattern really is `/api/users/`, but
ASP.NET Core routing treats the trailing empty segment as equivalent. Nothing to fix, and a test
now exists to stop someone "normalising" the pattern on the strength of reading it.

**D-G5 — right defect, wrong trigger.**
Claimed: an empty request body binds to null, which the bridge launders through `request!`.
Measured: `ReadFromJsonAsync` on a zero-length body **throws `JsonException`** — it does not return
`default`. The null path is real and reachable, but via a **literal JSON `null` body**, which
clients genuinely send. So D-G5 stands; the empty-body case is a separate 500-instead-of-400
problem, not a route into it. Whitespace-only bodies behave like empty ones.

**D-G7 — right defect, wrong symptom.**
Claimed: a formatter that consumes the body then returns `NoValue` leaves the JSON fallback reading
a drained stream and silently yielding null. Measured: the fallback **throws `JsonException`**. That
is arguably worse than a silent null, because the error blames the client — a JSON parse failure at
position 0 for a request whose body was valid JSON — for what is a double-read inside the library.

**D-M19 — right defect, wrong (and milder) symptom.**
Claimed: a null change-password URL throws `ArgumentNullException` from `Response.Redirect`.
Measured: it does **not** throw. It sets 302 and never writes the `Location` header. An exception
would at least surface as a 500 in the logs; this produces a syntactically valid response that no
browser or password manager can follow, with nothing recorded anywhere. An empty string differs
again — the header is written, empty. Both shapes are now pinned.

**D-G13 — right defect, wrong symptom.** Claimed: `CS0101` from a duplicate type name. Measured:
the shipped type lives in *metadata*, not the same compilation, so source silently wins. The
generated `EndpointRouteBuilderExtensions` **shadows** the real one with zero errors and zero
warnings, and calls to the shipped `MapEndpoint<T>()` still resolve. Silent shadowing is harder to
diagnose than a compile error, not easier.

**D-G12 — worse than recorded, and untestable as specified.** The register said to assert the
`IndexOutOfRangeException` via `runResult.Results[0].Exception`. It never gets there: the throw is
swallowed inside `MintPlayer.SourceGenerators.Tools`' `ProduceCode`, so there is **no generated
file, no diagnostic, no exception on the result, and not even `CS8785`**. A malformed assembly name
produces total silence, and the user meets it as `CS1061` at their own call site. The test asserts
the silence.

**D-G18 — unrefuted but not reproducible from a test.** Written as a determinism assertion, which
passes: `HashSet<string>` enumerates identically for identical insertions *within one process*, and
string hash randomization is per-process. So single-host tests structurally cannot catch the
cross-process instability. The defect stands on code inspection; the route assertions sort before
comparing so they are order-insensitive by construction.

**D-G17 — possibly unreachable, deliberately not confirmed.** Reading the producer suggests every
member of a cycle has a non-null parent, so none qualifies as a root group, `rootGroups` comes out
empty, and the cyclic groups and their endpoints are silently **dropped** rather than overflowing
the stack. Confirming that means running the recursion, and an uncatchable `StackOverflowException`
would take the whole test host down — so the test stays skipped and the question stays open. If it
is right, the defect changes from "crashes the compiler" to "silently discards endpoints", which is
quieter and arguably worse.

### A second round of corrections, from fixing (M9)

Executing the code disproved seven predictions. *Fixing* it disproved four more, including one
of my own prescriptions.

**D-G22 — NOT a defect, and the proposed fix was impossible.** Claimed: verb interfaces implement
`Methods` as explicit non-virtual statics, so a class cannot override it while implementing
`IGetEndpoint`, and implementing two verb interfaces is an error rather than the union. Measured in
every arity: a class **can** declare `public static IEnumerable<string> Methods` and it **wins** — a
class member beats an interface's default implementation. Two verb interfaces give `CS8705` ("no
most specific implementation"), which the class resolves by declaring `Methods`, yielding exactly
the union. And the fix I suggested cannot be written: an implicit `static virtual Methods` in a
derived interface does not implement the base member, it **hides** it (`CS0108` plus `CS0535` on
every implementer). The interface shape is unchanged; all three behaviours are now pinned and
documented in the README.

**D-G8 — misattributed.** The compiler-generated attributes originally observed in
`endpoint.Metadata` did not come from the attribute transfer at all: ASP.NET Core also contributes
the handler *delegate's* attributes, and that delegate is a compiler-generated lambda inside the
library. They are still present after the fix, correctly. Whether an endpoint *class* even carries
a type-level `NullableContextAttribute` depends on where Roslyn puts the uniform nullable context —
in the test assembly it lands on the **module**, so no fixture type carried one, meaning the
original test passed for the wrong reason. It now uses purpose-built fixture attributes.

**D-S21 — my prescribed fix does not work.** The register said to set `ObjectResult.ContentTypes`
"so the formatter is forced regardless of `Accept`". It is not forced:
`DefaultOutputFormatterSelector` **intersects** the declared content types with the sorted `Accept`
header, and an empty intersection under `ReturnHttpNotAcceptable = true` is precisely the path that
yields no formatter and therefore the 406. Declaring the content type makes the strict case no
better. Fixed instead by writing the description through the OSDX formatter directly (preset
`ContentType`, `ContentTypeIsServerDefined = true`), bypassing negotiation for byte-identical
output.

**The XSLT premise — wrong.** The register implied `XslCompiledTransform` cannot run the shipped
`version="2.0"` stylesheet. Measured: it loads and transforms it, producing byte-identical output,
because a version above the processor triggers *forwards-compatible processing* rather than a
failure. The downgrade to `1.0` is still correct, but for a different reason: in that mode an
unrecognised XSLT element is silently ignored instead of reported, so a future authoring mistake
would produce a quietly wrong page. The test now loads and transforms rather than reading the
attribute.

**D-G17 — unreachable, as suspected, and quieter than feared.** Confirmed while fixing: every
member of a cycle has a parent, so `rootGroups` came out empty and the cyclic groups *and their
endpoints* were silently **dropped** rather than overflowing the stack. Now reported as a new
MPEP005, with a visited set in `EmitGroupTree` regardless. The skipped test runs.

**D-G27 — root cause found, and it was not in this repo's code.**
`GeneratorExtensions.ProduceCode` in `MintPlayer.SourceGenerators.Tools` registers the source output
on `CompilationProvider.Combine(producer)`. A `Compilation` has no value equality — `Clone()`
*guarantees* inequality — and the `Producer` was a fresh object too, so the output could never cache
however well `Models.cs` compared. The `IEquatable` implementations were correct all along and
simply could not matter. Fixed by registering on a value-equal model with `WithTrackingName`
throughout.

> **2026-09-25 (PR #35, PRD-EndpointsOpenGenerics addendum D10):** the "IEquatable ceremony" in `Models.cs`
> is gone. The models are `[GenerateEquality]` partial classes and MintPlayer.ValueComparerGenerator 12.0.1
> generates their equality; the incremental tests passed unchanged across the migration.

Smaller ones: **D-M27** is three bare-`throw` sites covering three modes (the other two surfaced as
`UnauthorizedAccessException`), not "three sites, five modes" — five modes, five types now, so the
outcome matched but the mapping did not. **D-M33**'s "no `SameSite`" is accurate but the *effective*
default was already `Lax`, so the change is `Lax` → `Strict`, not "none → something". **D-M22**'s
blast radius is narrower than stated: it only bit `AddIdentityCore`-without-MVC hosts, since
ASP.NET Core registers the accessor itself. **D-M3** is unreachable (`HstsOptions.ExcludedHosts` is
get-only and framework-populated). **The `<PackageTags>` claim was wrong** for the middleware folder
— only one of those four projects had the copy-pasted value, and the real defect there was the
comma separator (NuGet expects `;`).

**Naming collisions are systemic, not incidental.** D-G26 turned out to be one instance of a habit:
`EndpointNameAttribute` vs `Microsoft.AspNetCore.Routing.EndpointNameAttribute`, `LoggerExtensions`
vs `Microsoft.Extensions.Logging.LoggerExtensions`, and — introduced by D-M21's own rename —
`ChangePassword.EndpointRouteBuilderExtensions` vs
`Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions`. All three shadow a type that is an
implicit global using in a Web SDK project, making the library's type unnamable from consumer code
without full qualification. Extension-method call sites are unaffected, which is why it went
unnoticed. Worth a naming rule rather than three separate fixes.

**The common thread.** Seven predictions were wrong: three outright (D-S1 latent, D-S15 and D-G19
not defects) and four in their mechanism or symptom (D-G5, D-G7, D-M19, D-S21). Every one was a
claim about framework behaviour — `XmlSerializer` namespace resolution, MVC content negotiation,
routing's trailing-segment handling, `ReadFromJsonAsync`'s empty-input contract,
`Response.Redirect`'s null handling — inferred from reading library code without executing the
framework underneath it.

Three lessons kept:

1. *A defect found by reading is a hypothesis until it is executed.* The ratio here was roughly
   one in four wrong, and the wrong ones were disproportionately the dramatic-sounding ones.
2. *Assert the measured behaviour, with the reasoning in a remark.* Several of these tests now
   exist specifically to stop a future reader "fixing" correct code — which is the failure mode a
   plain deletion would have left open.
3. *Being wrong about the symptom does not make the defect unreal.* Four of the seven were real
   problems with a different mechanism, and in two cases (D-G7, D-M19) the measured behaviour was
   **worse** than predicted. Downgrading a finding on the first contradicted detail would have
   dropped them.

### Additions to this register, from the empirical pass

Found while writing the tests. Numbered continuing the existing scheme.

| ID | Defect |
|---|---|
| D-S28 | **Every `Video` emits two `xsi:nil="true"` elements.** `FamilyFriendly` and `Live` lack the `ShouldSerialize*` methods every other optional member has (this is D-S5's consequence, and it is worse than "inconsistent"): the output is invalid against the video sitemap schema, *and* it drags the `xsi` namespace declaration back into responses that the formatter's `ns.Add("", "")` exists to keep clean. |
| D-S29 | **A null `Url.Loc` emits a `<url>` with no `<loc>`**, and a null `Image.Location` emits an empty `<image:image>`. `loc` is required by both protocols, and the invalid document is produced with no exception. |
| D-S30 | `ChangeFreq` is missing `weekly` in **both directions** — beyond D-S3's "cannot be written", deserializing a real-world sitemap containing `weekly` **throws**. |
| D-S31 | The stylesheet guard is `IsNullOrEmpty`, so a whitespace `StylesheetUrl` still emits `href="   "` — D-S9's blind spot, in the formatter. |
| D-S32 | `HttpContextExtensions.ExecuteResultAsync`'s `?? new RouteData()` is dead code: `GetRouteData()` returns an empty `RouteData` rather than null even with no routing feature. Uncoverable; delete it. |
| D-S33 | The `"Website"` fallback in the OSDX `ShortName` chain is unreachable — a loaded assembly always has a `FullName`, so the chain can only yield the library's own name (D-S19). |
| D-S34 | The OSDX `<Image>` dimensions are hard-coded 16×16 with no option, so a configured `/logo-512.png` is still advertised as 16×16. |
| D-M40 | `AddMustChangePasswordUserIdCookie` is not idempotent: two calls (or an app that registers a scheme of the same name) fail with a duplicate-scheme error surfacing when `IAuthenticationSchemeProvider` is first resolved, not at the call site. |
| D-M41 | **The credential-bearing cookie is a *session* cookie.** `ExpireTimeSpan = 5 min` bounds only the server-side ticket; no `expires`/`max-age` reaches the browser, so the cookie holding the plaintext password persists in the jar for the whole browser session, long after the ticket is dead. Distinct from D-M33. |
| D-M42 | **D-M23 × D-M26 compound:** because the success path never signs out, the plaintext *former* password stays decryptable on the client after rotation. Neither defect alone implies this. |
| D-M43 | `ChangePasswordSignInAsync` never verifies the supplied `oldPassword`. Verification is deferred a full request, so a garbage value still gets a ticket and the failure lands one request later as a bare `Exception`. |
| D-M44 | `MustChangePasswordInfo.Email` is dead — read out of the ticket and never used, so the address is carried in the cookie for nothing. |
| D-M45 | Identity's `IdentityError`s are discarded: a weak new password reports "unauthorized" instead of "needs a digit". |
| D-M46 | `FileLoggerOptions.FileName` is declared non-nullable under `Nullable=enable` with no initialiser and is null by default — the same lie as D-S27, in a package D-S27's scope does not cover. |
| D-M47 | The `Log.txt` fallback is a **relative** path, so an unconfigured host writes into whatever `Directory.GetCurrentDirectory()` happens to be. With D-M46, forgetting to configure produces logs in an unpredictable location instead of an error. |
| D-M48 | `LoggerFileProvider.Dispose()` is empty, giving no flush or completion guarantee. Harmless only while `FileLogger` opens and closes the stream per call — so it is a **constraint on the D-M14 fix**, not just an observation: serialising writes by holding a stream open makes this a real leak. |
| D-G25 | See the serious table above. |
| D-G27 | **The generator's incremental pipeline never caches.** Over `Compilation.Clone()` — byte-identical content — `SourceOutput` comes back `Modified`, and so does a pure handler-body edit. The `IEquatable` implementations in `Models.cs` are correct (asserted directly) and buy nothing: something upstream of `ProduceCode` compares unequal on every compilation. Since only `Compilation` and `SourceOutput` are tracked (no `WithTrackingName` anywhere), a test cannot localise the cause further. This defeats the entire purpose of the equality ceremony in `Models.cs`, and means every keystroke in a consuming project re-runs the whole generator. |
| D-G26 | **The library's `EndpointNameAttribute` collides by simple name with `Microsoft.AspNetCore.Routing.EndpointNameAttribute`.** That namespace is an implicit global using in a Web SDK project, so once a consumer adds `using MintPlayer.AspNetCore.Endpoints;`, writing `[EndpointName("x")]` is a `CS0104` ambiguity error. The attribute is unusable by its short name — and since D-G3 means the generator never reads it anyway, the two together say the type earns nothing. |

Corrections to severity of existing entries, also from measurement:

- **D-S6 is inconsistent, not merely wrong:** `PageCount(0, 1)` returns **0** while `PageCount(0, n>1)` returns **1**, because `-1/1` is `-1` but `-1/n` truncates to `0`.
- **D-S12's `?>` case is defused** by `XmlWriter`, which rewrites it to `? >`; the document stays well-formed with a corrupted href. The bare-quote injection is the severe half.
- **D-S27 (`throw new Exception()`) is three sites but five distinct outcomes**, collapsed onto two exception types. The first site conflates "no ticket at all" with "ticket present but malformed".
- **`MapDefaultSitemapXmlStylesheet` does not require `AddSitemapXml()`** — `AddRouting()` alone supplies the options infrastructure, so it maps and serves. The intuitive expectation of a startup failure is wrong, and is now pinned.

### Requires a decision — breaking API changes **[decision]**

Not implemented. Each changes a published package's public surface.

| ID | Change | Why it's breaking |
|---|---|---|
| D-S3 | Add `weekly`, `always`, `never` to `ChangeFreq` | `weekly` is the most-used spec value and is missing. Appending is source-compatible; inserting in spec order changes the underlying ints for anyone who persisted them. |
| D-S4 | `LastMod` → `DateTime?`, or add `ShouldSerializeLastMod` | Unset `LastMod` currently serializes as `0001-01-01`, telling crawlers the page changed in year 1. `lastmod` is optional in the spec. Same for `ChangeFreq`, whose `default` is `Hourly` — so every URL claims hourly churn. The `ShouldSerialize*` route is non-breaking; the nullable route is cleaner. |
| D-S27 | Annotate `string` → `string?` across `OpenSearchOptions` and the `Data.*` DTOs | They are declared non-nullable under `Nullable=enable` with no initialiser (206 CS8618 warnings on build), and are in fact null by default — the code defends with `?.` and `NullIfEmpty()` anyway. The signatures currently lie to consumers. |
| D-S9 | Make `NullIfEmpty` `internal` in both packages | Two packages export the same-named `public` extension on `string?`; an app installing both with `ImplicitUsings` gets ambiguous-call errors. Also it does not handle whitespace, so `"   "` survives to become a route pattern. |
| D-G22 | Verb interfaces' `Methods` are explicit non-virtual statics | A class cannot override `Methods` while implementing `IGetEndpoint`, and implementing two verb interfaces is an ambiguity error rather than the union. Fixable only by changing the interface shape. |
| D-M31 | `MustChangePasswordConstants` → `public static class`, field → `const` | It is a plain `public class` today. |
| D-M33 | `AddMustChangePasswordUserIdCookie` hard-codes a 5-minute expiry with no `Action<CookieAuthenticationOptions>` overload, and sets no `SameSite`/`SecurePolicy` for a credential-bearing cookie | Adding an overload is non-breaking; changing the defaults is not. |
| — | Move `MustChangePasswordConstants` into `.Abstractions` | `MintPlayer.AspNetCore.MustChangePassword.Abstractions` has **zero instrumentable IL** (one interface, two abstract members) and can therefore never appear in a coverage report. This would give it a `.cctor` with real sequence points *and* is a genuine design improvement — consumers need the scheme name to write `[Authorize(AuthenticationSchemes = …)]`. Otherwise the package is documented as an exemption. |

## Acceptance criteria

1. `dotnet restore` → `dotnet build -c Release --no-restore` → the R1.1 test command
   succeeds locally and on `ubuntu-latest`, with zero test failures on both.
2. `coverage/**/coverage.cobertura.xml` is produced, one per test project.
3. Every report path is repo-root-relative and resolves against `git ls-files` — **zero
   unmatched files**, verified by simulating the server's suffix match.
4. All 14 shippable assemblies appear in the report. **No exemption is needed** — see the
   correction in Appendix C: `MintPlayer.AspNetCore.MustChangePassword.Abstractions` was
   predicted to be absent for having zero instrumentable IL, and it is in fact present.
5. The generator assembly appears in the report with non-zero covered lines (proving
   R3.7's DLL+PDB placement actually worked — its failure mode is silent).
6. The upload step succeeds on a PR and the service publishes its check runs.
7. Every non-`[decision]` defect above is either fixed with a test pinning the fix, or
   has a test pinning current behaviour plus a note saying why it was not fixed.
8. No test hard-codes a UTC offset, a drive letter, `Environment.NewLine`, or a
   culture-sensitive format (R4, verified by grep).

## Results

**988 tests, 0 failures, 0 skipped. 98.9% line coverage (1393/1408).** Zero build warnings, down
from 206.

| Assembly | Lines | Rate |
|---|---:|---:|
| MintPlayer.AspNetCore.ChangePassword | 23/23 | 100.0% |
| MintPlayer.AspNetCore.Endpoints | 126/129 | 97.7% |
| MintPlayer.AspNetCore.Endpoints.Abstractions | 40/40 | 100.0% |
| MintPlayer.AspNetCore.Endpoints.Generator | 596/608 | 98.0% |
| MintPlayer.AspNetCore.Hsts | 35/35 | 100.0% |
| MintPlayer.AspNetCore.LoggerProviders | 105/105 | 100.0% |
| MintPlayer.AspNetCore.MustChangePassword | 82/82 | 100.0% |
| MintPlayer.AspNetCore.MustChangePassword.Abstractions | 30/30 | 100.0% |
| MintPlayer.AspNetCore.NoSniff | 14/14 | 100.0% |
| MintPlayer.AspNetCore.OpenSearch | 165/165 | 100.0% |
| MintPlayer.AspNetCore.OpenSearch.Abstractions | 1/1 | 100.0% |
| MintPlayer.AspNetCore.SitemapXml | 82/82 | 100.0% |
| MintPlayer.AspNetCore.SitemapXml.Abstractions | 65/65 | 100.0% |
| MintPlayer.AspNetCore.SubDirectoryViews | 29/29 | 100.0% |
| MintPlayer.Timestamps | 0/0 | n/a — four interfaces, no executable IL |
| **Total** | **1393/1408** | **98.9%** |

Path resolution against `git ls-files`: **81 matched, 0 ambiguous, 0 unmatched.**

### How this number was measured, and a correction to an earlier one

The figure is **normalized then max-merged**, which is what the coverage server does: each report
path is first resolved to its repo-relative form by suffix-matching against `git ls-files`, and only
then are the two reports merged with "covered by any session = covered".

An earlier pass of this document reported **88.7%**, computed by max-merging on the *raw* report
paths. That was wrong, and understated the result: the two reports express the same files under
different prefixes (`Endpoints/…` from the repo root vs `MintPlayer.AspNetCore.Endpoints/…` from the
`Endpoints/` prefix), so keying on the raw filename treats one file as two and defeats the merge
entirely for any library instrumented by both test projects. `MintPlayer.AspNetCore.Endpoints`
appeared as 50.8% and is actually 97.7%.

The lesson generalises past this document: **a coverage number is only meaningful together with the
merge rule that produced it**, and the merge rule has to be the server's.

### What is deliberately not covered

- **`MintPlayer.Timestamps`** — four interface declarations, no executable IL. Nothing to cover;
  its real contract (`T : IUpdateTimestamp` as consumed by `GetSitemapIndex`) is exercised by the
  SitemapXml service tests, plus two reflection shape-guards.
- **`MintPlayer.AspNetCore.Endpoints`, 3 lines** and **`.Generator`, 12 lines** — defensive branches
  unreachable without reflection, and generator paths that require a malformed compilation the
  driver cannot construct. Each is commented where it sits.

Note the earlier prediction that `MustChangePassword.Abstractions` could *never* appear in a report
for lack of instrumentable IL was wrong twice over: it appeared from the first run, and it is now at
100%.

## Risks

| Risk | Mitigation |
|---|---|
| A test project omitted from the `.sln` reads as a pass forever (P1) | R5.1 positive assertion in CI |
| Generator coverage silently zero (R3.7) | Acceptance criterion 5 asserts non-zero covered lines, and the copy target carries a hard `<Error>` if the asset list is empty |
| Report paths ambiguous ⇒ files silently dropped (P3.1) | R2.1 single project; acceptance criterion 3 simulates the server's matcher |
| A test passing for the wrong reason against `DefaultHttpContext` (P3.2) | R3.1 recording feature; each such test asserts the callback *count* as well as the effect |
| Windows-green / ubuntu-red (R4) | R4.1–R4.6 rules, plus the pre-completion grep — and one CI round before declaring done, which is what actually caught R4.6 |
| `COVERAGE_TOKEN` / GitHub App missing ⇒ upload warns and skips | Documented as an out-of-band prerequisite; `fail-ci-if-error: false` keeps it non-blocking either way |
| Fixing 27 defects alongside a new suite is a large diff | Deliberate, per the repo's one-PR convention. Defects are fixed in a dedicated milestone *after* the tests that pin current behaviour exist, so each fix shows as a test change. |

## Appendix A — measurement method

Every factual claim about the current state was verified by running it, not inferred:

- SDK `10.0.400` locally (CI pins `10.0.100`; no `global.json`, so they differ).
- `dotnet restore` (11s) → `dotnet build -c Release --no-restore` (39.14s, exit 0, 206
  warnings, 0 errors). All 206 are CS1591/CS8618/CS8604/CS8601/CS1573 doc-and-nullability
  noise; CS1591 exists only in Release because `GenerateDocumentationFile` is
  Release-conditioned. No `TreatWarningsAsErrors` anywhere.
- `dotnet pack --no-build -c Release /p:ContinuousIntegrationBuild=true` → exit 0, 29
  packages, generator correctly landing in `analyzers/dotnet/cs`, `.snupkg` suppressed for
  the generator (the `22c9cef` NU5017 fix holding).
- `dotnet test --no-restore --verbosity normal` → exit 0, 11 lines, **no compilation**;
  `find . -type d -name Debug` in `bin`/`obj` → **0 results** after a full CI sequence.
- SourceLink: 16 `obj/**/*.sourcelink.json` files present; content is absolute local
  paths. Rebuilding one project with `-p:ContinuousIntegrationBuild=true` changed its
  document map to `/_/*`, confirming R1.3's pinned `DeterministicReport`.
- Generated files: 16 `*.g.cs` under `obj/`, all `*.GlobalUsings.g.cs`; the
  `MintPlayer.SourceGenerators/…/ClassNames.g.cs` directory cited in build warnings does
  not exist on disk. Confirmed the 10.13.0 package's `build/*.props`/`*.targets` do not
  set `EmitCompilerGeneratedFiles`.
- Duplicated basenames and generator-basename uniqueness: `git ls-files '*.cs' | xargs -n1
  basename | sort | uniq -d`.
- All tracked `.csproj` files are in the `.sln` — no orphan (the sibling repo discovered
  `Solve.csproj` had never been built, tested *or* packed).
- Package versions confirmed against `api.nuget.org` flatcontainer indexes.

One local-only gotcha recorded: `dotnet pack … /p:Foo=true` **fails under Git Bash** —
MSYS rewrites `/p:…` into a `p:\` path, giving `MSB1009`. Use PowerShell locally.
Irrelevant to `ubuntu-latest`.

## Appendix B — deviations from `MintPlayer.Dotnet.Tools`

That repo is the reference. Every difference is deliberate.

| Area | Sibling | Here | Why |
|---|---|---|---|
| Starting point | 22 test projects, 1,723 tests | zero tests | The suite is the bulk of this work, not the CI change. |
| `--no-build` rationale | measured 88s → 32s regression fix | preventive | With no test projects, the current step compiles nothing. The Debug rebuild is what we'd acquire, not what we remove. |
| `ExcludeByFile=**/*.g.cs` | removes 6 files / 738 lines | **to be measured** | Generated code here is in-memory only; the sibling's measurement does not transfer. See R1.4. |
| Test-project granularity | one per library (22) | **one for all runtime libraries** | Five duplicated basenames make per-library reports lose files silently. The sibling hit the same rule and expressed it as "every test project must reference at least two top-level folders". |
| `Exclude` list | none | one entry, the TestApp | The e2e tests must reference the sample app for `WebApplicationFactory<Program>`, which makes it instrumentable. Non-shipped code must not enter the denominator. |
| Integration host | n/a | `TestHost` inline, not `WebApplicationFactory` | No entry-point assembly, no content-root probing (a real Linux hazard). One exception: the TestApp e2e tests. |
| Snapshot testing | `Verify.Xunit` adopted | deferred | The generator's output order is nondeterministic (D-G18). Snapshots come after that fix, if at all. |
| Auth | `COVERAGE_TOKEN` | same | OIDC is available (public repo) and was considered; one auth story across both repos won. |
| `xunit.runner.visualstudio` | 4.0.0 | 4.0.0 | Sibling's verified-together set adopted wholesale. |

## Appendix C — M1 gate results, and two corrections

The M1 gate ran the real CI command and simulated the coverage server's suffix matcher
against `git ls-files`. Results with 29 smoke/harness tests in place:

| Report | Assemblies | Files | `<sources>` |
|---|---:|---:|---|
| `MintPlayer.AspNetCore.Tools.Tests` | 14 | 47 | `C:/Repos/MintPlayer.AspNetCore.Tools/` |
| `MintPlayer.AspNetCore.Endpoints.Generator.Tests` | 3 | 18 | `…/MintPlayer.AspNetCore.Tools/Endpoints/` |

**Path matching: 65 matched, 0 ambiguous, 0 unmatched.** R2.1 confirmed: because the
first report's documents span nine top-level folders, `<sources>` is the repository root
and all five duplicated basenames resolve uniquely. The generator report's `<sources>` is
`Endpoints/`, and its second-level paths are unique, confirming R2.2.

`MintPlayer.AspNetCore.Endpoints.Generator` appears with **248 of 445 lines covered from
5 harness tests**, confirming R3.7 — a plain `ProjectReference` does put the generator DLL
*and* its PDB in the output root, and in-process driver tests do attribute to it. This was
the requirement whose failure mode is silent, so it is worth restating that it is now
positively verified rather than assumed.

`MintPlayer.AspNetCore.Endpoints.TestApp` is absent, confirming R2.3's `Exclude`.

### `ExcludeByFile` measurement (settles R1.4)

| Setting | Files | Coverable lines | Covered lines |
|---|---:|---:|---:|
| none | 73 | 903 | 260 |
| `**/obj/**` | 65 | 884 | 257 |

So the glob removes 8 files / 19 coverable lines / 3 covered lines. **A coverlet file glob
does match a virtual path with no file on disk** — the open question in R1.4, now answered
empirically rather than assumed.

The 8 files are the `MintPlayer.SourceGenerators` output (`Inject.g.cs`,
`ClassNameList.g.cs`, `ServiceMethods.g.cs`) in LoggerProviders, MustChangePassword,
NoSniff and SitemapXml. Verified absent from disk and untracked by git, so the server drops
them regardless; excluding them makes the local report and the server agree, which is the
whole point. The 3 covered lines are generated `[Inject]` constructors that really do run —
a small honest loss, taken deliberately in exchange for local/server agreement.

Note `**/*.g.cs` was **not** used: it would also match the 16 SDK `*.GlobalUsings.g.cs`
files, and `**/obj/**` expresses the actual rule (nothing under `obj/` is git-tracked
source) rather than a filename coincidence.

### Correction 1 — `MustChangePassword.Abstractions` is *not* absent

Predicted during investigation to be missing from every report for having zero
instrumentable IL (one interface, two abstract members), with a documented exemption and a
suggestion to move `MustChangePasswordConstants` into it to give it a `.cctor`.

**It appears in the report.** No exemption is needed and no code needs to move for coverage
reasons. Moving the constant may still be a good idea on design grounds — consumers need
the scheme name to write `[Authorize(AuthenticationSchemes = …)]` — but that is now purely a
design argument, listed under the `[decision]` items on its own merits.

### Correction 2 — the Debug-rebuild claim, scoped correctly

The sibling repo measured `--no-build` as an 88s → 32s fix. Restated for this repo in R1.1:
the current Test step compiles **nothing** here, because there are no test projects, so
`--no-build` is preventive rather than a saving. The Debug rebuild is what this repo would
acquire on adding the first test project, not a cost being removed.

## Version

Created 2026-08-27. Branch `feature/test-coverage`.
