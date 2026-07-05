# FHIR R6 Ballot4 Upgrade

## Purpose

The repo's R6 support is pinned to `hl7.fhir.r6.core#6.0.0-ballot2` (published 2024-08-13). HL7 has since published ballot3 (2025-04-03) and ballot4 (2025-12-18, confirmed live at `http://hl7.org/fhir/6.0.0-ballot4`). This upgrades the pin to ballot4, regenerates all R6-derived artifacts, and repoints the `fhir-codegen` submodule at its new home.

## Background

FHIR R6 is not yet normative — it ships as sequential ballot packages while HL7 iterates. The repo tracks a single pinned ballot version rather than "latest", so bumping requires an explicit, deliberate step (this document) rather than automatic drift.

Separately, the vendored `third-party/fhir-codegen` submodule (Microsoft's fhir-codegen tool, used to parse FHIR packages and drive our custom code generators) has moved: `github.com/microsoft/fhir-codegen` now permanently redirects (301) to `github.com/FHIR/fhir-codegen` — the project moved under HL7's FHIR GitHub org. The submodule URL should be repointed to avoid depending on a redirect indefinitely.

## Scope

### 1. Submodule remote

`.gitmodules` entry for `third-party/fhir-codegen` changes from:
```
url = https://github.com/microsoft/fhir-codegen.git
```
to:
```
url = https://github.com/FHIR/fhir-codegen.git
```
followed by `git submodule sync` and re-fetch. The pinned commit does not need to change unless the new remote requires it (verify the currently-pinned commit SHA still resolves under the new remote — same repo, so it should).

### 2. Version pin bump (3 locations)

All three are simple string-literal maps with no ballot-specific conditional logic — a mechanical find/replace of `6.0.0-ballot2` → `6.0.0-ballot4`:

- `codegen/Ignixa.Specification.Generators/Program.cs:67` — the `packageId` switch shared by every generation mode (`structure`, `search`, `compartment`, `codesystem`, `valueset`, `valueset-provider`, `invariant`, `coreschema`, `validation-terminology`, `narrative-template`).
- `codegen/Ignixa.Specification.Generators/CSharpCoreSchemaLanguage.cs:76` — the version string embedded into generated code output.
- `codegen/generate-search-parameters.ps1:49` — a separate generation path that invokes the vendored `fhir-codegen.exe` directly (bypassing `Program.cs`) for the `CSharpSearchParameter` language.

### 3. Fix stale output path in `generate-search-parameters.ps1`

The script's `$outputDir` resolves to `src/Ignixa.Search/Generated`, but the actual (and only) directory that exists is `src/Core/Ignixa.Search/Generated` (confirmed: R4/R4B/R5/R6/STU3 `*SearchParameterDefinitions.g.cs`, `*CompartmentDefinitions.g.cs`, `*CodeSystemMappings.g.cs` all live there). This looks like leftover drift from a `Core/` directory reorg. Fix the path as part of this change so the script doesn't silently write to a new, wrong location.

### 4. Regenerate R6 artifacts (7 files)

Run the generators against the bumped pin and commit the resulting diffs:

| Mode | Output file |
|---|---|
| `coreschema` | `src/Core/Ignixa.Specification/Generated/R6CoreSchemaProvider.g.cs` |
| `structure` | `src/Core/Ignixa.Specification/Generated/R6ReferenceMetadata.g.cs` |
| `valueset-provider` | `src/Core/Ignixa.Specification/Generated/R6ValueSetProvider.g.cs` + `Generated/Resources/R6ValueSetProviderResources.resx` |
| `search` | `src/Core/Ignixa.Search/Generated/R6SearchParameterDefinitions.g.cs` |
| `compartment` | `src/Core/Ignixa.Search/Generated/R6CompartmentDefinitions.g.cs` |
| `codesystem` | `src/Core/Ignixa.Search/Generated/R6CodeSystemMappings.g.cs` |

`R6CoreSchemaProvider.Partial.cs` is hand-maintained and is not touched by regeneration. The `invariant`, `validation-terminology`, and `narrative-template` modes currently produce no R6 (or any-version) output in the repo — out of scope, nothing regresses.

Because this spans two ballot cycles (ballot2 → ballot4), the diff may include structural changes (renamed/added/removed elements, resources, search parameters) beyond a version-string bump. Any adapter code in `Ignixa.Specification`/`Ignixa.Search` that assumed ballot2 shapes will be fixed as part of this work, discovered via build errors and test failures rather than a speculative pre-scan.

**Vendor-code note (informational only, not something we patch):** the submodule's own `CSharpFirely2` language generator (used by Microsoft/FHIR's Firely-SDK codegen path, which we do not invoke — we use our own `ILanguage` implementations: `CSharpCoreSchemaLanguage`, `CSharpSearchParameterLanguage`, etc.) contains a ballot2-specific TestScript backbone-element naming workaround. Repointing the submodule remote pulls in whatever fix upstream has already shipped for later ballots; if our own generators hit an equivalent TestScript naming collision under ballot4, it will surface as a build/test failure and be fixed in our own generator code, not the submodule.

### 5. Downstream fixups

- `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs:311` — update the `"6.0.0-ballot2"` → `FhirVersion.R6` string mapping to `"6.0.0-ballot4"`.

### 6. Docs

- `README.md` — version badge, "Multi-Version Support" bullet, specification table.
- `docs/site/docs/server/fhir/supported-resources.md:88` — `R6 (6.0.0-ballot2) | Preview | Limited support` → ballot4.
- `codegen/README.md` — currently stale independent of this change (references a `generate.ps1` that doesn't exist, omits R6 and 6 of the 9 generation modes entirely). Fix only the version-accuracy issues touched by this upgrade (mention R6, correct script references); a full rewrite of the doc's structure is out of scope for this change.

### 7. ADR

`docs/adr/adr-2607-fhir-r6-ballot4-upgrade.md` — records the decision, the submodule remote move, and whatever structural/breaking changes are discovered between ballot2 and ballot4 during regeneration (filled in with concrete findings once the regen diff is known, not written speculatively up front).

## Verification

- `dotnet build All.sln` — 0 warnings, 0 errors.
- `dotnet test All.sln` — all passing, including `Ignixa.Api.E2ETests` (uses the bumped version-string mapping) and any FHIRPath/search/conformance tests that exercise R6 resources.

## Out of scope

- Any generation mode with no existing R6 output (`invariant`, `validation-terminology`, `narrative-template`).
- Full rewrite/restructure of `codegen/README.md`.
- Bumping any other FHIR version (R4/R4B/R5/STU3) — unaffected by this change.
- Changing the pinned commit of `third-party/fhir-codegen` beyond what's needed for the remote move (no unrelated submodule version bump).
