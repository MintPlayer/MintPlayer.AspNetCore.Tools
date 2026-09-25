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
