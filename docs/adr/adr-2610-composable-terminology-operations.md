# ADR 2610: Composing FHIR Terminology Operations Internally

## Status

Proposed

## Context

FHIR terminology operations have semantic dependencies. For example, ValueSet validation may need to check set membership and resolve a recommended display; ValueSet expansion may need to resolve multiple includes and exclusions; and ConceptMap translation may need to consider multiple codings or maps.

The FHIR operation definitions specify observable inputs and outputs, but they do not require an implementation to call another FHIR endpoint over HTTP. Treating an internal sub-operation as a loopback HTTP request adds serialization, routing, authentication and tenant-context behavior, network failure modes, and recursion risk. Conversely, duplicating terminology semantics independently in every operation can produce inconsistent membership, display, version, and unavailable-result behavior.

Ignixa has an `ITerminologyService` contract with lookup, validation, expansion, translation, and subsumption methods. `ValidateBindingAsync` already combines membership validation with lookup when display correction is needed. The public experimental API currently exposes only a subset of this contract. The investigation [Composing FHIR Terminology Operations](../features/terminology-services/investigations/composable-operations.md) identifies additional opportunities and unresolved semantics.

## Decision

Implement composed terminology behavior through internal service capabilities, not by calling Ignixa's public HTTP endpoints from its own operation handlers.

- Keep FHIR HTTP and `Parameters` parsing/serialization in the API layer.
- Keep operation-level workflows in application handlers and reuse existing terminology-service capabilities where their semantics match.
- Keep local query efficiency and provider-specific behavior in the DataLayer. A composed operation may be optimized into targeted or batched queries rather than issuing one request for every logical sub-operation.
- Define an explicit, acyclic dependency map for any operation that composes other capabilities. Add a shared lower-level helper only when existing method contracts cannot express the required semantics; do not introduce a generic configurable operation-graph engine preemptively.
- Use remote FHIR terminology calls only through an explicitly configured remote provider/delegation boundary. Do not treat local endpoint loopback as the remote-provider mechanism.
- Preserve the distinction between definitive negative results and unsupported, unresolved, incomplete, or unavailable terminology. Propagate cancellation, version parameters, request budgets, and diagnostics through all sub-operations.

An evidenced composition candidate is ValueSet `$validate-code` for complex hierarchy filters: Firely Server documents using CodeSystem `$validate-code`, `$subsumes`, and `$lookup` against CAR-backed systems and aggregating the results, with fallback to configured terminology services when CAR cannot handle a request. Ignixa should investigate this pattern while preserving operation semantics and provider boundaries. Conditional display lookup, expansion over referenced ValueSets or external code systems, and later CodeableConcept translation are additional candidates. R4 also defines preliminary CodeSystem `$find-matches`, which needs a matching strategy and version-scope decision. Full ValueSet expansion is not the default implementation for single-code validation. `$closure` requires its own incremental state and performance design; it must not be implemented as repeated HTTP `$subsumes` calls.

## Consequences

### Positive

- Internal composition avoids endpoint serialization, loopback networking, and duplicate HTTP middleware execution.
- Reusable membership and lookup semantics reduce divergence between public operations and resource validation.
- DataLayer implementations can batch or optimize sub-operation work while the API contract remains FHIR-shaped.
- Explicit errors and unavailable outcomes prevent incomplete terminology data from being reported as a definitive answer. For FHIR R4 `$expand`, failure to correctly resolve referenced code systems requires an error; `valueset-unclosed` is reserved for a genuinely unbounded expansion, not an unresolved include.

### Negative

- The internal dependency graph and failure behavior require documentation and tests as capabilities are added.
- Local and remote providers may support different operations and parameters, so selection and delegation policies need explicit capability checks.
- A composed operation can amplify latency or backend load if it fans out per code or expands unnecessarily; budgets, batching, and measurement are required.
- The existing `ITerminologyService` has mixed completeness across implementations, and the current public endpoints do not expose the full service contract.

### Deferred

- A generic operation-graph engine, fan-out merge framework, and arbitrary provider choreography.
- Operation-per-code `$lookup` during expansion.
- `$closure` implementation and persistence.
- Vendor-specific batch validation as a core FHIR requirement.
- Attribution of Firely Server's documented CAR-backed composition pattern to the specific Firely DevDays 2026 presentation; the recap does not identify that call chain.

## References

- [FHIR R4 terminology operation contracts](https://hl7.org/fhir/R4/terminology-service.html)
- [Investigation: Composing FHIR Terminology Operations](../features/terminology-services/investigations/composable-operations.md)
- [Firely Server terminology services](https://docs.fire.ly/projects/Firely-Server/en/latest/features_terminology/terminology.html)
- [Firely DevDays 2026 recap](https://fire.ly/blog/devdays-2026-recap-fhir/)
