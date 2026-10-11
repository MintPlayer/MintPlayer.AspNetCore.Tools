# PRD: Rebuild against MintPlayer.SourceGenerators 12.2.0

Issue: [#42: Endpoints: rebuild against MintPlayer.SourceGenerators.Tools 12.2.0 (MissingMethodException OpenPathSpec breaks consumers)](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/issues/42). Fixed here.

*(Draft 2, 2026-10-11.*
- *Draft 1 came from a four-agent investigation. It proposed an upstream 12.2.1, ILRepack and a CI
  consumer guard.*
- *The owner cut the scope to the dependency update.)*

**Affected version:** MintPlayer.AspNetCore.Endpoints 11.4.0-rc.0.
**Version:** **11.4.1-rc.0**, for the meta package, Abstractions and Generator. The repo has never used
`-rc.1`, and 11.0.1-rc.0 set the precedent of a patch bump for a dependency-only fix.

## Problem

The Endpoints package bundles its generator together with a copy of
`MintPlayer.SourceGenerators.Tools.dll` (12.1.0) in `analyzers/dotnet/roslyn5.9/cs/`. The compiler loads
only one copy of that assembly per compilation, and the highest version wins.

Tools 12.2.0 (MintPlayer.Dotnet.Tools#195) gave `OpenPathSpec(IndentedTextWriter, PathSpec?)` new optional
parameters. That change is source compatible but binary breaking. So a consumer on
MintPlayer.SourceGenerators 12.2.0 runs the Endpoints generator against Tools 12.2.0 and gets MPSG001
`MissingMethodException`.

## Investigation findings

- **F1: `OpenPathSpec` is the only break that reaches this repo.**
  - Each member that the built Generator references in Tools and ValueComparerGenerator.Attributes was
    checked against 12.2.0. All of them resolve except `OpenPathSpec`, whose only call site is
    `EndpointGenerator.Producer.cs:246`.
  - Rebuilding against 12.2.0 is source compatible.
- **F2: The `[Inject]` order change affects nothing here.**
  - The only `[Inject]` class (`MustChangePasswordService`) has no base class and is resolved through DI.
  - Endpoints are created with `ActivatorUtilities`.
  - The generated constructors are identical under 12.1.0 and 12.2.0.
- **F3: MustChangePassword and SitemapXml do not need republishing.** They consume the generators with
  `PrivateAssets=all` and ship no analyzer, so their references are bumped for consistency only.
- **F4: Endpoints now requires MintPlayer.SourceGenerators 12.2.0 or newer.**
  - Once Endpoints bundles Tools 12.2.0, a consumer that stays on MintPlayer.SourceGenerators 12.1.x hits
    the same failure in the other direction: SourceGenerators' own generators report MPSG001.
  - The README states the 12.2.0 minimum.

## Requirements

- **R1:** Move every outdated package reference in the solution to its latest stable version:
  - `MintPlayer.SourceGenerators`, `.Attributes` and `.Tools` → 12.2.0;
  - `MintPlayer.ValueComparerGenerator` and `.Attributes` → 12.2.0. The issue's list missed these;
    they are at `Endpoints.Generator.csproj:37-38`;
  - `xunit.runner.visualstudio` → 4.0.1;
  - `coverlet.collector` → 10.1.0.

  Also update the version comment in `Endpoints.Generator.CodeFixes.csproj:35`.
- **R2:** Bump the Endpoints `<Version>` to 11.4.1-rc.0. MustChangePassword and SitemapXml keep theirs.
- **R3:** Add a README note under "Requirements": MintPlayer.SourceGenerators 12.2.0 or newer, the reason
  (one loaded copy, highest version wins), and the `[Inject]` order heads-up for consumers.

## Acceptance criteria

- **AC1:** A consumer that references the new Endpoints together with MintPlayer.SourceGenerators 12.2.0
  builds, generates `EndpointMapping.g.cs`, and reports no MPSG001.
- **AC2:** The solution rebuilds (`-t:Rebuild`, Release) with 0 warnings and 0 errors, and the full test
  suite passes.

> **As built (2026-10-11):** R1 to R3 are implemented.
> - AC1 was verified: the packages were packed to a scratch feed as `11.4.1-verify.1`, and a Web-SDK
>   consumer with a partial typed `IPostEndpoint<TReq, TRes>` was built against
>   MintPlayer.SourceGenerators 12.2.0 with `UseSharedCompilation=false`. The build exited 0 and
>   `EndpointMapping.g.cs` was generated.
> - AC2 was verified:
>   - the Release `-t:Rebuild` gave 0 warnings and 0 errors;
>   - the tests passed on net10.0 and net11.0, with 430 in Endpoints.Generator.Tests and 1136 in
>     Tools.Tests.

## Out of scope

- An upstream 12.2.1 forwarding overload in Dotnet.Tools.
- ILRepack internalization of Tools.
- A CI consumer-smoke guard.

## Consumer follow-up (MintPlayer.Spark)

- Move every `MintPlayer.SourceGenerators*` package to 12.2.0, and Endpoints to 11.4.1-rc.0, together.
- Fix the `: base(...)` argument order in `SparkSubscriptionWorker<T>`:
  `(ILoggerFactory, IDocumentStore)` becomes `(IDocumentStore, ILoggerFactory)`.
