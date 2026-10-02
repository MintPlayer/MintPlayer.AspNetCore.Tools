# PRD: Endpoint type metadata — "was endpoint X mapped?" without route strings

Issue: [#38 — record the endpoint type as metadata so "is endpoint X mapped?" needs no route strings](https://github.com/MintPlayer/MintPlayer.AspNetCore.Tools/issues/38).

*(Draft 1, written from an investigation of the runtime and generator mapping paths on 2026-10-02; not yet implemented.)*

**Affected version:** MintPlayer.AspNetCore.Endpoints 11.2.0-rc.0 (runtime, Abstractions, Generator)
**Proposed version:** 11.3.0-rc.0 (new public API)

## Overview

Record the endpoint **class** on every endpoint this library maps, and offer one helper that answers whether a given endpoint class — or any closing of an open generic one — is mapped in the running application.

## Problem statement

An endpoint mapped by this library carries its own attributes as metadata (`EndpointAttributes.ForMetadata`), but not its type. So "is `ListExternalLogins<TUser>` mapped?" can only be answered by matching route strings.

The motivating consumer is MintPlayer.Spark's `GET /spark/auth/capabilities`. It tells the SPA which account pages the server actually serves (connected logins, passkeys, …), so the SPA never links to a page that 404s. Today it collects every `RoutePattern.RawText` and matches literals such as `"/spark/auth/external-logins"` and `"/spark/auth/passkeys/sign-in"` (`GetAuthCapabilities.cs`):

- A renamed route or group prefix silently makes a flag wrong. Nothing fails to compile.
- The check is in a different file from the mapping, so the two drift.
- The generated `Endpoints` descriptor list cannot replace it: it lists **declared** endpoints, including those in disabled groups (`IEndpointGroup.IsEnabled`), not mapped ones.

## Goals

- G1. Every endpoint mapped by this library — the generated `Map{Assembly}Endpoints()` and the manual `MapEndpoint<T>()` — carries its closed endpoint type in its metadata, exactly once.
- G2. A public, trim-safe helper answers "is this endpoint type mapped?", including open generic definitions (`typeof(ListExternalLogins<>)`).
- G3. Renaming or deleting an endpoint class breaks consumers at compile time instead of silently changing an answer.

## Non-goals

- Describing endpoints that are declared but not mapped (that is the existing `Endpoints` descriptor list).
- A capability or feature-flag system. Consumers decide what a mapped endpoint *means*.
- Endpoints mapped outside this library (plain `MapGet`, `MapIdentityApi`). They carry no endpoint class.

## Requirements

- **R1. `EndpointTypeMetadata`.** A public sealed class in `MintPlayer.AspNetCore.Endpoints` with `Type EndpointType`. It holds the **closed** type: `typeof(TEndpoint)` on both paths, which can never be open. That includes endpoints closed by an application through `[assembly: EndpointTypeArgument<,>]`.
- **R2. Both paths, once.** `MapEndpoint<TEndpoint>()` (`EndpointRouteBuilderExtensions.cs`, next to the `ForMetadata` call) and the generated `Map<TEndpoint>` helper (`EndpointGenerator.Producer.cs`, `EmitMapBody`, next to the emitted `ForMetadata` call) each add exactly one `EndpointTypeMetadata`. Group conventions add none (they add no per-type metadata today).
- **R3. `IsEndpointMapped`.**
  - **Signature:** `public static bool IsEndpointMapped(this EndpointDataSource dataSource, Type endpointType)`, plus `IsEndpointMapped<TEndpoint>(this EndpointDataSource)`.
  - **Closed type:** a closed `endpointType` matches by equality.
  - **Generic definition:** a `endpointType` that is a generic type definition matches any mapped type whose type — **or a base type** — is a closing of it. So a derived closing such as `EchoString : Echo<string>` counts as `Echo<>` (see D2).
  - **No metadata:** endpoints without `EndpointTypeMetadata` never match.
- **R4. Disabled groups.** Endpoints in a disabled group are not mapped (both paths already skip them), so `IsEndpointMapped` is `false` for them. Tested explicitly.
- **R5. AOT/trimming.** Both runtime projects keep `IsAotCompatible` with zero warnings. The helper is not `[RequiresUnreferencedCode]`: `IsGenericType`, `GetGenericTypeDefinition` and `BaseType` need no `[DynamicallyAccessedMembers]`.
- **R6. Documentation.** The README gets a section near "Attributes, `Configure` and endpoint metadata" and "Generic endpoints", stating:
  - when the answer is complete: at request time, after mapping, not during startup;
  - where to read it: the DI `EndpointDataSource` is the composite over all route builders;
  - that a new object appears in `endpoint.Metadata`, with the justification for it.

## Acceptance criteria

- **AC1 (parity).** A test maps the same endpoint through the generated path and the manual path, and asserts that each endpoint carries exactly one `EndpointTypeMetadata` with the closed type. Mirror `GroupMembershipTests.MembershipAttribute_IsNotEndpointMetadata_OnEitherPath`.
- **AC2.** `IsEndpointMapped` cases, each asserted against the `EndpointDataSource` collected from a real host:
  - true for a mapped closed type;
  - false for an unmapped type;
  - true for `typeof(Echo<>)` when `Echo<string>` is mapped;
  - true for `typeof(Echo<>)` when only `EchoString : Echo<string>` is mapped;
  - false for an endpoint in a disabled group.
- **AC3 (end to end).** TestLibrary's open endpoint, closed by TestApp, is reported by `IsEndpointMapped(typeof(Open<>))` via `WebApplicationFactory` (extend `TestLibraryEndToEndTests`).
- **AC4.** Generator text tests assert that the emitted `WithMetadata(…EndpointTypeMetadata(typeof(TEndpoint)))` line is present exactly once in the `Map` helper.
- **AC5.** The OpenAPI contract snapshot (TestApp) is unchanged. The metadata is not an `IProducesResponseTypeMetadata`, `IAcceptsMetadata`, or anything else OpenAPI reads.

## Decisions

- **D1. Explicit metadata in both mapping sites, not inside `EndpointAttributes.ForMetadata`.**
  - Putting it in `ForMetadata` would reach libraries already compiled with the 11.2 generator without a rebuild. But it changes that method's documented contract ("the attributes of `endpointType`") and its existing tests.
  - Consumers must bump the package to call `IsEndpointMapped` anyway, so they rebuild either way.
  - Generator and runtime ship in one package (`PackEndpointsGenerator`), so the two sites cannot drift.
- **D2. A base-type walk for generic definitions.** A derived non-generic closing is a mapping of the generic endpoint in every sense a consumer cares about. Without the walk, `IsEndpointMapped(typeof(Echo<>))` would be false for `EchoString`.
- **D3. An extension on `EndpointDataSource`.** That is the DI composite of everything mapped, and what consumers already resolve. Tests without a host can build one from `IEndpointRouteBuilder.DataSources`.

## Consumer follow-up (MintPlayer.Spark, after this is published)

- Bump `MintPlayer.AspNetCore.Endpoints` (8 libs reference 11.2.0-rc.0).
- Replace the route-string matching in `GetAuthCapabilities`:
  - `passkeys = IsEndpointMapped(typeof(PasskeySignIn<>))`
  - `externalLogins = IsEndpointMapped(typeof(ListExternalLogins<>))`
- `localCredentials` keeps its current derivation. `/login` and `/register` come from `MapIdentityApi` and Spark's own lambda routes, which carry no endpoint class.
