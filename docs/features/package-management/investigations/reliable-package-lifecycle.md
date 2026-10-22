# Investigation: Reliable Package Lifecycle and Dependency Planning

**Feature**: package-management
**Status**: Complete (synthesized into [ADR-2610](../adr-2610-reliable-package-lifecycle.md))
**Created**: 2026-10-20

## Approach

Evolve Ignixa's existing package infrastructure rather than replace it with another SDK.
Separate dependency planning, artifact acquisition, resource installation, and conformance
activation. Persist installation completion independently of resource rows, retain the
resolved dependency graph and provenance, and expose incomplete outcomes to callers.

The immediate reliability problem is not tarball parsing: Ignixa already has strict
extraction and verified exact-version acquisition. The weakest boundary is between
partially imported resources and an installation that callers believe is complete.

This is a proposal based on static source inspection, not an implemented contract or a
claim that the failure scenarios below have been reproduced by executing tests.

## Evidence

### Research baseline and prior investigations

The comparison uses Ignixa commit
[`9db0620`](https://github.com/brendankowitz/ignixa-fhir/tree/9db0620d5909e1a0ffd11917a7fa1d78d3299c2b)
and GinoCanessa/dotnet-fhir-packages commit
[`8eb0edf`](https://github.com/GinoCanessa/dotnet-fhir-packages/tree/8eb0edf9050ecb8db88c755245b19c9f4c4298dd),
inspected on 2026-10-20. External references below are commit-pinned; local source links
identify the implementation examined at that baseline.

Existing investigations remain useful context, but their current-state descriptions
are historical:

- [npm-simplifier](npm-simplifier.md) favors generated core definitions plus runtime IGs.
- [enhancements](enhancements.md) proposes dependencies, updates, and rollback. Its claim
  that dependencies are ignored is superseded: the current manifest exposes dependencies
  and the Application layer walks them transitively.
- [multi-version-ig](multi-version-ig.md) distinguishes package versions, resource business
  versions, and runtime selection. Its illustrative interfaces are not the current APIs.
- [custom-structures](custom-structures.md) concerns schema extensibility, not installation
  completion; this proposal does not adopt its R6 scope.
- [version-conversion](version-conversion.md) favors native-version handling without
  implicit conversion. Compatibility checks here must not introduce automatic conversion.
- [packaging-strategy](packaging-strategy.md) concerns host/NuGet distribution, not FHIR
  package lifecycle; [valueset-migration](valueset-migration.md) concerns provider ownership.
  Neither is decided by this investigation.

[ADR-2512: Event-Sourced Conformance Management](../../../adr/adr-2512-event-sourced-conformance.md)
is still marked Proposed. The current
[activation pipeline](../../../../src/Application/Ignixa.Application/Features/Conformance/PackageActivationPipeline.cs)
nevertheless already stages events and uses an expected durable event position before
updating live state. Reuse that boundary rather than create a second activation store.

### Findings in Ignixa

| Area | Observed implementation | Consequence |
|------|-------------------------|-------------|
| Installation completeness | [Importer](../../../../src/Application/Ignixa.Application/Features/Packages/PackageResourceImporter.cs) sets resource rows active and commits batches of 500. [SQL existence check](../../../../src/DataLayer/Ignixa.DataLayer.SqlServer/Features/PackageManagement/SqlServerPackageResourceRepository.cs) returns true for any active matching resource. | A failure after an earlier batch commits can leave a partial package that a later load skips. There is no package-level completion proof in this path. |
| Dependency planning | [ImplementationGuideProvider](../../../../src/Application/Ignixa.Application/Features/Packages/ImplementationGuideProvider.cs) imports during BFS, deduplicates by name, keeps the first encountered version, and records conflicting versions or failed dependencies as skipped strings. | The effective closure depends on traversal order and can be incomplete after resources have already been written. |
| Installed manifests | The provider returns no manifest for an already-loaded package and downloads/extracts it again to discover dependencies. | Dependency inspection of installed content can depend on network availability. |
| Cancellation | Dependency loads explicitly rethrow cancellation, but the already-installed manifest re-fetch catches all exceptions and records a skip. | That re-fetch path can misclassify caller cancellation as a dependency warning. |
| Result visibility | [LoadPackageHandler](../../../../src/Application/Ignixa.Application/Features/Admin/LoadPackageHandler.cs) carries loaded/skipped lists and attempts activation of the requested root. [AdminPackageEndpoints](../../../../src/Application/Ignixa.Api/Endpoints/AdminPackageEndpoints.cs) drops those lists in its response; activation rejection is logged, not returned as activation state. | An HTTP success does not establish closure completeness or successful activation. Dependencies are not individually activated by this handler. |
| Acquisition integration | [Server registration](../../../../src/Application/Ignixa.Api/Registrations/PackageManagementRegistration.cs) uses the legacy loader with no cache. The [validation CLI](../../../../tools/Ignixa.Validation.Cli/Commands/ValidateCommand.cs) uses a legacy tarball cache and loads only explicitly supplied packages. | Server and CLI do not yet share the verified acquisition or dependency-planning workflow. |
| Stronger existing primitives | [NpmPackageAcquirer](../../../../src/Core/Ignixa.PackageManagement/Infrastructure/NpmPackageAcquirer.cs) checks approved destinations, SHA-512, bounds, deadlines, exact identity, and strict extraction. [VerifiedPackageCache](../../../../src/Core/Ignixa.PackageManagement/Infrastructure/VerifiedPackageCache.cs) keys by source/identity/digest and publishes atomically. | Reuse these guarantees; do not describe integrity checking or atomic verified-cache publication as missing. Acquisition is deliberately not installation or activation. |
| Version semantics | The SQL [SemanticVersionComparer](../../../../src/DataLayer/Ignixa.DataLayer.SqlServer/Features/PackageManagement/SemanticVersionComparer.cs) compares suffixes ordinally and is explicitly not full SemVer. The [legacy extractor](../../../../src/Core/Ignixa.PackageManagement/Infrastructure/PackageExtractor.cs) reads singular `fhirVersion`, defaults to R4, and assigns versions while traversing entries. | Prerelease ordering, plural declarations, absent versions, and manifest entry order need explicit, consistent handling in the new workflow. |
| Scope | SQL package content is shared; its existence check explicitly cannot apply the tenant argument because the table has no tenant column. | A tenant-labelled route must not be interpreted as evidence of tenant-isolated installation or activation. |
| Deactivation | The provider's unload path deactivates shared repository rows; the admin endpoint and MCP uninstall tool expose that operation. | Existing unload calls must join lifecycle coordination rather than invalidate a plan or remove an active prerequisite concurrently. |

### Transferable ideas from dotnet-fhir-packages

| Implemented prior art | What to borrow | Boundary or limitation |
|-----------------------|----------------|------------------------|
| [PackageClosure](https://github.com/GinoCanessa/dotnet-fhir-packages/blob/8eb0edf9050ecb8db88c755245b19c9f4c4298dd/src/FhirPkg/Models/PackageClosure.cs) retains exact-version nodes, preferred selections, install order, and completeness. [Resolver tests](https://github.com/GinoCanessa/dotnet-fhir-packages/blob/8eb0edf9050ecb8db88c755245b19c9f4c4298dd/test/FhirPkg.Tests/Resolution/DependencyResolverTests.cs) cover graph behavior. | Preserve every required exact-version node and parent edge; separate inventory from activation preference. | Coexisting artifacts do not imply that conflicting conformance versions can all be active. |
| [DependencyResolutionFailure](https://github.com/GinoCanessa/dotnet-fhir-packages/blob/8eb0edf9050ecb8db88c755245b19c9f4c4298dd/src/FhirPkg/Models/DependencyResolutionFailure.cs) provides typed failures with parent/depth/conflict context. | Stable failure categories and affected edges, not strings that clients must parse. | Integrate with Ignixa's existing safe acquisition/extraction diagnostics. |
| [Cache transaction journal](https://github.com/GinoCanessa/dotnet-fhir-packages/blob/8eb0edf9050ecb8db88c755245b19c9f4c4298dd/src/FhirPkg/Cache/PackageCacheTransactionJournal.cs), [coordinator](https://github.com/GinoCanessa/dotnet-fhir-packages/blob/8eb0edf9050ecb8db88c755245b19c9f4c4298dd/src/FhirPkg/Cache/PackageCacheCoordinator.cs), and [cross-process tests](https://github.com/GinoCanessa/dotnet-fhir-packages/blob/8eb0edf9050ecb8db88c755245b19c9f4c4298dd/test/FhirPkg.IntegrationTests/CrossProcessCacheIntegrationTests.cs) distinguish staged and committed generations and recover interruption. | Durable completion, per-identity ownership, crash recovery, and tests with real competing processes. | Its filesystem journal is not a SQL installation design. Existing Ignixa immutable verified-artifact publication does not need the whole journal subsystem. |
| [PackageIndexer](https://github.com/GinoCanessa/dotnet-fhir-packages/blob/8eb0edf9050ecb8db88c755245b19c9f4c4298dd/src/FhirPkg/Indexing/PackageIndexer.cs) supports package/resource/canonical discovery and managed indexes. | Package inspection without activation, preserving resource business versions independently of package versions. | Ignixa already has canonical/package repository lookups. Do not add a parallel resolver or trust archive indexes instead of resource content. |
| [RedundantRegistryClient](https://github.com/GinoCanessa/dotnet-fhir-packages/blob/8eb0edf9050ecb8db88c755245b19c9f4c4298dd/src/FhirPkg/Registry/RedundantRegistryClient.cs) bounds parallel queries and reports incomplete listings. | Explicit source failures and bounded concurrency if federated discovery is later required. | Do not infer permission to fall back to public registries or broaden Ignixa source trust. |
| [Qualification harness](https://github.com/GinoCanessa/dotnet-fhir-packages/tree/8eb0edf9050ecb8db88c755245b19c9f4c4298dd/test/FhirPkg.Qualification) and [CLI commands](https://github.com/GinoCanessa/dotnet-fhir-packages/tree/8eb0edf9050ecb8db88c755245b19c9f4c4298dd/src/FhirPkg.Cli/Commands) exercise package workflows and expose machine-readable results. | Corpus-based acceptance evidence and consistent SDK/HTTP/CLI outcomes. | Reuse Ignixa's existing tests and CLI; a new qualification executable is not a prerequisite. |

The reference's [restore contract](https://github.com/GinoCanessa/dotnet-fhir-packages/blob/8eb0edf9050ecb8db88c755245b19c9f4c4298dd/reference/dependencies.md)
is always live: it does not provide a lockfile or replayable resolution snapshot.
Persisting a resolved graph and digests would be an Ignixa addition, not a feature
already demonstrated by the reference. Exact version strings alone do not authenticate
artifact bytes.

## Proposed Design

### Ownership and durable installation

Keep acquisition, strict extraction, identities, and source-neutral graph data in
`Ignixa.PackageManagement`. Application owns installation policy, tenant authorization,
compatibility, import orchestration, and activation. Domain owns persistence contracts;
DataLayer implements durable state and concurrency. API and CLI present outcomes rather
than independently implement policy. No new service, queue, or external SDK is required.

Persist a package installation record containing exact identity, source ID, verified
artifact digest when available, original manifest, dependency edges, installation
generation/state, and safe diagnostics. Separate `Installing`, `Installed`, and `Failed`
from activation and reindex states. A cached artifact is neither an installation nor an
active package.

Chunked resource writes can remain, but unfinished generations must be excluded from
all ordinary package-resource lookup, validation, search, terminology, and activation
paths. Commit completion only after the expected resource set is stored and checked.
A zero-resource package still needs an installation record; a resource count is not a
state machine. An existing installed generation remains visible until its replacement
is complete, and a changed digest for an immutable identity is a conflict, not an
in-place overwrite.

Completion alone must not enable runtime use. Inspection and activation staging may read
completed inactive installations; ordinary canonical resolution, validation, search, and
terminology operations must use the explicitly active selection. Terminology preparation
may process completed inactive content only into non-visible staging; activation rejection
must not expose it. Active-selection changes must invalidate or version derived caches,
and failed activation must leave the previous selection in effect.

The artifact cache can remain source-scoped, but the current resource store and activation
keys are source-blind. Serialize publication, activation, and deactivation for the same
shared package name/version across sources, with durable ownership and conditional
completion so another process cannot publish a stale attempt. A second source with the
same verified bytes can add provenance without rewriting the installation; differing
bytes conflict before resource writes. Unverified content cannot establish equivalence
or replace a verified installation implicitly. Source-specific conflicting variants would
require a separate storage/activation identity decision, not just a different cache key.
Unrelated package identities can proceed concurrently. Recovery must inspect persisted state,
not assume an in-process semaphore survived a restart. An interrupted attempt may be
restarted idempotently or resumed from verified checkpoints, but never reported Installed
on the basis of a prefix of its resources. Cancellation before completion leaves a
recoverable, non-visible installation; cancellation after durable completion does not
undo that fact or imply activation happened.

Preserve the current shared-content ownership rather than silently introduce per-tenant
package storage. Any future tenant-specific activation policy must be represented and
authorized separately from globally cached/installed bytes. System partition 0 remains
internal, never an exposed `/tenant/0` management route.

Existing HTTP unload and MCP uninstall calls must use the same coordination boundary.
Treat their non-destructive effect as deactivation, retaining completed inventory and
provenance; keep physical artifact/resource removal separate. Refuse deactivation of an
active prerequisite unless an explicitly authorized operation also deactivates its
dependants safely. Revalidate persisted dependency/active-selection state when publishing
an activation so an unload after planning cannot bypass prerequisite checks. Shared
deactivation requires authorization for shared effects, not an assumption of tenant-local
ownership from the route. Preserve observable rejection if a legacy entry point cannot
yet honor these guarantees.

### Resolve before import, then activate deliberately

Build an inspectable graph of exact identities and parent edges before publishing any
installation or activation changes. Resolution may acquire verified artifacts to read
manifests; those cache writes are not installation. Retain all conflicting requirements
in diagnostics, detect cycles using active traversal paths, and bound graph depth, node
count, and acquisition concurrency. A diamond must not be mistaken for a cycle.
The initial planned workflow rejects a cyclic dependency closure with a typed cycle
diagnostic before installation publication. Cycle-tolerant installation in the reference
does not establish cycle-safe conformance activation in Ignixa.

Initially accept exact dependencies only. Unsupported ranges/tags fail with a typed
diagnostic; future range resolution must be an explicit policy that produces exact
pins, not a relaxation of `NpmPackageIdentity`. Use complete SemVer precedence for
selection, ignoring build metadata for precedence while retaining exact artifact identity.
Do not confuse package version, resource business version, and FHIR release.

Validate singular/plural FHIR declarations and reject contradictory or incompatible
declarations rather than silently assume R4. An absent declaration stays unknown until
an explicit host compatibility policy establishes it. Check core dependencies against
the in-process provider's supported release/version rather than skipping every known
core name regardless of compatibility. Do not convert resource versions implicitly.

Use deterministic dependency-first order for acyclic graphs. Default the new planned
workflow to reject unresolved dependencies and activation conflicts. Preserve the
legacy best-effort behavior behind an explicit compatibility mode during migration;
its result must say incomplete and must not claim full closure activation. Exact
versions may coexist in inventory, with an explicit compatible activation selection.
Do not choose a version merely because it was encountered first.

Persist the resolved exact graph, original requirements, source IDs, and available
verified digests with the operation. Installed manifests support offline inspection.
This does not make verified acquisition offline: the current acquirer always requests
trusted metadata, even on artifact cache hits. Exportable lockfiles/offline restore
require a separately designed trust and revalidation policy.

Installation completion precedes conformance activation. Reuse the event-sourced
activation pipeline, including its durable concurrency check and reindex lifecycle.
Activate selected dependencies before dependants and report each outcome. A failed
activation leaves installed content inspectable without claiming it is active.
Per-package atomic activation does not promise an atomic switch of the entire closure:
report partial activation and block dependants whose required activation failed.
No long SQL transaction may span network acquisition or the whole dependency closure.

### Source integration and observable outcomes

Select an explicitly administered source/protocol adapter. Standard NPM feeds can use
`NpmPackageAcquirer`; embedded and legacy direct-download feeds retain their own contracts.
Do not point the acquirer at a Simplifier-style download endpoint and assume protocol or
integrity compatibility. Legacy sources lacking authenticated expected digests must be
labelled unverified, never assigned a fabricated verification claim.

Keep the acquirer's allowlists, redirect checks, bounded extraction, deadlines, and safe
diagnostics intact. Avoid automatic public fallback, ambient credential forwarding, or
loosening integrity requirements to make a feed appear compatible. Memory limits per
artifact are not aggregate limits: the host must bound concurrent work.

Return structured resolution completeness, installation state, activation state,
pending reindex, per-package outcomes, and safe diagnostic codes/parent edges through the
SDK, admin API, MCP package tools, startup preload reporting, and validation CLI as
applicable. Preserve cancellation as cancellation, not a skipped dependency. Include
operation/installation identity for recovery and inspection, not credential-bearing URLs
or raw transport exception messages. The existing HTTP response should first stop
discarding loaded/skipped dependency information.

### Delivery order and acceptance evidence

The following are proposed implementation slices, not work performed by this PR:

1. Preserve dependency/activation outcomes at boundaries and add failure-path coverage.
2. Introduce durable completion and visibility gating across storage providers, with
   cross-source same-identity coordination, deactivation coordination, and restart
   recovery. Classify existing installations explicitly; old rows alone cannot prove
   completeness or artifact integrity.
3. Add dependency planning, persisted manifests/edges, compatibility checks, and explicit
   conflict policy, keeping legacy behavior opt-in until clients can migrate.
4. Integrate approved source adapters and verified acquisition into server and CLI;
   reuse acquisition contracts instead of duplicating transport code.
5. Consolidate version selection and add repeatable lifecycle qualification using existing
   test projects. Extend published SDK/API/CLI documentation when behavior ships.

Acceptance evidence should cover failure after batch 1 of a package larger than 500
resources; zero-resource installs; cancellation during installed-manifest retrieval;
restart and simultaneous same-identity installs; a stale owner trying to complete;
an existing visible generation during a failed replacement; and changed bytes for an
immutable identity. Include same-name/version installs from two sources, sequentially
and concurrently, with identical verified bytes, different bytes, and unverified content.

Graph coverage should include diamonds, true cycles, conflicting exact versions,
unsupported ranges, missing transitive dependencies, incompatible core requirements,
singular/plural/absent/contradictory FHIR declarations, and tar entry-order independence.
Check stable SemVer precedence, including release versus prerelease and numeric
prerelease identifiers, while retaining exact build-metadata identity.

Integration coverage must demonstrate that incomplete installations never enter normal
resolvers or terminology import; dependency activation failures block dependants; HTTP,
MCP, preload, and CLI report incomplete outcomes; retries use persisted manifests; and
shared content does not acquire an accidental per-tenant ownership claim.
Also cover completed-but-rejected activation preserving the previous runtime selection
and its caches, install-versus-unload races, and deactivation of an active prerequisite.
Use pinned representative IG fixtures across supported FHIR releases and report both
expected resource sets and dependency closure, rather than treating a successful download
or nonzero import count as proof of correctness.

## Tradeoffs

| Pros | Cons |
|------|------|
| Fixes partial-install/retry correctness at the owning boundary. | Requires package-level persistence, visibility changes, and a migration policy. |
| Reuses verified acquisition and event-sourced activation. | Legacy feed protocols cannot all provide the same integrity guarantees. |
| Makes graph conflicts and partial outcomes inspectable before activation. | Strict planned mode changes existing best-effort semantics and needs a compatibility rollout. |
| Preserves chunked imports without a closure-wide transaction. | Completion and activation are separate commits; clients must understand both states. |
| Enables deterministic inspection and recovery with persisted evidence. | A saved graph is not, by itself, offline trust or a portable lockfile. |

## Alternatives

- **Patch symptoms only**: return skipped lists, add a process-local lock, and retry
  imports. Useful first steps, but they do not establish durable completion or prevent
  another host from publishing a stale attempt.
- **Replace the package layer with FhirPkg**: gains broad registry/CLI functionality,
  but its filesystem installation does not own Ignixa's database resource visibility,
  shared-content model, or conformance activation. An adapter would still be required.
- **One transaction for the whole operation**: simpler rollback intuition, but cannot
  safely span registry I/O and large dependency closures. It also conflates installation
  with the existing event-sourced activation boundary.

Multi-registry federation, CI-build heuristics, package publishing, automatic updates,
automatic rollback, custom resource registration, and a general cache journal/eviction
service remain separate investigations, not prerequisites.

## Alignment

- [x] Follows architectural layering rules: generic artifacts in Core; policy in
  Application; persistence contracts in Domain and implementations in DataLayer.
- [x] Developer Experience: existing server registration, CLI, and test projects remain
  the integration points; no new infrastructure is mandatory.
- [x] Specification compliance: preserve exact identities, canonical business versions,
  and declared FHIR releases; no implicit cross-version conversion.
- [x] Consistent with existing patterns: reuse strict acquisition, safe diagnostics,
  chunked writes, and staged event-sourced activation rather than parallel mechanisms.

## Verdict

Viable: propose incremental lifecycle separation with durable installation completion,
pre-import dependency planning, and structured outcomes. Synthesize this proposal into
[ADR-2610: Reliable Package Lifecycle](../adr-2610-reliable-package-lifecycle.md).
The ADR remains Proposed; implementation, migration details, and measured acceptance
evidence are follow-up work.

An Astra static review on 2026-10-20 verified the main source claims and external
attribution. Its four completeness findings are incorporated above: active versus
installed visibility, cross-source identity coordination, existing unload semantics,
and an explicit cycle-rejection policy.
