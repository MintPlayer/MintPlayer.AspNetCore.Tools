# Implementation plan: service-aware hooks (issues #36, #37; README recipe for #40)

Companion to `PRD-EndpointServiceAwareHooks.md` (Draft 3; all decisions settled).

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

- [x] **S1: `GetPath` dispatch and detection.**
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
- [x] **S2: MPEP035 detection and break behaviour.**
  - After the signature change, check what each old form does:
    - an implicit `public static void Configure(RouteGroupBuilder)`: expected to compile silently, which
      is what MPEP035 must catch;
    - an explicit `static void IEndpointGroup.Configure(RouteGroupBuilder)`: expected to fail with
      CS0539;
    - an old signature inherited from a base endpoint class.
  - Decide whether MPEP035 also walks base classes, using the same trigger as D5.
  - Confirm the code-fix project can offer "add `IServiceProvider services` parameter". This is an
    optional improvement, worth doing if it is cheap.
- [x] **S3: Open-generic `GetPath`.**
  - Add `HasPathOverride` to the open-endpoint record (`Producer.cs:568`, `OpenEndpointRecords.cs:129`,
    `EndpointClosing.cs:109,121`).
  - Confirm the closing assembly omits the typed link and contract, reports MPEP034 and maps on the
    resolved path.
- [x] **S4: Typed binding parity on `MapEndpoint<T>()` (R6, from the Spark review).**
  - Map a generic `IPostEndpoint<TReq>` and a form-urlencoded `BindRequestAsync` endpoint through
    `MapEndpoint<T>()`, with no generator involved.
  - POST each one a valid body, a malformed body, the wrong content type and an empty body.
  - Compare the results with the generated mapping.
  - Outcome: either "verified, regression tests only", or a gap list with a fix that goes into M3c.

### Spike results (2026-10-07; S1 to S4 done; scratch projects are in the session scratchpad)

> **S4: done. Parity already holds, and no fix is needed.** Details are in PRD R6.2.
> - 48 request cases gave identical results through `MapEndpoint<X<User>>()` and through the generated
>   mapping, with and without MVC.
> - Tests to add (AC12):
>   1. `ParameterBindingPipelineTests.cs`: a typed twin of
>      `ManualMapEndpoint_BindsRawEndpointsIdenticallyToTheGeneratedMapping`, using TestApp's
>      CreateUser/UpdateUser. Send valid JSON, malformed JSON, text/plain and an empty body, and require
>      the same status and body as the generated mapping.
>   2. `MapEndpointGenericTests.cs`: a generic `PostThing<T>` mapped as `MapEndpoint<PostThing<User>>()`.
>      Expect 200 with the values bound, 400 for malformed JSON and 415 for text/plain.
>   3. `EndpointInvocationTests.cs`: a generic form-urlencoded `BindRequestAsync` override mapped through
>      `MapEndpoint`. Expect 200 with the fields bound, 400 when an `EndpointBindingException` is thrown,
>      and 415 for JSON.
> - `MapEndpoint<T>()` returns `app` unmapped for a disabled group (`EndpointRouteBuilderExtensions.cs:82`).
>   A disabled endpoint (R5.4) must do the same.

> **S1: done.** `static virtual string? GetPath(IServiceProvider) => null` combined with
> `T.GetPath(sp) ?? T.Path` builds with no warnings on net10.0 and net11.0, and dispatches correctly for
> every case tested:
> - implicit, explicit, and non-nullable `string` overrides;
> - an override on a base class: an implicit one, an explicit one, and a base class that doesn't itself
>   implement the interface but has a `public static GetPath`;
> - an intermediate interface with an explicit default;
> - an override that returns null (falls back to `Path`).
>
> An `internal static GetPath` does **not** implement the member, and the compiler gives no warning.
>
> Detection, refined from D5:
> 1. **Syntactic:** a `GetPath` in any partial declaration of the class counts if it is either
>    `public static` or an explicit `IEndpointBase.GetPath`. Requiring one of these excludes the
>    `internal static` false positive. Do not filter on the return type.
> 2. **Trigger for the semantic check:** the base type is not `object`, **or** the class implements a
>    non-library interface that itself derives from `IEndpointBase`. Use an exact-namespace match via
>    `SymbolNames.IsNamespace(..., "MintPlayer.AspNetCore.Endpoints")`, as the generator does today.
> 3. **Semantic check:** `impl = FindImplementationForInterfaceMember(IEndpointBase.GetPath)`.
>    - The endpoint overrides when `impl.ContainingType` is not `IEndpointBase` (the default member
>      itself, `IsVirtual=true`).
>    - `null` means a diamond (CS8705). The user already has an error, so treat it as overriding.
>
> Roslyn version: 5.9.0, the same as the generator.

> **S2: done.**
> - An implicit one-argument `public static Configure`, on the type or on a base class, compiles silently
>   and is never called. An explicit one fails with CS0539.
> - Adding `IServiceProvider services` is the whole fix.
>
> MPEP035 implementation:
> - **Detection** goes in `Discover` (`EndpointGenerator.cs:158-172`), after an endpoint or group has been
>   identified. It walks `symbol`, then each `BaseType` that has `DeclaringSyntaxReferences`. Abstract
>   bases are never discovered on their own, so the walk is required; bases from metadata are skipped.
>   It matches a static method named `Configure` with exactly one parameter whose rightmost simple type
>   name is `RouteGroupBuilder` (for a group) or `RouteHandlerBuilder` (for an endpoint).
> - **Model:**
>   - A new `[GenerateEquality] LegacyConfigureHook(TypeName, ParameterType, LocationKey)`.
>   - `DiscoveredType.LegacyHooks` (`Models.cs:344-355`).
>   - A separate `SelectMany(...).Collect()` branch with a new `TrackingNames.LegacyConfigureHooks`.
>   - A new `EndpointModel` constructor parameter, emptied in `WithoutLocations()`.
> - **Reporting:** in `EndpointDiagnosticReporter.Collect`, add a
>   `foreach (var hook in model.LegacyHooks.Distinct())` after the group loop (`:124-128`).
>   `Distinct` avoids reporting a shared base twice.
> - **Descriptor:** `LegacyConfigureHookIgnored`, Error.
> - **Code fix:** cheap. Model `AddServiceProviderParameterCodeFixProvider` on
>   `MakePartialCodeFixProvider.cs`. It appends `global::System.IServiceProvider services` with the
>   `Simplifier` annotation and supports `BatchFixer`. Copy the tests from `MakePartialCodeFixTests.cs`.
>   **Do it.**
>
> Migration list:
> - Declarations:
>   - `Abstractions/IEndpointGroup.cs:28`, `IEndpointBase.cs:36`
>   - `TestLibrary/LibraryEndpoints.cs:15`
>   - `TestApp/Endpoints/ProductsApi.cs:11`, `UsersApi.cs:11`
>   - README `:110`, `:550`
>   - `Generator.Tests/Infrastructure/FixtureSources.cs:50`
>   - `Tools.Tests/Endpoints/MapEndpointTests.cs:55,76,299`, `IsEndpointMappedTests.cs:159`
> - Call sites:
>   - `EndpointRouteBuilderExtensions.cs:123,292`
>   - `EndpointGenerator.Producer.cs:414,484`
>   - `Tools.Tests/Endpoints/HttpMethodInterfaceTests.cs:311`

> **S3: done.**
> - **The open-endpoint record** is `OpenEndpointAttribute` (`Abstractions/OpenEndpointAttribute.cs:18-34`,
>   currently Version 1). Add `bool PathConfigurable { get; set; }`:
>   - emit `PathConfigurable = true` only when the flag is set (next to `Producer.cs:570`);
>   - read it with a new `case "PathConfigurable"` in `OpenEndpointRecords.cs:117-138`;
>   - add it to `OpenEndpointRecord`.
>
>   Do **not** bump `Version`: an older reader ignoring an unknown key is the right way to degrade.
> - **Carrying the flag to closed endpoints:**
>   - Add it to `OpenCandidate` (`EndpointClosing.cs:57-65`).
>   - Fill it from `declared.HasPathOverride` at `:109` and from `record.PathConfigurable` at `:121`.
>   - Pass it into the `EndpointInfo` constructor at `:409-428`.
>   - Add it to `Models.cs` (constructor `31-60`, equality list `~228`).
> - **Descriptor:** `EndpointDescriptor` (`Abstractions/EndpointDescriptor.cs:31`) gains a fifth
>   positional parameter, `bool IsPathConfigurable`, which must also go into its hand-written
>   `Equals`/`GetHashCode` (`38-58`). `Describe<>` (`Producer.cs:447-450`) takes the flag, emitted per
>   endpoint as `true`/`false` (`:131-132`), and keeps the literal `Path`.
> - **Exclusions:**
>   - `TypedLinks.cs:128-129` and `EndpointContracts.cs:93-94` skip endpoints with the flag
>     (`|| endpoint.HasPathOverride`).
>   - `EndpointMappingPlan.cs:291` (`ShadowParametersOf`) passes a null route for them.
>   - `ComposedRoutes` stays as it is, because MPEP007, 009 and 010 still use it.
> - **Diagnostics:**
>   - `EndpointDiagnosticReporter.cs:194-197`: report MPEP034 in place of MPEP011 when the flag is set.
>   - Closed endpoints never reach that loop (it iterates only `DeclaredEndpoints`). Add a loop over
>     `MappableEndpoints.Where(e => e.Closed is not null && e.HasPathOverride)` that reports MPEP034 at
>     the closing attribute's location.
>   - MPEP007 may report a false positive when two overriding endpoints share a default `Path`. That is
>     accepted under D4 (the default paths really do collide), and gets a README note.
> - **Tests:**
>   - Defining side: use `OpenGenericEndpointTests.cs:61,94` as the template.
>   - Closing side: use `EndpointTypeArgumentTests.cs:149` (`Library()`/`App()` harness),
>     the `[Theory] crossAssembly` tests at `616-693`, and `:484`.
>   - Runtime resolved path: `TestLibraryEndToEndTests`.

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
5. **R2.10:** add the runtime helper `EndpointPathValidator.EnsureSameParameters(Type endpoint, string configured, string @default)`.
   - It uses `RoutePatternFactory.Parse` and compares parameter names case-insensitively.
   - It throws `InvalidOperationException` naming the endpoint, both patterns and each side's missing
     names.
   - Call it from the generated `Map` helpers (fully qualified, static form) and from `MapEndpoint<T>()`,
     only when `GetPath` returned a non-null value.
   - README: say the startup check enforces the same-parameters rule.

## M3b: Endpoint-level `IsEnabled` and one role per class (R5; D9, D12)

1. Abstractions: add `static virtual bool IsEnabled(IServiceProvider services) => true;` to
   `IEndpointBase`, with a doc comment covering root provider only, map-time evaluation, and that
   `GetPath` and `Configure` are skipped when it returns false.
2. Generator: emit a generic helper, `IsEndpointEnabled<TEndpoint>(IServiceProvider)`, and wrap every
   endpoint's `Map…` call in `if (IsEndpointEnabled<T>(app.ServiceProvider)) { … }`, inside its group
   block. That covers both declared and closed open-generic endpoints.
3. Runtime: `MapEndpoint<T>()` checks `T.IsEnabled(app.ServiceProvider)` after the group-chain check
   (`EndpointRouteBuilderExtensions.cs:78-83`).
   - For a disabled endpoint it returns the same value as for a disabled group (S4 confirms what that
     is), and the doc comment says so.
   - Order: group chain → endpoint `IsEnabled` → `GetPath` → R2.10 → `MapMethods` → `Configure`.
4. Add MPEP036 (Error), raised when a class is both a group and an endpoint.
   - Detect it in `Discover` on `AllInterfaces`.
   - Report it at the class identifier.
   - Treat the type as a group only.
   - First grep the repo, fixtures and README for existing classes that are both, and migrate them.
5. README:
   - Add an "Enabling a single endpoint" subsection with the `/manage` example.
   - Add the "a class is a group or an endpoint, not both" rule.
   - Add MPEP036 to the diagnostics table.
   - Note that the descriptor list ignores endpoint `IsEnabled`.
   - Fix the Spark follow-up wording.

## M3c: Typed binding parity on `MapEndpoint<T>()` (R6; D11)

- S4 verified that parity already holds, so no code change is needed. This milestone is just the three
  AC12 regression tests listed in the S4 result.

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
| AC10 | Param-name mismatch throws at startup (generated + `MapEndpoint<T>()`); literal/constraint/case differences pass | `Tools.Tests/Endpoints/` new `EndpointPathValidatorTests.cs` + `MapEndpointGenericTests.cs` + `TestLibraryEndToEndTests.cs` |
| AC11 | Disabled endpoint: 404, `Configure`/`GetPath` counters stay 0, siblings mapped, `IsEndpointMapped<T>` false (both paths) | `Tools.Tests/Endpoints/MapEndpointGenericTests.cs`, `IsEndpointMappedTests.cs`, `TestLibraryEndToEndTests.cs` |
| AC11 | Generated code wraps each endpoint in `IsEndpointEnabled<T>` inside its group block | `Generator.Tests/EndpointTypeArgumentTests.cs` (next to `EveryGroup_IsWrappedInItsIsEnabledCheck`) |
| AC12 | Typed twin of the raw parity test: CreateUser/UpdateUser via `MapEndpoint<T>()` vs generated, same status + body | `Tools.Tests/Endpoints/ParameterBindingPipelineTests.cs` |
| AC12 | Generic `PostThing<User>` via `MapEndpoint<T>()`: 200 bound / 400 malformed / 415 text/plain | `Tools.Tests/Endpoints/MapEndpointGenericTests.cs` |
| AC12 | Generic form `BindRequestAsync` override via `MapEndpoint<T>()`: 200 / 400 / 415 | `Tools.Tests/Endpoints/EndpointInvocationTests.cs` |
| AC13 | MPEP036: direct, via base class, via intermediate interface; not for plain group/endpoint | `Generator.Tests/` new `GroupEndpointRoleDiagnosticTests.cs` |

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
  - Spikes S1 to S3 done. Draft PR #41 opened with the plan only.
  - The Spark consumer review on #41 led to PRD Draft 3:
    - D9: endpoint-level `IsEnabled`;
    - D10: startup check on route parameters;
    - D11: typed-binding parity on `MapEndpoint<T>()`. Spike S4 verified it, so only tests are needed.
  - Owner decision D12: a class is never both a group and an endpoint (MPEP036).
  - The Spark session confirmed Draft 3 meets all its requirements, and no Spark class is both a group
    and an endpoint.
  - Owner decision D13: endpoints are fixed at startup (no runtime toggling).
  - The owner gave the go-ahead for implementation.
