# Typed-Model Generator

The `typed-model` mode is a multi-version pass that builds the shared-base typed FHIR facades:
classifies every type/element across the targeted versions (R4 + R5 today) and emits

- base/shared facades  -> `src/Core/Ignixa.Serialization/Generated/Models` (namespace `Ignixa.Models`)
- per-version subclasses -> `src/Core/Models/Ignixa.Models.{R4,R5}/Generated` (namespaces `Ignixa.Models.R4` / `.R5`)

See the design: `docs/features/typed-models/shared-base-restructure.md`.

## Regenerate

```bash
dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model
```

The generated output is checked in. The FHIR packages are cached offline, so this runs without
network access.

## Regen-drift guard

Generated output must not drift from the generator. CI (and local pre-commit) runs:

```bash
# PowerShell
pwsh build/check-typed-model-regen.ps1
# or bash
build/check-typed-model-regen.sh
```

The guard regenerates and fails if the on-disk output changes (snapshotting the generated dirs
before/after, so it is independent of commit state). It also surfaces **classification churn** —
e.g. adding a divergent version can move a type from identical/additive to incompatible, demoting
an element from the base to per-version, which shows up as a diff to review.

The guard is intentionally **not** part of the default `dotnet test` run (it invokes the generator);
run it in CI or on demand.
