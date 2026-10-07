# Implementation plan: service-aware hooks (issues #36, #37; README recipe for #40)

Companion to `PRD-EndpointServiceAwareHooks.md` (Draft 2; all decisions settled).

- **Branch:** `feat/endpoint-service-aware-hooks`.
- **PR:** one PR against `master` with `fixes #36, fixes #37, refs #40`. It is titled as a **breaking**
  change. Claude does not merge it, because merging publishes to nuget.org through `publish-release.yml`.
- **Testing rule:** test runs wait until M6. Earlier milestones are verified with targeted builds only.
  Committing per milestone is fine.

## M0: Investigation and decisions (done, 2026-10-07)

- Four agents investigated the work: I1 (#36), I2 (#37, with a CS8926 prototype), I3 (#40) and I4
  (conventions).
- A grilling session (Q1 to Q7) settled D1 to D8.
- #40 was closed as not planned, so its pipeline design and its spikes were dropped.

## M1: Spikes (before implementation)

Each spike is a throwaway in the scratchpad or a scratch test. Record the result here as a blockquote.

- [ ] **S1: `GetPath` dispatch and detection.**
  - Confirm that `static virtual string? GetPath(IServiceProvider) => null` combined with
    `T.GetPath(sp) ?? T.Path` compiles and dispatches on net10.0 and net11.0, in each of these cases:
    - an implicit override;
    - an explicit override;
    - an override inherited from a base class;
    - an override supplied by an intermediate user interface (`static string? IEndpointBase.GetPath(...)`
      declared in `IMyEndpoint : IEndpoint`);
    - an override that returns null.
  - Prototype the D5 detection, and confirm the semantic fallback catches the last two cases.
  - Check that `EndpointGeneratorIncrementalTests` and the timing tests show no regression for classes
    that override nothing.
- [ ] **S2: MPEP035 detection and break behaviour.**
  - After the signature change, check what each old form does:
    - an implicit `public static void Configure(RouteGroupBuilder)`: expected to compile silently, which
      is what MPEP035 must catch;
    - an explicit `static void IEndpointGroup.Configure(RouteGroupBuilder)`: expected to fail with
      CS0539;
    - an old signature inherited from a base endpoint class.
  - Decide whether MPEP035 also walks base classes, using the same trigger as D5.
  - Confirm the code-fix project can offer "add `IServiceProvider services` parameter". This is an
    optional improvement, worth doing if it is cheap.
- [ ] **S3: Open-generic `GetPath`.**
  - Add `HasPathOverride` to the open-endpoint record (`Producer.cs:568`, `OpenEndpointRecords.cs:129`,
    `EndpointClosing.cs:109,121`).
  - Confirm the closing assembly omits the typed link and contract, reports MPEP034 and maps on the
    resolved path.

## M2: #36, `Configure(…, IServiceProvider)` replaces the old hook (R1)

1. Abstractions: replace the signatures in `IEndpointGroup` and `IEndpointBase`. Update their doc
   comments: root provider, no scoped services, and that `IsEnabled` decides existence.
2. Generator: change the `MapGroup<TGroup>` and endpoint `Map` helpers to pass `routes.ServiceProvider`.
3. Runtime: change `MapGroupCore<TGroup>` and `MapEndpoint<T>` (line 123) the same way.
4. Add MPEP035:
   - the descriptor in `DiagnosticDescriptors.cs`;
   - a syntactic detector over group and endpoint declarations;
   - a code fix, if S2 shows it is cheap.
5. Migrate the in-repo hooks:
   - TestApp `UsersApi` and `ProductsApi`;
   - TestLibrary `LibAuthGroup`;
   - `MapEndpointTests.cs:76`;
   - every test fixture string containing `Configure(RouteGroupBuilder` or
     `Configure(RouteHandlerBuilder`. Grep `Tests/` for these.
6. README:
   - the `IsEnabled`-is-existence paragraph, with the CORS example;
   - the `Configure` section;
   - MPEP035 in the diagnostics table.
7. Verify with a targeted build of the Endpoints solution folder and the test projects. Do not run tests.

## M3: #37, `GetPath(IServiceProvider)` (R2)

1. Abstractions: add `GetPath`. Its doc comment covers:
   - null means `Path`, and never "unmapped";
   - only the root provider is available;
   - the path must keep the same tokens;
   - what is left out.
2. Runtime:
   - `MapEndpoint<T>()` uses `T.GetPath(app.ServiceProvider) ?? T.Path`;
   - `EndpointDescriptor` gains `IsPathConfigurable`.
3. Generator:
   1. `Map` helpers use the `??` expression.
   2. `EndpointInfo.HasPathOverride` is filled in by the D5 detection.
   3. `TypedLinks`, `EndpointContracts` and `ShadowParameters` skip endpoints that have the flag.
   4. `Describe<>` emits `IsPathConfigurable`.
   5. Add MPEP034, reported by `EndpointDiagnosticReporter` in place of MPEP011.
   6. MPEP032 also covers `new static GetPath`.
   7. Carry the open-generic record flag (S3).
4. README: add the `GetPath` section and MPEP034.

## M4: #40, README recipe (R3)

- Add the "Middleware for a group's prefix" subsection with `StartsWithSegments(Group.Prefix)` and the
  nested-group caveat.

## M5: Tests (written alongside M2 to M4, run in M6)

| AC | Test | File |
|---|---|---|
| AC1 | Generated `MapGroup` / endpoint `Map` call `Configure(x, routes.ServiceProvider)` exactly once | `Generator.Tests/EndpointGroupingTests.cs` |
| AC1 | Group and endpoint 2-arg `Configure` applied (generated host), receives root provider | `Generator.Tests/GroupMembershipTests.cs` via `GeneratedEndpointHost` |
| AC1 | Same via `MapEndpoint<T>()` | `Tools.Tests/Endpoints/MapEndpointTests.cs` |
| AC2 | Config-driven tag on/off; routes exist both ways | `Tools.Tests/Endpoints/TestLibraryEndToEndTests.cs` (+ `LibraryEndpoints.cs` fixture reading `IConfiguration`) |
| AC3 | MPEP035 on implicit old group/endpoint `Configure`; not on new signature or unrelated `Configure(int)` | `Generator.Tests/` new `ConfigureSignatureDiagnosticTests.cs` |
| AC3 | Code fix adds the parameter (if built) | `Generator.Tests/` code-fix tests |
| AC4 | `GetPath` from options: resolved path answers, literal 404s (generated) | `Tools.Tests/Endpoints/TestLibraryEndToEndTests.cs` (+ fixture endpoint + option) |
| AC4 | Same via `MapEndpoint<T>()`; override returning null maps at `Path`; no-override unchanged | `Tools.Tests/Endpoints/MapEndpointGenericTests.cs` |
| AC5 | MPEP034 not MPEP011; MPEP009 still fires on default; sibling unaffected | `Generator.Tests/RouteDiagnosticTests.cs` |
| AC5 | Detection: implicit, explicit, base class, intermediate interface | `Generator.Tests/RouteDiagnosticTests.cs` |
| AC5 | No typed link / no client contract | `Generator.Tests/TypedLinkEmissionTests.cs`, `EndpointClientGeneratorTests.cs` |
| AC5 | `IsPathConfigurable` true / false in descriptors | `Tools.Tests/Endpoints/EndpointDescriptorTests.cs` |
| AC5 | `HasPathOverride` participates in incremental caching | `Generator.Tests/EndpointGeneratorIncrementalTests.cs` |
| AC6 | Open-generic `GetPath` closed by app | `Generator.Tests/OpenGenericEndpointTests.cs` |
| AC7 | `new static GetPath` → MPEP032 | `Generator.Tests/` (alongside existing MPEP032 tests) |
| AC8 | README samples compile | README sample-compile script |

## M6: Sweep, docs, PR

1. Rebuild with `-t:Rebuild`. Deduplicate the warnings, then confirm no new ones were introduced.
2. Run the full sweep once on both TFMs, with output redirected to logs. Record the counts against the
   previous run (1097 Tools.Tests and 389 Generator.Tests).
   - `dotnet test Tests/MintPlayer.AspNetCore.Endpoints.Generator.Tests > <scratch>/gen.log 2>&1`
   - `dotnet test Tests/MintPlayer.AspNetCore.Tools.Tests > <scratch>/tools.log 2>&1`
3. Compile-check the README samples, and run the OpenAPI snapshot and oasdiff check.
4. Add an "As built" blockquote to the PRD under the acceptance criteria.
5. Open the PR as **breaking**, with the MPEP035 migration note, `fixes #36, fixes #37, refs #40`, and
   version 11.4.0-rc.0.

## Progress ledger

- 2026-10-07:
  - M0 done. Branch created, and versions bumped from 11.3.0-rc.0 to 11.4.0-rc.0.
  - PRD and PLAN Draft 1 written.
  - Grilling settled D1 to D8 and #40 was closed, which led to Draft 2.
