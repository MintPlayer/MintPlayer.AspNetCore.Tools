# Implementation plan: open-generic endpoint classes (issue #34)

Companion to `PRD-EndpointsOpenGenerics.md`. Branch `fix/endpoints-open-generics`. One PR, not merged
by Claude (merging publishes to nuget.org).

## Phase 0 — Investigation: done

Three agents (I1 audit and red tests, I2 manual path, I3 library shape). Findings are in the PRD as an
appended blockquote. The red tests are `Tests\MintPlayer.AspNetCore.Endpoints.Generator.Tests\OpenGenericEndpointTests.cs`
(11 red, 1 green regression guard, net10.0).

## Phase 1 — Decide: done

The grill session produced PRD decisions D1–D9: application-side closing through
`[assembly: EndpointTypeArgument<TConstraint, TArgument>]`, group-level `IsEnabled`, and a closed
endpoint treated as an ordinary endpoint. Prototype: `C:\Repos\WebApplication9`.

## Phase 2 — Red: done

- Update the 12 investigation tests to D6: an open endpoint is not mapped in its own assembly, compiles,
  keeps a type-parameterised partial, and gets the Info diagnostic.
- New generator tests: closing from the same compilation, and from a metadata reference to a marked
  library. Constraint-keyed and explicit forms. Every D2 error. Unbound parameters. Nested open
  containers. Closed generic groups (D7). `new static Path` (D7). The unused-attribute warning. The
  incremental cache.
- New runtime tests: a test library project with generic endpoints, closed by the TestApp. `IsEnabled`
  in both states. Manual-path naming for two closings.
All must fail on the unfixed code for the right reason.

## Phase 3 — Green: done

1. Abstractions: `EndpointTypeArgumentAttribute<TConstraint, TArgument>`, the explicit
   `EndpointTypeArgumentAttribute(Type, params Type[])`, the assembly marker, and
   `IEndpointGroup.IsEnabled`.
2. Generator, declaring side: an open-ness flag on `EndpointInfo` and `GroupInfo`; the
   `EndpointMappingPlan` filter next to MPEP024; the partial header with type parameters; the Info
   diagnostic; the assembly marker.
3. Generator, closing side: read the attributes, collect open endpoints (own compilation plus marked
   references, memoised), bind, check constraints, and produce closed `EndpointInfo`s (FQN, name, route,
   bound properties, levels, request and response types substituted) that join the normal plan.
4. Group fixes: closed generic group keys, the `IsEnabled` wrapper, and `new static Path` resolution.
5. Runtime: `EndpointNameOf` for closed generics, and `IsEnabled` on the manual `MapGroup` path.
6. Diagnostics: fresh MPEP ids from MPEP025 on (MPEP017 stays reserved).

Rules: netstandard2.0 without list patterns, ranges or `Index`; value-equatable models; generated
code fully qualified, extension methods in static form, zero `using`s; no `CompilationProvider`
combine that defeats caching.

## Phase 4 — Docs, version, sweep, PR: done

README "Generic endpoints" section (compiled) and diagnostics rows; version `11.2.0-rc.0` (a proposal);
one sweep on both TFMs (generator tests, `Tools.Tests`, solution `-t:Rebuild`, 36 CS1591 baseline);
PR closing #34, not merged.

PR [#35](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/pull/35) opened 2026-09-25, CI green, and the oasdiff gate ran for the first time: "No breaking changes to report, but the specs are different" (the three added closed paths). Not merged; version 11.2.0-rc.0 is proposed and the release is the owner's decision.

## Phase 5 — Tools 12 and generated model equality (PRD addendum D10–D18): done (uncommitted, awaiting review)

Investigated 2026-09-25 by three agents (upstream #185, downstream map, end-to-end spike). The spike patch is `scratchpad\spike185\spike.patch`
(373/375 net10.0; the 2 failures are the hash-pinning tests).

1. **Packages:**
   - `MintPlayer.SourceGenerators.Tools` 11.0.0 → 12.0.0 in all three references: Generator, the runtime with `ExcludeAssets=all`, and Generator.Tests.
   - Add `MintPlayer.ValueComparerGenerator` 12.0.0 (`PrivateAssets=all`) and `.Attributes` 12.0.0 (`PrivateAssets=all`, `GeneratePathProperty=true`) to the generator.
2. **Breaking changes:**
   - Remove the `ICompilationCache` parameter from `EndpointGenerator.Initialize`.
   - Remove `using MintPlayer.SourceGenerators.Tools.ValueComparers;`.
3. **Models:** all 14 become `[GenerateEquality] internal sealed partial class`, with the hand-written `Equals`/`GetHashCode` deleted. Add `[EqualityIgnore]` on `ClientServer.IsCacheable` (semantics) and on `EffectiveDescriptorName`, `IsEmpty`, `MethodNameWasSanitised` and `RequestedMethodName` (cost).
4. **Comparers:**
   - Delete `SequenceComparer<T>`, `LocationKeys.AreEqual`, `PathSpecs.AreEqual`, and the two `.WithComparer()` calls after `Collect()`.
   - `openEndpointNamesProvider` returns `.ToEquatableArray()`, and its call site unwraps with `AsImmutableArray()`.
5. **Build and pack:**
   - A named target sets `IncludeRuntimeDependency=false` on the attributes item. This works around the upstream MSB4018 in `GenerateDepsFile`.
   - No extra `GetDependencyTargetPaths` line: the package target already feeds `GetTargetPath`.
   - Pack guards in `PackAnalyzerAssemblies` and `PackEndpointsGenerator`: the attributes dll is present and only in `$(EndpointsAnalyzerPackPath)`.
   - An error when `$(PkgMintPlayer_ValueComparerGenerator_Attributes)` is empty.
   - Trigger each guard once, deliberately.
6. **Warning baseline:** handle the CS1591 from the generated public `IncrementalValueProviderAdditionalEx` per D16 (a documenting partial if the class is partial, otherwise a type-scoped suppression). Keep the baseline at 36.
7. **Tests:**
   - Rewrite the two hash-pinning tests to the equal-models-equal-hashes contract.
   - Add a `ClientServer` `IsCacheable`-ignored test.
   - Incremental and cache test expectations stay unchanged.
8. **Docs and comments** per D18: appended blockquotes in older PRDs, and code comments rewritten where they state the old mechanism.
9. **Line endings:** normalise the edited csprojs to the repo convention. The OpenAPI snapshot must come out byte-identical; the spike showed an EOL-only change.
10. **Sweep:** once, in Release, on net10.0 and net11.0. Generator tests (baseline 375) and Tools.Tests (1085). Solution `-t:Rebuild` warnings deduplicated (36). `dotnet list package --outdated` must be empty. List both nupkgs.
11. **Upstream:** file the MintPlayer.Dotnet.Tools issue(s) for the MSB4018 `IncludeRuntimeDependency`, the public `JoinMethods.g.cs` type, and the contradicted load-time claim.
12. **Commit and push to PR #35,** and update the PR description.

> **Phase 5 targets 12.0.1** (PRD D26), not 12.0.0. The only consumer-visible change is the equality output file name `GeneratedEquality.g.cs`. MintPlayer.SourceGenerators(.Attributes) also moves to 12.0.1 for MustChangePassword and SitemapXml.

## Phase 6 — Generator performance and robustness (PRD addendum 2, D19–D26): done except acceptance 18 (uncommitted, awaiting review)

Baseline measured 2026-09-25 by agent B. Benchmark: `scratchpadench\ZzGeneratorBenchmark.cs` plus `benchun.sh`; numbers in PRD addendum 2. Run it before and after, with the same N and scenarios.

0. **Owner decisions first:** D24 (typed client in one file) and D25 (analyzer folder).
1. **Red first:**
   - D22 group-through-base-class test;
   - D23 fixed-file-set guard for both generators (the client case fails today unless D24 decides otherwise);
   - the D20 line-shift test (fails today);
   - tracking of the `openEndpointNames` step.
2. **D21:** build `EndpointMappingPlan` once and share it with the producers and the reporter; cache `ShadowParameters` in it.
3. **D20:** a location-free projection feeds the producers, and the reporter keeps the located model.
4. **D19:** syntactic fast path for `Path`/`Prefix`/`Methods`; reuse the transform's `SemanticModel`; the interface map only when the member is not declared on the class or is `new`. Every route/diagnostic test and the OpenAPI snapshot stay byte-identical.
5. **D22 fix** if reproduced.
6. **D24:** the typed client writes one `EndpointClients.g.cs` (if chosen). Update the client tests that read `ApiClient.g.cs`.
7. **Benchmark after:** at least 3× faster on the N=500 body edit (acceptance 18). Record every scenario in the PRD.
8. **Docs:** README "What the generator emits" (client file name); PRD as-built notes.
9. **One sweep** together with Phase 5, then commit and push to PR #35. Delete the benchmark file from any tree before committing.

> **Owner decisions (2026-09-25):**
> - **D24:** the client writes one `EndpointClients.g.cs`; step 6 applies.
> - **D25:** move to `analyzers/dotnet/roslyn5.9/cs`. Added as step 6a:
>   - set `$(EndpointsAnalyzerPackPath)` to `analyzers/dotnet/roslyn5.9/cs`;
>   - make the stray-folder guard tolerate upstream's identical-path copies by deduplicating;
>   - list both nupkgs to confirm exactly one analyzer folder;
>   - re-measure SDK 10.0.112 (expect a silent skip and CS1061) and rewrite the README and Generator README "Requirements" to that symptom, for Visual Studio 2026 and .NET SDK 10.0.400+ or 11.x.

> **Superseded by 12.1.0 (commit `1fa1b75`, 2026-09-26).** MintPlayer.Dotnet.Tools#187 was fixed upstream (#188, 12.1.0). So the `IncludeRuntimeDependency` workaround target (Phase 5 step 5) and the CS1591 suppressor project (step 6) are removed, and every MintPlayer package is at 12.1.0. The warning baseline is now **0** (measured: solution `-t:Rebuild -c Release`, 0 warnings), not 36. The attributes-dll pack guards and the `roslyn5.9/cs` folder stay. See the PRD blockquote after the Phase 5 as-built notes.
