# Implementation plan: endpoint ergonomics

Companion to [PRD-EndpointsErgonomics.md](PRD-EndpointsErgonomics.md). Requirement IDs
(`R1.1`, …), problem IDs (`P1`, …) and diagnostic ids (`MPEP007`, …) refer to that
document.

Branch: `feature/endpoints-ergonomics`. One PR, per the repo convention — including the
packaging guards, the Tools upgrade, the README rewrite, the code-fix assembly and the CI
snapshot gate. Nothing here is deferred to a follow-up.

**Test runs are batched.** The suite runs once, in M13. Intermediate milestones are
verified by building and by reading the generated output, not by running 1,976 tests
fourteen times. The one exception is M0: the spikes run their own throwaway projects,
which are not the suite.

**The spikes are not optional and they come first.** Four of the six (S1, S2, S4, S5) can
each invalidate a milestone that would otherwise be written blind, and S1 decides a
requirement the PRD deliberately leaves open.

**This is a large PR.** If it has to shrink, the designated cut is M6 — the Tier-3
diagnostics are purely additive and nothing else depends on them. Do not cut M2; four
later milestones depend on it.

---

## M0 — Spikes (before implementation)

- [ ] **S1 — Can an OpenAPI schema transformer restore typed schemas over a string-typed
      shadow parameter?**
      Build a minimal app with `MapMethods(path, methods, ([AsParameters] Shadow s,
      HttpContext ctx) => …)` where every `Shadow` member is `string?`. Add an
      `AddOpenApiOperationTransformer` that rewrites each path parameter's schema to the
      real CLR type (`integer`/`int32`, `$ref` for an enum) and confirm the dumped
      `/openapi/v1.json` matches what a typed shadow produces — while `GET /users/abc`
      still reaches the library's binder and returns the per-parameter 400 message.
      **If it works**, R4's decision holds and M5 ships both properties. **If it does
      not**, fall back to the untyped shadow and accept `type: string`, per the PRD's
      stated fallback. Do not silently choose the typed shadow — a bodyless 400 is a
      regression against today's `EndpointBindingException` behaviour.

- [ ] **S2 — Does `[ValidatableType]` plus a library-side `TryGetValidatableTypeInfo`
      call actually validate?**
      A request type marked `[ValidatableType]` with `[Range(1,1000)]`, in a consumer
      project calling `AddValidation()`; the library resolves
      `IOptions<ValidationOptions>`, calls `TryGetValidatableTypeInfo`, runs it, and maps
      failures to 400. Confirm the out-of-range value returns 400 with the field key, and
      confirm the null-check path leaves an app that never called `AddValidation()`
      working. Also confirm the behaviour when the request type lives in a **different
      assembly** from the `AddValidation()` call, because the validation generator only
      covers its own assembly.
      **If it fails**, R5 reduces to MPEP015 alone and the README must state plainly that
      automatic validation does not work and why.

- [ ] **S3 — Does `ForAttributeWithMetadataName` fire on `[MemberOf<UsersApi>]` inside the
      real generator, against Tools 10.21.0?**
      The string is `"MintPlayer.AspNetCore.Endpoints.MemberOfAttribute\`1"`. It has been
      proven on Roslyn 4.14.0 in a probe; this re-confirms it after the R6.5 upgrade and
      inside `EndpointGenerator`'s actual pipeline. Assert a **positive match count**, not
      just generated output — the two wrong metadata-name forms produce zero hits with no
      error and no warning, which is indistinguishable from "no endpoints in this project".

- [ ] **S4 — Does the Tools 10.16.0 → 10.21.0 upgrade keep the analyzer loadable?**
      Upgrade, build, then `dotnet pack` in **Debug and Release**, unpack both nupkgs, and
      diff the `analyzers/**` entries — they must be identical to each other and the
      analyzer must be present in both. Then consume the packed nupkg from a scratch
      project on the pinned SDK and confirm the generator runs. The Roslyn floor moves
      4.14.0 → 5.x; the analyzer folder convention in the sibling repo moved to
      `analyzers/dotnet/roslyn5.0/cs`. Confirm which folder this package must use.

- [ ] **S5 — Does a `CodeFixProvider` assembly pack alongside the generator without
      breaking it?**
      A code fix needs `Microsoft.CodeAnalysis.Workspaces.Common`, which is **not** loaded
      in the `csc` analyzer host, so it must ship as a separate assembly in
      `analyzers/dotnet/cs` and only ever runs in the IDE. Build the smallest possible fix
      (MPEP001 → "Make 'X' partial"), pack it, and verify by unpacking that **both**
      assemblies are present and that the generator still loads in a plain `dotnet build`
      with the code-fix assembly present. The `PackEndpointsGenerator` target has already
      failed silently once; this is the second thing being threaded through it.

- [ ] **S6 — Does `[assembly: EndpointContract(...)]` survive the assembly boundary in the
      real solution shape?**
      Three projects: abstractions, a server assembly whose generator emits the assembly
      attributes, and a client assembly with no source access to the server's endpoints.
      Confirm a generator running in the client reads them via
      `IAssemblySymbol.GetAttributes()`, and confirm the control — that the `Path` literal
      itself is gone (`DeclaringSyntaxReferences.Length == 0`). Also confirm what the
      client assembly is forced to reference, since dragging ASP.NET Core server types
      into a Blazor WASM client is the thing that would make M9 not worth shipping.

**Gate — do not start M1 until these are answered:**

- [ ] S1 answered, and the R4 decision recorded in the PRD as either confirmed or fallen
      back, with the observed OpenAPI JSON quoted.
- [ ] S2 answered, and R5 either confirmed or reduced to MPEP015.
- [ ] S3 asserts a positive match count.
- [ ] S4 shows identical `analyzers/**` between Debug and Release.
- [ ] S5 shows both assemblies packed and the generator still loading.
- [ ] S6 answered, and M9's cost to a client project recorded.

If any of these fail, fix the design here rather than proceeding. A packaging defect found
in M13 costs a release; found in M0 it costs an afternoon. An S1 answered wrong ships a
library whose error messages are worse than the ones it replaced.

---

## M0 outcome

*(One bold-led paragraph per spike. Corrections to the PRD are appended there as
blockquotes, per this repo's convention — never rewritten in place.)*

**S1 — PASS, and the chosen option beats the alternative rather than tying it.** A
`string?` shadow plus a ~70-line schema transformer produced a parameter document
**byte-identical** to a typed shadow's on both net10.0 and net11.0, while keeping the
library's `application/problem+json` 400 (`"The route parameter 'id' must be a valid
Int32; 'abc' is not."`) where the typed shadow returns a zero-length body with no content
type. It also does things the typed shadow cannot: emit an enum's `enum: [0,1,2]` list —
a typed `ItemKind?` yields a bare `"type": "integer"` — and carry `minimum`/`maximum`/
`maxLength` from `[Range]`/`[StringLength]`. The typed shadow additionally binds enums
**case-sensitively** and accepts undefined values, making it the only mode where the
framework and the library disagree about what is valid. R4's decision is confirmed; the
`type: string` fallback is not needed.

Three corrections went back into the PRD. **R4.6 was wrong** — one source file compiled
unchanged against Microsoft.OpenApi 2.12.0 and 3.10.0 with zero `#if` and zero warnings,
so no per-TFM code is planned. **R4.7 is new** — `AddOpenApi`/`AddOpenApiOperationTransformer`
are not in the shared framework (verified across all 142 assemblies of
`Microsoft.AspNetCore.App\10.0.12`), so the runtime library needs a package reference on
`Microsoft.AspNetCore.OpenApi`. **R4.8 is new** — an optional route token `{term?}` is
documented as `required: true` with no separate path entry, in both modes; an operation
transformer cannot add a path key and OpenAPI forbids an optional path parameter, so this
is a named accuracy gap rather than a fixable defect.

One trap recorded for M5: an `int` path parameter is emitted by ASP.NET Core as
`pattern` + `type: ["integer","string"]` + `format: "int32"`, **not** plain
`type: integer`. A first transformer wrote the obvious shape and only the byte-diff caught
it — which is why M5's gate compares whole documents rather than inspecting fields.

**S2 — PASS, and R5 does not reduce to a diagnostic plus a paragraph.** A
generator-shaped call site with no typed parameter returned 400 `application/problem+json`
with every field key populated, on both TFMs; nested members report as `Inner.Name`, and
the inner type does **not** need its own attribute because the generated resolver recurses
into complex members automatically. The library's explicit path is *more* RFC-compliant
than the framework's own filter, which omits `type` and `status`.

Four things went back into the PRD, and M7 is bigger than it looked. **R5.2's null check
is wrong** — `IOptions<ValidationOptions>` always resolves, so `TryGetValidatableTypeInfo`
returning false is the only real guard. **R5.5 is new and is the condition most likely to
bite**: a `[ValidatableType]` in a contracts assembly with `AddValidation()` called
elsewhere is silently undiscovered; two remedies are proven. **R5.6 is new**: on net10.0
ASP0029 is an *error*, and it covers not just the attributes but the whole API surface the
library itself calls — so `MintPlayer.AspNetCore.Endpoints` needs the `NoWarn` in its own
csproj, not only consumers. **R5.7 is new and is the real cost**: `IValidatableInfo` →
`IValidatableTypeInfo`, `ValidateContext.ValidationContext` removed, and `ValidationErrors`
changes shape between the TFMs, so the call needs `#if NET11_0_OR_GREATER`. A working
reference implementation exists at `scratchpad\spike-S2\LibSim\EndpointRunner.cs`.

Ironic footnote: R4.6's per-TFM concern was retracted for `Microsoft.OpenApi` and turns
out to be true for `Microsoft.Extensions.Validation` instead.

**S6 — PASS, and the client reference shape is the whole answer.** A generator in a client
assembly read all four contracts out of the server's metadata, with the control confirming
the `Path` literal is gone (`DeclaringSyntaxReferences.Length = 0`,
`Locations = [MetadataFile]`). A raw non-`partial` endpoint's contract crossed identically,
which is the case a type-level attribute could not have reached.

The decisive finding is negative: **`ProjectReference` is fatal and cannot be mitigated.**
It propagates the server's `FrameworkReference Microsoft.AspNetCore.App`, and a Blazor WASM
client then fails with `NETSDK1082 … no runtime pack … for 'browser-wasm'`. Four
mitigations were tested and all four still fail, because the client fails in
`ProcessFrameworkReferences` before the server project is even built. The working shape is
a metadata-only `<Reference>` with `<Private>false</Private>`: **+1 compile-time reference
assembly, 0 shipped bytes, no ASP.NET Core in the client**, and a loud `MSB3245` + `CS1061`
if the server DLL is missing. Emitting the attributes into Contracts instead was tested and
produces nothing, since Contracts has no endpoint types and cannot reference Server without
a cycle.

**S3 — PASS.** `"MintPlayer.AspNetCore.Endpoints.MemberOfAttribute\`1"` matched 5 targets
inside the real generator on both TFMs; the two wrong forms matched **0 with no error and
no warning**, and a deliberately-broken negative control failed the assertion loudly, which
is the property R1.3 requires. `attr.AttributeClass.TypeArguments[0]` reads the group;
`GetAttributes()` returns nothing for a derived type; `GetAllBaseTypes()` walks two levels
to find it. Two notes for M3: the helper is **self-inclusive and includes `System.Object`**,
so R1.4's nearest-declaration-wins rule falls straight out of taking the first hit (proven
— a derived type's own `[MemberOf<UsersApi>]` beat its base's `ProductsApi`); and an
attribute on two partial parts fired the transform **twice for one symbol** with a
different type argument each time, collapsing to 3 symbols from 5 fires under
`SymbolEqualityComparer.Default`. Also proven across a package boundary.

**S4 — FAIL, and R6.5 is withdrawn.** The upgrade builds and packs cleanly with identical
Debug/Release `analyzers/**`, so the gate item as written passes — and the package is still
broken. Published Tools 10.19.0+ bind Roslyn **5.3.0.0**, and SDK 10.0.112 (Roslyn 5.0.0)
rejects the analyzer with `CS9057` then fails the consumer with `CS1061` on every generated
call. The PRD's premise, "every SDK that can build net10.0 ships Roslyn 5.x", is true and
irrelevant: the boundary is 5.3. Renaming to `analyzers/dotnet/roslyn5.0/cs` was tested
directly and produced the identical failure, because the folder controls selection and not
binding. Since nothing else in this PR depends on the upgrade, it is dropped.

S4 also found a **live defect that predates it**: Tools' own props packs `roslyn4.0` and
`roslyn4.9` copies beside this repo's `analyzers/dotnet/cs`, the SDK resolves more than one
as `@(Analyzer)`, and the generator runs twice — `CS0101`/`CS0111` for the consumer. That
is fixed here regardless (R6.5a).

**S5 — PASS, +4.9 KB.** A code-fix assembly sits in `analyzers/dotnet/cs` without disturbing
the generator: `csc` reads its metadata, finds no `[Generator]`, never resolves Workspaces,
and emits not even a CS8033. Debug and Release payloads identical; the new `<Error>` guards
fail the pack in both directions. Four shipping packages (CommunityToolkit.Mvvm,
Meziantou.Analyzer, GraphQL.Analyzers, Avalonia) confirm Workspaces must **not** be packed
beside it — the IDE host supplies it, and a copy would bind the wrong Roslyn.

One correction to M11: `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing.**XUnit**` does not
work here. Its newest version is 1.1.2, built against xunit 2.4.x, and on this repo's xunit
2.9.3 every assertion dies with `MissingMethodException: Xunit.Sdk.EqualException..ctor`.
Use `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` 1.1.2 with `DefaultVerifier`, which has
no xunit dependency at all. Since MPEP001 comes from a *generator*, the analyzer slot is
`EmptyDiagnosticAnalyzer` and the diagnostic arrives via `GetSourceGenerators()`.

Two consequences went into the PRD as R7.4 and R7.4a. M9 must ship the reference snippet
as documentation or a targets file, because getting it wrong is an `MSB3245` + `CS1061`
cascade rather than a clear message, and the server must be built before the client with a
configuration-sensitive `HintPath`. And the client generator must treat zero contracts as
an empty client rather than an error, because roslyn#57997's IDE-only failure is **not
detectable from a command-line build** — measured indistinguishable on every shape.

---

## M1 — Hygiene, packaging and the toolkit upgrade (P6, P7, P8, P11; R6.1–R6.5)

Cheap, independent, and it de-risks everything after it. None of this depends on a design
decision.

- [ ] `EndpointGenerator.Producer.cs:46` — emit real `///` docs for the three public
      generated members (`{Assembly}EndpointsExtensions`, `Map{Assembly}Endpoints`,
      `Endpoints`), which also gives them IntelliSense they have never had. Add
      `#pragma warning disable CS1591` as a belt-and-braces second line. (R6.1)
- [ ] `MintPlayer.AspNetCore.Endpoints.csproj` — add `<Error>` guards to
      `PackEndpointsGenerator`, testing `Exists('%(x.FullPath)')` for literal includes and
      `'@(x)' == ''` for globs. **Never `<Warning>`** — an empty analyzer payload installs
      happily and does nothing. (R6.2)
- [ ] `EndpointGenerator.Producer.cs:67-82` — replace the flat `namespace { … }` block with
      `using (writer.OpenPathSpec(endpoint.PathSpec))`, and add `PathSpec` to `EndpointInfo`
      via `INamedTypeSymbol.GetPathSpec(ct)`. (R6.3, P7)
- [ ] `FixtureSources.cs` — add a **nested** endpoint (`class Outer { partial class Inner :
      IGetEndpoint<…> }`) to `Corpus`. This is the fixture whose absence hid P7.
- [ ] `EndpointRouteBuilderExtensions.cs:31` — `[DynamicallyAccessedMembers(PublicConstructors)]`
      on the `TEndpoint` type parameter; `:93` — `[DynamicallyAccessedMembers(Interfaces)]`
      on `ParentGroupOf`'s parameter. Add `<IsAotCompatible>true</IsAotCompatible>` to the
      two shipping projects as a regression gate. (R6.4)
- [ ] `EndpointGenerator.Producer.cs:188-241` — rewrite every extension-method call in the
      emitted helpers to its fully-qualified static form:
      `global::Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(routes, …)`,
      `…MapGroup(routes, …)`, and the same for `.Produces<T>()`, `.Accepts<T>()`,
      `.ProducesProblem()` and `.WithMetadata()` added in M5. Then **delete the `using`
      block** from the generated file. Types are already `global::`-qualified; extension
      calls are the one remaining dependency on the consumer's import set. (R6.9)
- [ ] ~~Upgrade `MintPlayer.SourceGenerators.Tools` 10.16.0 → 10.21.0.~~ **Withdrawn by
      spike S4** — every published Tools version from 10.19.0 binds Roslyn 5.3.0.0, which
      SDK 10.0.112 (Roslyn 5.0.0) rejects with CS9057 and then CS1061 on every generated
      `Map…Endpoints` call. Renaming the analyzer folder was tested and does not help.
      The test csproj comment recording *"verified 4.14.0"* therefore stays correct. (R6.5)
- [ ] Ship **exactly one** analyzer folder. `Tools`' own `build/*.props` contributes
      `analyzers/dotnet/roslyn4.0/cs` and `roslyn4.9/cs` copies beside this repo's
      `analyzers/dotnet/cs`, the SDK resolves more than one as `@(Analyzer)`, the generator
      runs twice, and the consumer gets `CS0101`/`CS0111`. Pre-existing on the shipped
      package. (R6.5a)
- [ ] Make the pack path a `$(EndpointsAnalyzerPackPath)` property rather than the
      hard-coded literal, so the folder is one property to change when a Tools release
      binds 5.0.0 and R6.5 can be revisited.
- [ ] **Do not touch** `coverlet.runsettings`. Its `DeterministicReport=false` is correct
      only because `ContinuousIntegrationBuild=true` sits on the pack step. If this
      milestone edits that file, the milestone is wrong.

**Gate — verified by building and by unpacking a real nupkg, not by reading a log:**

- [ ] A scratch consumer with `GenerateDocumentationFile=true` and
      `TreatWarningsAsErrors=true` builds clean; zero CS1591 from `EndpointMapping.g.cs`.
- [ ] Deleting the generator's build output makes `dotnet pack` **fail** with the new
      `<Error>`, not succeed with an empty `analyzers/`.
- [ ] Debug and Release nupkgs have identical `analyzers/**` entries.
- [ ] The nested fixture endpoint compiles (it does not today).
- [ ] The generated file contains **no `using` directives**, and a generator test asserts
      it — the assertion is on the emitted text, because a compile-only check passes
      whenever the consumer happens to import the right namespaces.
- [ ] `GeneratedCodeUsingsTests` gains three hostile consumers, all of which must compile:
      `<ImplicitUsings>disable</ImplicitUsings>`; a consumer declaring its own
      `MapMethods` extension on `IEndpointRouteBuilder`; and one with a namespace that
      shadows `Microsoft.AspNetCore.Builder`. At least one of these fails today.
- [ ] `dotnet build -t:Rebuild` warning count, deduplicated, is no worse than the recorded
      baseline.

---

## M2 — Route capture (P3; R3.1, R3.2)

The foundation. M5, M6, M8 and M9 all depend on it, and it is ~40 lines.

- [ ] `Models.cs` — add `Route` to `EndpointInfo` and `Prefix` to `GroupInfo`, both
      `string?`.
- [ ] `EndpointGenerator.cs:99-213` — recover the literal with
      `SemanticModel.GetConstantValue` over the property's expression body, initializer,
      getter arrow, or single-`return` getter. Handle the ten proven-recoverable forms;
      return `null` for the five proven-unrecoverable ones rather than guessing.
- [ ] `EndpointMappingPlan.cs` — compose the group chain into a resolved route per
      endpoint, reusing the existing `GroupChains`.
- [ ] Parse each template **once** per pipeline run into an equatable parameter list
      (name, optionality, catch-all, constraint text) and cache it. Do not re-parse per
      diagnostic. (R3.5)
- [ ] **Extend `EndpointInfo.Equals` (`Models.cs:88-103`)** with the new fields, and with
      `SequenceComparer<T>.Instance` for the parameter list. `ImmutableArray<T>` equality
      is by reference; forgetting this kills incremental caching **silently**. (R6.7)

**Gate — verified by building and by reading generated output:**

- [ ] A generator test asserts the recovered route for each of the ten recoverable forms
      and `null` for each of the five unrecoverable ones.
- [ ] `EndpointGeneratorIncrementalTests` still reports a cache hit on an unchanged
      compilation and on a handler-body-only edit. **If this fails, the `Equals` above is
      wrong** — that is exactly the failure mode this gate exists for.

---

## M3 — Group membership by attribute (P2; R1.1–R1.7)

- [ ] `Abstractions` — add `MemberOfAttribute<TGroup> : Attribute where TGroup :
      IEndpointGroup`, `AttributeUsage(AttributeTargets.Class, AllowMultiple = false,
      Inherited = true)`.
- [ ] `IEndpointGroup.cs:43` — **delete** `IMemberOf<TGroup>`. (R1.2)
- [ ] `EndpointGenerator.cs` — replace the `AllInterfaces` walks at `:122-135` and
      `:265-281` with attribute lookup. Use `ForAttributeWithMetadataName` with the
      arity-encoded name from S3; read the group via
      `attr.AttributeClass.TypeArguments[0]`. De-duplicate targets by
      `SymbolEqualityComparer` and read the authoritative set from `ctx.TargetSymbol` —
      an attribute on two partial parts fires the transform twice. (R6.8)
- [ ] Implement **nearest-declaration-wins** over `GetAllBaseTypes()` from the Tools
      package. (R1.4, R1.5)
- [ ] `EndpointGenerator.cs:74-95` — drop the dead `name.StartsWith("IMemberOf")` arm from
      `IsEndpointCandidate`.
- [ ] `EndpointRouteBuilderExtensions.cs:91-107` — `ParentGroupOf` switches to
      `GetCustomAttribute(inherit: true)`, implementing the **same** nearest-wins rule.
- [ ] `EndpointAttributes.cs:30-36` — exclude `MemberOfAttribute<>` from `IsMeaningful`, or
      it leaks into `endpoint.Metadata`. (R1.7)
- [ ] `DiagnosticDescriptors.cs` — delete MPEP003 and MPEP004; reword MPEP005's message,
      which names `IMemberOf<T>` literally. Do **not** reuse 003/004 for anything new.
- [ ] Update all 8 TestApp endpoint/group files and the generator fixtures.

**Gate:**

- [ ] `[MemberOf<UsersApi>]` resolves the same routes the interface did, asserted against
      the existing grouping tests.
- [ ] A **parity matrix** asserts generated and manual registration agree for: no
      attribute; on self; on base; on both (derived wins); two levels up.
- [ ] `[MemberOf<NotAGroup>]` is **CS0311**; two on one type is **CS0579**; two on separate
      partial parts is **CS0579**.
- [ ] `MapEndpointTests`' metadata-filtering assertions still pass — the new attribute must
      not appear in endpoint metadata.

---

## M4 — Request binding (P1, P10; R2.1–R2.14)

The largest milestone. Build it in this order; each step is independently readable in the
generated output.

- [ ] `Abstractions` — add `RouteParamAttribute : Attribute, IFromRouteMetadata` and
      `QueryParamAttribute : Attribute, IFromQueryMetadata`, both with an optional
      `Name`. Match **by interface** in the generator, so MVC's own attributes work
      identically for free. (R2.3)
- [ ] Runtime — add the five conversion helpers with distinct names (`TryString`,
      `TryEnum<T>`, `TryEnumNullable<T>`, `TryParsable<T>`, `TryParsableNullable<T>`). A
      single overload set is **CS0111**. `Enum.IsDefined` in both enum branches;
      `InvariantCulture` throughout. (R2.5, R2.6, R2.7)
- [ ] Generator — walk the **endpoint type's** properties from the symbol, not the
      triggering syntax node, so a partial split is consistent. Classify each property's
      type in the R2.5 order; emit MPEP013 for anything unclassifiable rather than a
      fallback. Optionality is read from the nullable annotation, and "assign only if
      present" from a `PropertyDeclarationSyntax.Initializer` — the latter is not on the
      symbol, so this pass reads syntax as well.
- [ ] Emit the binder into a generated partial. The three current gates (`Level != Raw &&
      IsPartial && !HasExistingBaseClass`) must widen — a raw endpoint with bound
      properties needs a partial where today none is emitted. (R2.13)
- [ ] Raw endpoints reach the binder through a new `IParameterBinder` interface,
      implemented **explicitly** so it stays off the consumer's public surface;
      `EndpointBase<TRequest>` implements it and forwards to `OnBindFailedAsync` so both
      rungs share one customisation point. (R2.11, R2.13)
- [ ] `Map<TEndpoint>` calls the binder immediately after `factory(...)` and before
      `HandleAsync`, so a malformed route value is rejected without the body being read.
- [ ] **Leave `BodyEndpoint<TRequest>` alone.** The body keeps going through MVC's input
      formatters with the JSON fallback. No overlay, no merge, no `JsonObject` — route
      values never enter the body type, so content negotiation survives everywhere. (R2.9)
- [ ] Reshape the interface ladder: `IGetEndpoint<TResponse>` and
      `IDeleteEndpoint<TResponse>` mean *response*; `IPostEndpoint<TBody>` and
      `IPostEndpoint<TBody, TResponse>` mean *body*. No `NoRequest` placeholder type is
      introduced. (R2.14)
- [ ] Read route values via `TryGetValue` then `as string`, collapsing all three no-value
      shapes. (R2.8)
- [ ] Detect a hand-written `BindRequestAsync` and suppress the generated one, or it is
      CS0111 pointing into generated code. Emit MPEP017 if both are genuinely present.
- [ ] Make `IEndpointBase.Path` `static virtual => "/"`. (Appendix B)
- [ ] Fix P10: `UpdateUserBody` loses its `Id` member entirely — the route owns it. P10 is
      closed by construction, not by a binder change.
- [ ] Rewrite the TestApp endpoints and `FixtureSources.Corpus` to the new shape,
      including a raw endpoint carrying bound properties and a GET using the new
      `IGetEndpoint<TResponse>` rung.

**Gate — verified by building, by reading generated output, and by a scratch app:**

- [ ] A GET endpoint with one route parameter is **≤ 8 consumer lines** and declares no
      `BindRequestAsync`.
- [ ] `/users/abc` → 400 naming parameter, value and expected type. Not 500, not 404.
- [ ] `/items/kind/7` → 400, not `200 {"kind":7}`.
- [ ] `PUT /users/abc` with a **valid** body → 400 on the route value, and the body is
      never read. Assert the ordering, not just the status.
- [ ] A body endpoint **with** a route parameter still reaches a registered XML input
      formatter — this is the property the rejected flat design could not hold.
- [ ] A raw `IEndpoint` with `[RouteParam]` binds; a raw one **without** still needs no
      `partial` and still gets no generated partial.
- [ ] No shipped or fixture signature contains a placeholder request type.
- [ ] A generator test asserts `factory(...)` appears **inside** the request delegate.
      (R2.12 — this is the cross-request-leak guard, and it is cheap to assert and
      catastrophic to get wrong.)
- [ ] The reflection pin test asserting `BindRequestAsync` is still abstract either passes
      or is **deliberately** rewritten with a comment saying why — one deleted keyword from
      silently breaking.

---

## M5 — OpenAPI (P4; R4.1–R4.6)

Shape decided by S1.

- [ ] Runtime helper that, per endpoint, calls `.Accepts<TRequest>()` on body verbs,
      `.Produces<TResponse>(SuccessStatusCode)`, `.ProducesProblem(400)` and
      `.ProducesProblem(415)` for typed endpoints, and `.WithName(const)`.
- [ ] Put it in the **shared** helper so the generated path and `MapEndpoint<T>` both get
      it — they duplicate each other's shape today.
- [ ] Emit the shadow `[AsParameters]` parameter per S1's answer. Members **nullable** — a
      non-nullable member makes the framework 400 on a *missing* value before the endpoint
      runs. Leave `Name` unset unless the consumer supplied one, or Swagger UI fails to
      match `Id:path` against `{id}`.
- [ ] Back-fill path parameters via `AddOpenApiOperationTransformer`, **not** `.WithOpenApi`.
      (R4.4)
- [ ] Fix P4.1: level-2 endpoints get a declared status code. (R4.5)
- [ ] Per-TFM transformer code for `Microsoft.OpenApi` 2.x/3.x. (R4.6)

**Gate — verified by dumping `/openapi/v1.json` from the running TestApp:**

- [ ] Every `{token}` has a `required: true` path parameter.
- [ ] Every body verb has a `requestBody`.
- [ ] Every typed endpoint documents 400 and 415.
- [ ] `DELETE /api/users/{id}` documents **204**.
- [ ] The document validates against `spectral:oas` with no path-templating errors.
- [ ] `/users/abc` still returns the library's 400 message (this is the S1 property; if S1
      fell back, record the schema degradation here instead).

---

## M6 — Diagnostics (P5; R3.3, R3.4, R3.6)

The differentiator. Additive — this is the designated cut if the PR overruns.

- [ ] MPEP007 duplicate verb+route, **Warning**. Compare segment-wise: equal literal, or
      both parameters with identical constraint text; intersect verb sets. Normalise
      case, collapse `//`, trim trailing `/`. **Do not diagnose shadowing** — `/users/{id}`
      vs `/users/me` is correct behaviour.
- [ ] MPEP008 route parameter with no bindable member; MPEP009 bound member not in the
      template. Both **Error**, both opportunistic — silent when the route is unreadable.
- [ ] MPEP010 `Path` already begins with its group prefix, **Warning**. Falls out of M2 for
      almost free and catches a silent 404.
- [ ] MPEP011 non-constant `Path`, **Info**.
- [ ] MPEP012 duplicate endpoint name, **Error**. `.WithName()` duplicates throw on the
      **first request**, not at startup — only the generator sees every endpoint at once.
- [ ] MPEP014 bound members on a non-`partial` type; MPEP016 unjoined group, **Info** (the
      set difference already exists in `EndpointMappingPlan.From`).
- [ ] **Every aborting diagnostic emits a throwing stub** so the consumer does not read a
      cascading CS0534 instead of the real message. (R3.4)
- [ ] Update the README diagnostics table in lockstep.

**Gate:**

- [ ] A negative-fixture project produces each diagnostic with the intended message and
      location, and **no** cascading CS-errors above it.
- [ ] `/users/{id}` + `/users/me` produces **nothing**.
- [ ] `["GET","HEAD"]` vs `["HEAD","OPTIONS"]` produces MPEP007.
- [ ] An endpoint whose `Path` is `$"…"` produces MPEP011 and no false MPEP008.

---

## M7 — Validation (P9; R5.1–R5.4)

Scope set by S2. If S2 failed, this milestone is MPEP015 and a README paragraph.

- [ ] `EndpointBase<TRequest>` — after a successful bind, resolve
      `IOptions<ValidationOptions>`, `TryGetValidatableTypeInfo`, run, map failures to 400.
      Behind a null check so an app that never called `AddValidation()` is unaffected.
- [ ] MPEP015 for a request type with DataAnnotations but no `[ValidatableType]`.
- [ ] **Never emit `[ValidatableType]`** — .NET 11 ships ASP0037 forbidding exactly that.

**Gate:**

- [ ] `[ValidatableType]` + `[Range(1,…)]` on a **body** type → 400 with the field key on
      out-of-range input.
- [ ] An app without `AddValidation()` behaves exactly as before.
- [ ] MPEP015 fires on the silent-no-op shape.
- [ ] The README states that `[Range]` on a `[RouteParam]` endpoint property is **not**
      evaluated, and says where those rules belong instead. (R5.1a — a consumer who
      decorates a route property and sees nothing happen is the exact failure this
      milestone exists to prevent.)

---

## M8 — Typed links (R7.1–R7.3)

- [ ] Emit nested static `Routes` classes mirroring the group tree, one method per
      endpoint, parameters typed from the request members by case-insensitive name match.
- [ ] Return an `EndpointRoute` object carrying name + `RouteValueDictionary`, with
      `Path(LinkGenerator)`, `Uri(…)` and an implicit `string` conversion — not a bare
      `string`, which would close off `CreatedAtRoute` and URI generation.
- [ ] **Delegate all construction to `LinkGenerator`.** Do not hand-roll substitution: a
      prototype matched byte-for-byte on 12 cases then diverged on 7 of 16 once `@ ! $ ( )
      * , ;` were included. (R7.2)
- [ ] Emit `{Endpoint}Template` consts, and replace the hardcoded route strings in
      `EndpointGroupingTests.cs:326-331`.
- [ ] Replace the magic strings in the TestApp and the three README occurrences.

**Gate:**

- [ ] `Routes.Api.Users.GetUser(id: 42)` returns `/api/users/42`; a wrong argument type is
      CS1503; a missing argument is CS7036.
- [ ] `GetUser(id: "a/b")` → `/api/users/a%2Fb` and `GetUser(id: "a@b")` → `/api/users/a@b`,
      matching `LinkGenerator` rather than `Uri.EscapeDataString`.
- [ ] An endpoint with a non-constant `Path` is **skipped** with MPEP011, not emitted wrong.

---

## M9 — Cross-assembly contract and typed client (R7.4, R7.6)

Scope set by S6.

- [ ] Emit `[assembly: EndpointContract(typeof(T), route, methods, RequestType=…,
      ResponseType=…, SuccessStatusCode=…)]` into the server's generated file. Assembly
      attributes must precede other members in their file — trivially true in generated
      code.
- [ ] A downstream generator reads them via `IAssemblySymbol.GetAttributes()` and emits a
      typed client. Memoize on `Compilation.GetMetadataReference(assemblySymbol)`, **not**
      on `IAssemblySymbol` — workspaces reuse the former across edits and GC the latter
      unpredictably (dotnet/roslyn#57997: `ReferencedAssemblySymbols` returned zero under
      the IDE's analysis service while working in `dotnet build`, closed as not planned).
- [ ] **Do not build a JSON manifest.** Microsoft deprecated that tooling in .NET 10
      Preview 7.
- [ ] **Do not claim AOT-safe binding anywhere.** RDG emitted nothing for ~60
      generator-emitted `MapMethods` calls and intercepted a hand-written one immediately;
      generators cannot see each other's output. (R7.6)

**Gate:**

- [ ] A client project with no source access to the server's endpoints compiles a working
      typed client.
- [ ] Removing an endpoint from the server breaks the **client** build.
- [ ] The client project's forced references are recorded, per S6.

---

## M10 — Contract snapshot and CI gate (R7.5)

- [ ] `Microsoft.Extensions.ApiDescription.Server` +
      `<OpenApiGenerateDocumentsOnBuild>true</OpenApiGenerateDocumentsOnBuild>`; commit the
      document. **Pin `OpenApiVersion` explicitly** — the default moved 3.0 → 3.1 → 3.2
      across three releases and an unpinned snapshot churns on every SDK upgrade.
- [ ] Guard `Program.cs` against the build-time host with
      `Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider"` — document
      generation **runs your `Program.cs`**.
- [ ] CI step: regenerate, fail if the tree is dirty.
- [ ] CI step: `oasdiff breaking base.json new.json --fail-on ERR`. (Optic was archived in
      January 2026; do not build on it.)

**Gate:**

- [ ] Changing a route without committing the document fails CI.
- [ ] Removing a path fails `oasdiff` with an ERR.

---

## M11 — MPEP001 code fix (R3.3 adjacent)

Scope and packaging shape set by S5.

- [ ] New `MintPlayer.AspNetCore.Endpoints.Generator.CodeFixes` project referencing
      `Microsoft.CodeAnalysis.Workspaces.Common`, pinned to the same Roslyn as the
      generator.
- [ ] `CodeFixProvider` for MPEP001: "Make 'X' partial". This is the highest-value fix
      because the diagnostic is already correct and correctly located — only the repair is
      manual, and today it is buried under three cascading CS errors.
- [ ] Extend the pack target with `<Error>` guards for the second assembly.
- [ ] Add `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing.XUnit`; the existing harness drives
      `CSharpGeneratorDriver` only.

**Gate:**

- [ ] The fix applies in an IDE and produces a compiling document.
- [ ] Both assemblies are present in the unpacked nupkg, Debug and Release identical.
- [ ] A plain `dotnet build` still runs the generator with the code-fix assembly present.

---

## M12 — Documentation (R8.1–R8.3)

- [ ] Rewrite the 646-line package README: `IMemberOf<T>` in ~20 places, five
      `BindRequestAsync` code blocks, the diagnostics table, the binding-failure table.
- [ ] State the binding rule **once**, in one sentence.
- [ ] New section: `[Authorize]`, `[Authorize(Policy=…)]`, `[EnableRateLimiting]`,
      `[Tags]` and `[ProducesResponseType]` already work as class attributes — verified
      end to end, and entirely undocumented today. (R8.2)
- [ ] Document what the library does **not** do: no automatic `AddValidation()` discovery,
      no AOT-safe generated binding, and why.
- [ ] Add a README for `.Abstractions` and `.Generator`, which have none.

**Gate:**

- [ ] Every code block in the README compiles when pasted into a scratch project.
- [ ] No occurrence of `IMemberOf` remains anywhere outside this plan and the PRD.

---

## M13 — The single test sweep (goals 1–6; acceptance 1–12)

- [ ] Extend the generator suite: route recovery, attribute membership + parity matrix,
      binder emission, every new diagnostic, incrementality.
- [ ] Extend the runtime suite: conversion helpers, the JSON overlay, the three no-value
      shapes, raw-endpoint binding, validation invocation.
- [ ] Extend `TestAppEndToEndTests` with the new endpoint shapes.
- [ ] `dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"` on
      **both** TFMs.

**Gate:**

- [ ] All twelve acceptance criteria demonstrated, each with the command that shows it.
- [ ] Test count is **identical per TFM** — a mismatch means a test silently stopped being
      discovered when the TFM changed.
- [ ] Coverage has not regressed against the recorded baseline.
- [ ] `dotnet build -t:Rebuild`, deduplicated, is warning-clean apart from the documented
      pre-existing baseline.

---

## M14 — Release gate **[decision]**

- [ ] Ship as `12.0.0` — this is a breaking change to a package that is already public.
- [ ] Or hold the merge until the .NET 11 GA SDK and ship then.

Merging to `master` **publishes to nuget.org**. `IMemberOf<TGroup>` is deleted and
`BindRequestAsync` is no longer hand-written, so every existing consumer's code stops
compiling. Either choice is defensible. What is not defensible is discovering which one
happened by looking at nuget.org afterwards.

---

## M1–M14 outcome

*(Written after the work. A `| Measure | Before (master) | After |` table covering consumer
lines over the seven-scenario corpus, concept count, diagnostic count, OpenAPI validity,
warning count and test count. Corrections to the PRD are appended there as blockquotes.)*

---

## Deliberately out of scope

Genuinely not being done — not a parking lot for deferred work.

- **Route-shadowing diagnostics.** Measured as correct routing behaviour; a diagnostic
  would be a pure false positive on every codebase that has a `/users/me`.
- **Inferring the route from the request type, or the verb from the class name.**
  Prototyped; the first invents wrong routes that nothing can catch and the second saves
  zero lines. PRD Appendix B.
- **Dropping the request type so handler parameters are the request.** Six lines cheaper
  over seven endpoints, paid for with the compiler-enforced handler contract, anywhere to
  hang `[Required]`, and anything to name in `.Accepts<T>()`.
- **`MapMethods(…, RequestDelegate)` for the last two AOT warnings.** Correct, warning-free,
  and it breaks `Configure(RouteHandlerBuilder)` — the library's most-used public hook. A
  v13 conversation, and only if someone asks for AOT.
- **`[FromClaim]`.** Couples binding to authentication and needs `IsRequired`, name
  mapping, JSON deserialization and a validation-failure path invented from scratch. Ship
  `[RouteParam]`/`[QueryParam]`; add `[HeaderParam]` when asked.
- **A `Directory.Build.props`.** Rejected in the previous plan for a still-valid reason:
  it would break the `DeterministicReport=false` coupling documented in
  `coverlet.runsettings`.
- **Competing with `Microsoft.Extensions.Validation`.** It is the right substrate. The
  library's contribution is making its silent no-op visible, not replacing it.
