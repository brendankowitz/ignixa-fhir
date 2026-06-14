# Feature: Client-Side FHIR Operations

**Status**: Exploring
**Created**: 2026-06-14

## Problem Statement

The server has no first-class **client** story. Every consumer that talks FHIR over HTTP — the E2E suite, sample apps, plugin/extension authors, anyone scripting against an Ignixa server — hand-rolls the same plumbing: build an `HttpClient`, set `StringContent(json, "application/fhir+json")`, `ReadAsStringAsync`, parse the body, dig the `Location`/`ETag` out of the response, page through `Bundle.link[rel=next]` by hand. `test/Ignixa.Api.E2ETests/_Infrastructure/Harness/SearchTestHarness.cs` is exactly this boilerplate, written once and copied in spirit everywhere else.

Firely ships a rich client (`FhirClient`: typed CRUD, search, history, `$operations`, transaction/batch, conditional operations, paging, ETag/concurrency) — but it is POCO-primary and carries the `Hl7.Fhir.*` dependency [ADR-2510](../../adr/adr-2510-capability-sourcenode-model.md) keeps out of our layers, and it binds a process to one FHIR version.

**The question**: can we offer Firely-grade *client* ergonomics built on our own element/JSON runtime and (optionally) our [typed models](../typed-models/readme.md), without taking the Firely dependency and without making either the client or the models a load-bearing part of the core?

## Constraints

- **Typed models are an optional module, never required.** The client must be fully usable with only the element/JSON runtime (`ResourceJsonNode`/`IElement`). Typed models are a purely additive ergonomic layer on top — if a consumer doesn't reference `Ignixa.Models.*`, the client still does everything.
- **No `Hl7.Fhir.*` dependency** in the client or anything it forces onto consumers (ADR-2510).
- **Multi-version stays first-class** — the client is version-agnostic over the wire; version only enters when a consumer opts into a typed read (`As<R4.Patient>()`).
- **Opt-in package** — `Ignixa.Client` is a NuGet a consumer pulls in deliberately; the server does not depend on it.
- **Honor FHIR REST semantics** — content negotiation, `Location`/`ETag`/`Last-Modified`, `If-Match`/`If-None-Exist`, `OperationOutcome` on errors, Bundle paging.

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [models-backed-client-operations](investigations/models-backed-client-operations.md) | In Progress | A layered, element-primary REST client (`Ignixa.Client`) over `ResourceJsonNode`/`IElement`; typed-model overloads (`ReadAsync<R4.Patient>`) are additive sugar that just `As<T>()` the returned node. Client works with zero typed-model references. |

### Future investigation candidates

- **firely-fhirclient-wrapper** — thin adapter exposing our operations through Firely's `FhirClient` surface; fastest to familiarity, reintroduces the rejected dependency. Interop aid only.
- **source-generated-typed-client** — per-resource typed entry points (`client.Patient.ReadAsync(id)`, typed search builders) generated alongside the typed models; highest ergonomics, larger generated surface, couples client to the models package.
- **no-dedicated-client** — document the raw `HttpClient` + `.As<T>()` pattern and ship only small extension helpers; lowest cost, leaves the boilerplate problem mostly unsolved.

## Decision

*Pending — first investigation in progress.*
