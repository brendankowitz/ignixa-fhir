# Investigation: Models-Backed Client-Side Operations

**Feature**: client-sdk
**Status**: In Progress
**Created**: 2026-06-14

## Approach

Build a **layered, element-primary FHIR REST client** — `Ignixa.Client` — and make typed-model support a strictly additive layer on top of it.

The design mirrors the [typed-models](../typed-models/readme.md) decision exactly: the core is element/JSON-primary and version-agnostic; strong typing is an opt-in view, never a requirement.

**Layer 1 — element/JSON client (no typed-model dependency).** An `HttpClient`-backed `FhirClient` whose currency is `ResourceJsonNode` / `IElement`. It owns the REST plumbing every consumer currently hand-rolls:

```csharp
var client = new FhirClient(new Uri("https://server/fhir"));      // or DI: AddIgnixaFhirClient()

ResourceJsonNode created = await client.CreateAsync(patientNode, ct);
ResourceJsonNode read    = await client.ReadAsync("Patient", "123", ct);
ResourceJsonNode updated = await client.UpdateAsync(read, ifMatch: read.Meta.VersionId, ct);
await client.DeleteAsync("Patient", "123", ct);

await foreach (var entry in client.SearchAsync("Patient", new() { ["name"] = "chalmers" }, ct)) { ... }  // auto-pages Bundle.link
ResourceJsonNode outcome = await client.OperationAsync("Patient", "123", "$everything", parameters, ct);
ResourceJsonNode response = await client.TransactionAsync(bundleNode, ct);
```

This layer handles content negotiation (`application/fhir+json`), `Location`/`ETag`/`Last-Modified` extraction, `If-Match`/`If-None-Exist`, `OperationOutcome`-on-error (thrown as a typed `FhirOperationException` carrying the outcome), and `Bundle` paging. It references only `Ignixa.Serialization`/`Ignixa.Abstractions` — **no `Ignixa.Models.*`, no `Hl7.Fhir.*`**.

**Layer 2 — typed overloads (additive sugar).** Generic overloads that return a typed facade by calling the zero-copy `As<T>()` that already exists on `ResourceJsonNode`:

```csharp
R4.Patient p = await client.ReadAsync<R4.Patient>("123", ct);     // == (await ReadAsync("Patient","123")).As<R4.Patient>()
R5.Patient q = await client.ReadAsync<R5.Patient>("123", ct);     // same wire, different lens
await client.UpdateAsync(p, ct);                                   // p is a ResourceJsonNode; no special path
```

Because facades are views over the same node, the typed overload is the element call plus a pointer-reinterpret — no second serialization path, no conversion. These overloads can live in the `Ignixa.Client` package guarded so they're only useful when a consumer also references a typed-models package, **or** ship in a tiny `Ignixa.Client.Models` bridge package. Either way, a consumer who never references `Ignixa.Models.*` loses nothing but the generic sugar.

**Resource-type string** for the typed overload comes from the same `As<T>()`-derived registry the models use, not from reflection on the wire payload.

## Tradeoffs

| Pros | Cons |
|------|------|
| Typed models stay genuinely optional — the client is fully functional on `ResourceJsonNode`/`IElement` alone | Two API surfaces (element + typed overloads) to document and keep consistent |
| Kills the duplicated `HttpClient`/`StringContent`/parse boilerplate (E2E harness, samples, plugins) | A correct FHIR REST client is real work: paging, conditional headers, ETag concurrency, OperationOutcome mapping, ret/throw policy |
| Multi-version is free: one wire client, version chosen only at the `As<T>()` boundary; `R4.Patient` and `R5.Patient` over the same response | Search/operation parameter modelling (chaining, `_include`, modifiers) is a large surface to type well; MVP likely stays string/dictionary-based |
| No `Hl7.Fhir.*` dependency — honors [ADR-2510](../../adr/adr-2510-capability-sourcenode-model.md); the client never forces Firely on a consumer | Async streaming search (`IAsyncEnumerable` over pages) needs careful cancellation + error semantics |
| Reuses existing primitives: `ResourceJsonNode.Parse`/`As<T>()`, `MutableNode`, `Meta`, the `*JsonNode` facades | Not a drop-in for teams already standardized on Firely's `FhirClient` (see firely-fhirclient-wrapper candidate) |
| Natural first consumer already exists (E2E `SearchTestHarness`) — dogfood immediately | Auth/retry/telemetry policy (`HttpClient` handlers, SMART tokens) must integrate with existing [smart-on-fhir](../smart-on-fhir/readme.md) without bloating the core client |

## Alignment

- [ ] Follows architectural layering rules — client is a standalone opt-in package above Core; references `Ignixa.Serialization`/`Ignixa.Abstractions` only; typed sugar isolated so models stay optional.
- [ ] Developer Experience — works with one `new FhirClient(uri)` and `ResourceJsonNode`; typed models add IntelliSense when (and only when) referenced.
- [ ] Specification compliance — FHIR REST semantics (content negotiation, conditional ops, ETag/versioning, Bundle paging, `OperationOutcome` errors).
- [ ] Consistent with existing patterns — element-primary core + optional typed view, identical philosophy to typed-models; `As<T>()` is the shared seam.

## Evidence

### What exists today (the boilerplate this replaces)

- No FHIR client abstraction anywhere in `src/` (a grep for `FhirClient`/`RestClient`/`HttpClient`-based clients finds only `Ignixa.FhirFakes` population tooling).
- The server is exercised exclusively via **raw `HttpClient`**. `test/Ignixa.Api.E2ETests/_Infrastructure/Harness/SearchTestHarness.cs` hand-builds every call: `PostAsync`/`PutAsync`/`GetAsync` with `new StringContent(json, Encoding.UTF8, "application/fhir+json")`, then `ReadAsStringAsync` + manual parse. This is precisely the surface a Layer-1 client collapses, and it is the natural first internal consumer / dogfood target.

### The `As<T>()` seam makes typed overloads almost free

- `ResourceJsonNode.As<T>()` (`src/Core/Ignixa.Serialization/SourceNodes/ResourceJsonNode.cs`) already does zero-copy reinterpretation of the parsed `JsonObject` into a typed subclass, via `ResourceTypeRegistry` fast path + reflection fallback. A typed read is therefore `(await ReadAsync(...)).As<T>()` — no extra parse, no conversion. This is the same mechanism the [typed-models](../typed-models/investigations/source-generated-poco-facades.md) facades rely on, which is why the client and the models compose without coupling.
- The hand-written `*JsonNode` facades (`BundleJsonNode`, `OperationOutcomeJsonNode`) already model the two response types a client touches most — Bundle (search/transaction results, paging links) and OperationOutcome (errors). Layer 1 can use these directly without any `Ignixa.Models.*` dependency.

### Prior art — Firely `FhirClient`, and why we diverge

Firely's `FhirClient` is the reference for scope (CRUD, `Search`/`Continue` paging, `History`, `WholeSystem*`, `$operations`, `Transaction`, conditional create/update/delete, `PreferredFormat`, ETag concurrency via `If-Match`, `VersionAware` updates). We borrow the *operation surface and REST discipline*, not the model: Firely's client is POCO-primary and version-bound, so it can't return "the same response as both R4 and R5" and it drags `Hl7.Fhir.*` into the consumer. Our Layer-1 client returns version-agnostic nodes; the version lens is chosen by the caller at `As<T>()`. (A `firely-fhirclient-wrapper` remains a viable interop aid for teams already on Firely — tracked as a separate candidate.)

### Open questions for the ADR

- **Search/operation parameter modelling**: MVP string/dictionary params vs a typed/fluent search builder (and whether that builder is generated alongside the models — the `source-generated-typed-client` candidate).
- **Error policy**: throw `FhirOperationException(OperationOutcome)` vs return a `Result`-style outcome. Lean toward throwing at the boundary, consistent with the project's exception-at-boundaries stance.
- **Paging surface**: `IAsyncEnumerable<entry>` auto-paging vs explicit `Bundle` + `ContinueAsync(bundle)` (Firely-style). Possibly both.
- **Auth/retry/telemetry**: compose via `HttpClient` `DelegatingHandler`s and `IHttpClientFactory`; SMART token acquisition should plug in from [smart-on-fhir](../smart-on-fhir/readme.md), not be baked into the client.
- **Packaging of Layer 2**: typed overloads inside `Ignixa.Client` (guarded) vs a separate `Ignixa.Client.Models` bridge — the latter keeps the optional-models boundary unambiguous.

## Verdict

*Pending evaluation* — promising and well-aligned (it is the typed-models philosophy applied to the client surface, with `As<T>()` as the shared, dependency-free seam), but scope is non-trivial. Next step is a thin Layer-1 spike: `ReadAsync`/`CreateAsync`/`SearchAsync` (with paging) over `ResourceJsonNode`, dogfooded by porting one `SearchTestHarness` call, plus one typed overload to confirm `As<T>()` composition costs nothing. Weigh against `source-generated-typed-client` (more ergonomics, more generated surface, couples to models) before drafting an ADR.
