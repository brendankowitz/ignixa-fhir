# Feature: Application Layer Modernization

**Status**: Exploring
**Created**: 2026-07-11

## Problem Statement

`src/Application` (Ignixa.Api, Ignixa.Api.OpenIddict, Ignixa.Application, Ignixa.Application.BackgroundOperations,
Ignixa.Application.Operations, Ignixa.Conformance.Events, Ignixa.Domain, Ignixa.Sidecar.Contracts, Ignixa.Web —
~743 source files) was largely built by early-generation coding agents over the project's first 6 months. It has
never had a holistic architectural/technical review. Before continuing to build on it, we want a clear picture of:

- Where it drifts from the layering rules in `docs/adr/adr-2509-vertical-slice-architecture.md` and root `CLAUDE.md`
- Tech debt, dead code, over/under-engineering, and inconsistent patterns typical of early-agent output
- Correctness and maintainability risks (error handling, async/cancellation, nullability, testability)
- A prioritized, phased roadmap for remediation

## Constraints

- Must respect existing layer dependency direction: API → Application → Domain, DataLayer implements Domain
- No `Hl7.Fhir.*` in Application/DataLayer (use `Ignixa.*` abstractions)
- Minimal API only (no MVC controllers)
- Nullable reference types enabled; warnings-as-errors
- Changes must not require a big-bang rewrite — the server must keep running (F5 zero-dependency experience per ADR 2509)

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [api-http-layer-review](investigations/api-http-layer-review.md) | Complete | Ignixa.Api/.OpenIddict/.Web — 3 P0 (authz filter masks errors as 500, systemic authz/audit bypass on several endpoint families, OpenIddict dev-auth open in prod), 14 P1, God-file `FhirEndpoints.cs` |
| [domain-conformance-events-review](investigations/domain-conformance-events-review.md) | Complete | Ignixa.Domain/.Conformance.Events — 1 P0 (wrong HTTP status on not-found), ~20% of Domain is dead code, ADR-2509 "no dependencies" claim is false |
| [application-core-infra-review](investigations/application-core-infra-review.md) | Complete | Application Events/Infrastructure/Utilities + Authorization/Admin/Conformance/Metadata/Specification/Packages — 4 P0 (fail-open authz, unsynchronized ConformanceState race, R6 metadata crash, dead OIDC discovery), extensive dead "sophisticated infrastructure" |
| [crud-vertical-slices-review](investigations/crud-vertical-slices-review.md) | Complete | Resource/Bundle/Patch/ConditionalOperations/History/Search/Compartment/Export — 8 P0 (validation never runs, If-Match unenforced, bundles not atomic, PATCH data-integrity bugs, parser PHI leak) |
| [experimental-feature-review](investigations/experimental-feature-review.md) | Complete | Features/Experimental (103 files: GraphQL/MCP/IPS/Transform/Terminology) — legitimate staging mechanism, not a dumping ground, but 4 P0 (MCP auth dead wiring, IPS strategy handoff broken, patch value-type bug, dead duplicate files) |
| [background-operations-review](investigations/background-operations-review.md) | Complete | BackgroundOperations/Operations/Sidecar.Contracts — 5 P0 (failed jobs reported Completed, export paths don't match storage, non-deterministic orchestration I/O, silent partial export, zero retry policy) |

## Decision

*No ADR yet.* Findings are synthesized into [`roadmap.md`](roadmap.md): a phased plan (0. security/data-integrity
emergency, 1. reliability/observability, 2. layering/dead-code cleanup, 3. mechanical consistency sweep, 4. feature-fate
decisions). Per the Transformer Mandate, this is a set of ranked options — prioritization, scope cut-line, and
acceptance are a human call, not committed here.
