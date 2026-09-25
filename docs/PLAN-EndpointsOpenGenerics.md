# Implementation plan: open-generic endpoint classes (issue #34)

Companion to `PRD-EndpointsOpenGenerics.md`. Branch `fix/endpoints-open-generics`. One PR.

## Phase 0 — Investigation (three agents in parallel, each in its own git worktree)

Worktrees keep the builds apart: concurrent builds of the generator in one tree lock its DLLs.

- **I1 — Reproduce and audit the generated output.** In `Tests\MintPlayer.AspNetCore.Endpoints.Generator.Tests`,
  write the issue's repro as a failing (red) test. Then build a matrix: for each open shape in R1
  (own type parameter; nested in a generic container; open group; typed levels using the type
  parameter; `[RouteParam]` on a generic partial; closed derived from a generic base), record which
  generated files contain the unbound type parameter, the compiler errors, and whether the
  endpoint's own partial declaration repeats the type parameters and constraints. Output: the
  matrix, plus the red tests (not committed).
- **I2 — The manual path with closed generics.** Measure `MapEndpoint<Echo<A>>()` and
  `MapEndpoint<Echo<B>>()` in one TestServer app (a scratch copy with the generic class excluded
  from generation or in a project without the generator), covering route, prefix, inherited
  `[MemberOf<T>]`, the `WithName` value (`Echo\`1`?), duplicate names across two closings,
  OpenAPI `operationId`s, typed levels, and bound properties. Output: what works, what breaks,
  with the exact error text.
- **I3 — Design of R6 (library shape).** Read-only: the generator's emission, the Spark adoption
  PRD linked from the issue, and how comparable libraries handle generic endpoints. Evaluate (A)
  skip + diagnostic against (B) a generated generic mapping method. For (B): naming, grouping by
  type-parameter list and constraints, mixed parameter names, nested open containers, interaction
  with `EndpointsMethodName`. Output: a recommendation with the traps.

## Phase 1 — Decide

Fold the findings into the PRD as appended blockquotes: fix R1's shape list, settle R4 (ids) and R6
(A/B/C), and correct any **[verify]** claim.

## Phase 2 — Red

Commit-free until green. The issue's repro plus one test per open shape; the manual-path runtime
test from acceptance 4; the closed-derived regression test. All must fail, or pass for the right
reason (the regression test), on the unfixed generator.

## Phase 3 — Green

- Detection helper (type parameters on the class or any container), carried on the value-equal
  `EndpointInfo`/`GroupInfo`.
- Filter open endpoints and groups out of `EndpointMappingPlan` (the same place MPEP024 filters),
  so the mapping, descriptors, `Routes`, contracts and OpenAPI hooks never see them, and duplicate
  route/name checks ignore them.
- Keep the partial emission for open endpoints, with type parameters (and constraints where C#
  requires them) repeated.
- The new Info diagnostic.
- Manual-path fixes from I2 (endpoint naming for closed generics, if needed).
- The R6 outcome.

Generator rules: netstandard2.0 without list patterns, ranges or `Index`; value-equatable models;
generated code fully qualified, extension methods in static form, zero `using`s.

## Phase 4 — Docs, version, sweep, PR

README "Generic endpoints" section and diagnostics-table row; version `11.1.1-rc.0` (proposal);
one sweep on both TFMs (generator tests, `Tools.Tests`, solution `-t:Rebuild`, 36 CS1591
baseline); PR referencing #34, not merged.
