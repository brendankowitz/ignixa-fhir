# ADR-2610: Reliable Package Lifecycle

**Status**: Proposed
**Date**: 2026-10-20
**Feature**: package-management

## Context

Ignixa already provides strict package extraction, verified exact-version NPM acquisition,
and staged event-sourced conformance activation. However, the server's legacy load path
imports resources in independently committed batches and treats any active resource as
proof that the package is installed. An interrupted import can therefore be mistaken
for a completed installation. Dependency traversal imports while resolving, keeps the
first encountered version, and tolerates missing dependencies; HTTP responses omit
dependency completeness and activation outcomes.

This ADR describes the [`9db0620`](https://github.com/brendankowitz/ignixa-fhir/tree/9db0620d5909e1a0ffd11917a7fa1d78d3299c2b)
baseline; slice 1 (typed closure outcomes, `IsComplete`, HTTP/preload closure reporting, and MCP activation reporting) has since
been implemented, while durable installation remains unimplemented.

The [reliable-package-lifecycle investigation](investigations/reliable-package-lifecycle.md)
compares current source with dotnet-fhir-packages and distinguishes reusable ideas from
capabilities Ignixa already has. Package bytes and resources are currently shared content,
despite tenant-labelled management calls. We must preserve explicit source trust,
multi-version FHIR behavior, chunked storage writes, and the activation concurrency
boundary described by [ADR-2512](../../adr/adr-2512-event-sourced-conformance.md), which
also remains Proposed. This ADR proposes future behavior; it does not claim implementation.

## Options Considered

1. **Patch the legacy loader only** - Return warnings and retry failed imports without
   introducing package-level completion. *(Rejected as the full solution: resource
   existence and process-local coordination cannot prove durable installation.)*
2. **Adopt FhirPkg as the package-management implementation** - Reuse its graph, registry,
   and filesystem cache functionality. *(Not selected: Ignixa would still need its own
   database visibility, source-policy integration, and conformance lifecycle.)*
3. **Evolve Ignixa with explicit planning, installation, and activation boundaries** -
   Reuse existing acquisition and activation primitives while adding durable installation
   completion and inspectable dependency plans. *(Selected proposal.)*

## Decision

Propose an incremental, Ignixa-owned lifecycle: resolve and validate an exact dependency
plan, acquire artifacts through approved protocol-specific adapters, install resources
behind a durable completion boundary, then activate the compatible selection through
the existing conformance pipeline. Persist manifests, dependency edges, source identity,
available verified digests, and installation state separately from activation/reindex
state. Incomplete installations must be excluded from ordinary resource resolution and
downstream processing; completion permits inspection/staging, while runtime consumers
use only the explicitly active selection and its versioned or invalidated caches.
Coordinate the shared package name/version across sources, rejecting differing bytes
before resource publication; concurrent owners must not complete stale attempts.
Shared content remains shared, and any tenant-specific activation policy must be explicit.
Core owns generic artifact/graph primitives, Application owns policy and orchestration,
and Domain/DataLayer own persistence contracts and durable coordination.

Retain every required exact-version node and report conflicts instead of pruning them
by traversal order. The new planned workflow rejects unresolved or incompatible closures
and cycles by default, initially supports exact dependencies, and validates FHIR
declarations and in-process core compatibility without implicit conversion. Preserve legacy best-effort
semantics only through an explicit compatibility mode with incomplete outcomes. Keep
installation distinct from activation, report per-package failures and pending reindex
across SDK/HTTP/MCP/preload/CLI surfaces, and preserve caller cancellation. Reuse verified
acquisition without weakening its trust rules or assuming legacy feeds speak standard
NPM. A persisted plan is not an offline restore guarantee or a portable lockfile.

Existing unload/uninstall calls participate in the same coordination boundary and
deactivate content without erasing installation evidence. Protect active dependants,
revalidate prerequisites at activation publication, and authorize shared effects
explicitly; physical removal remains separate.

## Consequences

- Partial imports no longer qualify as installed; chunked writes remain possible without
  a network-spanning or dependency-closure-wide SQL transaction.
- Installation persistence, provider-wide visibility gating, durable ownership, and
  recovery require migration. Existing rows alone cannot prove completion or integrity.
- Installed content remains inspectable after activation rejection. Activation stays
  atomic per package, not across the closure; partial activation must be observable and
  failed prerequisites must block dependants. Rejected inactive content must not affect
  runtime resolution or replace the previous active selection.
- Strict planning and richer outcomes change client expectations and require a
  compatibility rollout, consistent version semantics, and updated published contracts.
- Existing acquisition/activation investments are retained; no new external SDK, queue,
  service, automatic public-registry fallback, or filesystem journal is mandated.
- Acceptance requires interrupted-import, cancellation, restart/concurrency, graph,
  release-compatibility, and boundary-result coverage identified in the investigation.
  Offline restore, federation, automatic updates/rollback, and tenant-specific activation
  remain separate decisions.
