# Conformance Suite Consolidation

**Date:** 2026-07-20
**Status:** Design approved, pending implementation plan

## Problem

The TestScript conformance corpus exists twice, and the copies have diverged.

| | `ignixa-fhir/conformance-tests/` | `ignixa-lab/backend/src/Ignixa.Lab.Suites/testscripts/` |
|---|---|---|
| Scripts | 13 | 87 |
| Categories | Bundles, CRUD, Search, Validation | + Foundation, Microsoft, Operations, Regression, Subscriptions |
| CRUD | 2 | 25 |
| Search | 8 | 23 |
| Packaged | no | yes — `IgnixaLab.TestScript.Suites`, content-only NuGet |

`ignixa-fhir`'s set is a strict filename subset of lab's. Consequence: the server's own
conformance gate — `test/Ignixa.Api.E2ETests/Conformance/TestScriptConformanceReportTests.cs`
and `tools/Ignixa.ConformanceMatrix.Cli` — currently runs ~15% of the corpus that exists.
The published conformance matrix understates coverage by the same margin.

The intended end state was already recorded in `Ignixa.Lab.Suites.csproj`:

> "Interim local package per ADR-2607; will be repointed at the upstream ignixa-fhir
> suites artifact once it is published."

That repointing never happened, and lab's copy grew 6.7x in the interim.

### Divergence detail

Of the 13 overlapping files, 10 are byte-identical and 0 exist only in `ignixa-fhir`.
Three differ in both directions, so this is a merge and not a fast-forward:

| File | Lines only in `ignixa-fhir` |
|---|---|
| `Bundles/transaction.json` | 10 |
| `Search/chaining.json` | 12 |
| `Validation/validate-op.json` | 1 |

## Decision

**`ignixa-fhir` owns the canonical corpus, at `src/Core/Ignixa.TestScript.Suites/testscripts/`.
`ignixa-lab` becomes a pure consumer via `PackageReference`.**

### Why `ignixa-fhir` and not lab or a third repo

The suites depend on four TestScript extensions — `parametrize`, `fhirVersions`,
`requiresCapability`, `fhirfakes` — defined, versioned, and ADR'd in `ignixa-fhir`
(ADR-2607), with an IG package `ignixa.fhir.testscript-extensions` planned to ship from
`docs/site/static/`. Per that ADR only three of the four are ignore-safe; a suite using
`fhirfakes` against an engine that lacks it fails hard rather than skipping. Suites and
engine must therefore ship from one commit. Splitting them across repos makes every
extension change a two-repo lockstep.

Secondary: `ignixa-fhir`'s own conformance gate must not depend on a downstream repo.
Lab consumes Ignixa NuGet packages; lab owning the suites would make the product
dependency circular. Lab is also explicitly the exploratory samples repo — making it the
canonical home for the conformance corpus makes a scratchpad load-bearing.

A vendor-neutral third repo (`ignixa-conformance`) was considered and rejected for now.
It has a better external story given the `Microsoft/` suites, but costs a third repo and
applies the publish round-trip tax to both consumers instead of one. This layout extracts
cleanly if that framing becomes worth paying for.

### Why inside the project directory, not repo root

- The pack glob stays inside the project cone (`testscripts/**/*.json`) rather than
  reaching `../../../conformance-tests/**`. Reaching outside breaks `dotnet pack` run
  from the project directory, complicates SourceLink path mapping, and makes the project
  non-relocatable.
- It places the corpus inside the `Core.slnf` boundary, so an SDK-only slice carries it.
  At repo root it sits outside `src/` and would need explicit inclusion.
- It matches lab's existing layout, so the port is a copy rather than a restructure.

The one thing repo-root placement bought — advertising the corpus to anyone landing on
the repo — is replaced by a README pointer and a docs-site link.

## Target layout

```
src/Core/Ignixa.TestScript.Suites/
  Ignixa.TestScript.Suites.csproj
  build/
    Ignixa.TestScript.Suites.targets
  testscripts/
    Bundles/ CRUD/ Foundation/ Microsoft/ Operations/
    Regression/ Search/ Subscriptions/ Validation/
```

`testscripts/` as the folder name and `PackagePath="testscripts/"` are both load-bearing:
lab's `Suites/SuiteCatalog.cs` reads that layout from the consumer's output directory.
Neither may change during this migration.

Repo-root `conformance-tests/` is deleted.

## Path resolution

The packaged `.targets` copies suites into the consumer's output under `testscripts/`,
so package consumers resolve them at `AppContext.BaseDirectory/testscripts/`.

In-repo consumers adopt the same mechanism. `test/Ignixa.TestScript.Tests` and
`test/Ignixa.Api.E2ETests` each add an explicit `<Import>` of
`src/Core/Ignixa.TestScript.Suites/build/Ignixa.TestScript.Suites.targets` and resolve
`Path.Combine(AppContext.BaseDirectory, "testscripts")`.

The relative path inside the targets — `$(MSBuildThisFileDirectory)../testscripts/**` —
is correct for both layouts, because in-repo `build/` is a sibling of `testscripts/`
exactly as it is at the package root. One glob serves both delivery paths.

Two benefits beyond tidiness:

1. The hand-rolled ancestor walk in `ConformanceScriptParseTests.cs:13-36` goes away.
   Its failure message ("Expected it to be a sibling of All.sln or an ancestor of the
   test output directory") describes an assumption that is fragile under git worktrees,
   of which this repo currently has eight active.
2. `ignixa-fhir` starts exercising the delivery mechanism it publishes. Today nothing in
   `ignixa-fhir` imports that targets file, so lab is the canary — breakage surfaces only
   after a publish.

**Sharp edge:** `build/*.targets` auto-import applies to `PackageReference` only, not
`ProjectReference`. The in-repo wiring must be an explicit `<Import>`. This needs a
comment in both csproj files, or it will eventually be "simplified" into a
`ProjectReference` and silently stop copying.

## Packaging

Port `Ignixa.Lab.Suites.csproj` verbatim except:

- `PackageId` → `Ignixa.TestScript.Suites`
- Drop the hardcoded `<Version>0.1.0-local</Version>`; inherit repo versioning (ADR-2606)
- Rewrite `<Description>` to drop the "interim / will be repointed" language

Everything else carries over unchanged, **including the comments**. The
`TargetsForTfmSpecificContentInPackage` / `WriteSourceRevisionFile` target and the
`MSB3030` guard on `source-revision.txt` both encode non-obvious MSBuild behaviour that
was expensive to discover:

- A plain `BeforeTargets="Pack"` hook is unreliable — MSBuild can skip those targets via
  their own up-to-date checks. `TfmSpecificPackageFile` is the documented extension point.
- `TfmSpecificPackageFile`'s `PackagePath` is a target *directory*; naming the file there
  nests it under a same-named subdirectory.
- NuGet's default pack excludes dotfiles, so `source-revision.txt` cannot be `.source-revision`.
- A literal `Include` with `CopyToOutputDirectory` hard-fails the consumer build (MSB3030)
  if the file is absent, hence the `Exists()` guard.

CI needs no workflow change: `ci.yml`'s pack step discovers packable projects via
`find src/Core tools src/Application/Ignixa.Sidecar.Contracts -name "*.csproj"`, so the
new project is picked up automatically and lands on the public feed under ADR-2606
versioning.

## Migration steps

Sequenced as two independent phases. Phase 1 lands entirely in `ignixa-fhir` and is
self-contained; lab stays on its local `IgnixaLab.TestScript.Suites` package throughout
and is never broken. Phase 2 happens only after `Ignixa.TestScript.Suites` has actually
published.

### Phase 1 — `ignixa-fhir` (this effort)

1. Create `src/Core/Ignixa.TestScript.Suites/`; copy lab's `testscripts/`, `build/`, and
   csproj across.
2. Hand-merge the 3 divergent files so the fhir-only assertions (10/12/1 lines) survive.
3. Apply the csproj changes above; add the project to `All.sln`.
4. Wire the two test projects via explicit `<Import>`; delete `FindRepositoryDirectory`
   and the ancestor-walk logic; switch both to `AppContext.BaseDirectory`.
5. Delete repo-root `conformance-tests/`.
6. Update path references: `docs/adr/adr-2607-testscript-extensions.md` (4),
   `docs/site/docs/core-sdk/testscript.md` (2),
   `tools/Ignixa.ConformanceMatrix.Cli/README.md` (1). The CLI itself takes
   `--tests <path>` and needs no code change.
7. Add the `RepoGuards` case (below).
8. Add README + docs-site pointers to the new location.

### Phase 2 — `ignixa-lab` (deferred until first publish)

9. Delete `backend/src/Ignixa.Lab.Suites`, add
   `PackageReference Include="Ignixa.TestScript.Suites"`, add a dev-time path override so
   a local `ignixa-fhir` checkout's `testscripts/` wins over the package during authoring.

### Consequence of the split: a duplication window

Between the two phases both copies exist, and lab's is still writable. If a suite is
authored in lab during that window it is lost work — phase 2 deletes that tree wholesale.
Mitigations, in preference order:

- Keep the window short. Phase 2 should follow the first publish promptly, not sit.
- Land a note in `backend/src/Ignixa.Lab.Suites/README.md` during phase 1 stating the tree
  is frozen and pointing authors at `ignixa-fhir`. Cheap, and it is the only signal an
  author actually encounters at the moment they would create the file.
- Track phase 2 as an issue at the time phase 1 merges, so it is not remembered only by
  this document.

Note that phase 1 does not resolve the drift for lab — lab keeps running its own 87 until
phase 2. What phase 1 fixes immediately is `ignixa-fhir`'s own conformance gate, which is
the dishonest one.

## Guardrail

A `Ignixa.RepoGuards.Tests` case asserting that every script under `testscripts/` parses,
and that every extension URL any script uses is one of the four ADR-2607 canonicals:

- `http://ignixa.io/testscript/parametrize`
- `http://ignixa.io/testscript/fhirVersions`
- `http://ignixa.io/testscript/requiresCapability`
- `http://ignixa.io/testscript/fhirfakes`

This catches the drift class that produced the current situation: a suite authored
against an engine capability that the shipped engine does not have.

## Expected fallout

The E2E conformance gate will go red when the corpus jumps 13 → 87. That is the intended
outcome — the current green is dishonest. Triage each new failure into:

- **Fix now** — a real server bug the expanded corpus exposed.
- **Gate** — a legitimate capability gap; mark with `requiresCapability` so it records as
  skipped with a reason rather than failing.
- **Known gap** — track as an issue; do not silently delete the script.

The published conformance matrix will change shape substantially on the next docs deploy.

## Follow-up

ADR-2607 documents the repo-root layout as accepted and needs an amendment recording this
location decision and the `AppContext.BaseDirectory` resolution mechanism.

## Out of scope

- Moving or renaming the `Microsoft/` suites.
- The vendor-neutral `ignixa-conformance` third-repo option.
- The `Core.slnf` / `Server.slnf` and package-metadata work discussed alongside this
  (separate effort; this design only depends on `Core.slnf` existing eventually, not now).
