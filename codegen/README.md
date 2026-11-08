# FHIR Specification Code Generators

This folder generates the FHIR specification artifacts Ignixa checks in (schema providers, reference metadata, code-system
mappings, search parameter and compartment definitions, ValueSets, typed models) from the official FHIR core packages
(STU3, R4, R4B, R5, R6). It uses Microsoft's [fhir-codegen](https://github.com/microsoft/fhir-codegen) to load the
packages and custom `ILanguage` implementations in `Ignixa.Specification.Generators` to write the C#.

```
codegen/
├── IgnixaCodegen.sln                    # Separate solution for code generation
├── Ignixa.Specification.Generators/     # The generator console app: Program.cs picks a mode, one *Language.cs per mode
├── fhir-codegen/                        # Git submodule (Microsoft fhir-codegen)
├── generate.sh                          # Regenerates what every version needs (see below)
├── generate-search-parameters.ps1       # PowerShell wrapper for search-parameter generation through the fhir-codegen CLI
├── Directory.Build.props                # Disables CPM for codegen
└── Directory.Packages.props
```

### Why a separate solution?

The main `All.sln` uses Central Package Management (CPM), which conflicts with the fhir-codegen submodule's explicit
package versions. Isolating code generation in `IgnixaCodegen.sln` keeps the main build fast, avoids the CPM conflicts,
and generates files on demand rather than on every build.

## Prerequisites

- The submodule: `git submodule update --init codegen/fhir-codegen`.
- A .NET SDK. The generator targets `net8.0`; with only a newer runtime installed, run with `DOTNET_ROLL_FORWARD=Major`.
- Bash. There is no PowerShell equivalent of `generate.sh`; on Windows run it from Git Bash or WSL.
- Network access to `https://packages.fhir.org/` on first use; packages are cached in `~/.fhir/packages/`.

## Regenerating

```bash
cd codegen
./generate.sh          # every version
./generate.sh R4       # one of R4, R4B, R5, R6, STU3
```

For each version `generate.sh` runs three generator modes:

| Step | Mode | Output |
|------|------|--------|
| `structure` | default | `src/Core/Ignixa.Specification/Generated/{R4,R4B,R5,R6,STU3}ReferenceMetadata.g.cs` |
| `coreschema` | `coreschema` | `src/Core/Ignixa.Specification/Generated/*CoreSchemaProvider.g.cs` |
| `codesystem` | `codesystem` | `src/Core/Ignixa.Search/Generated/*CodeSystemMappings.g.cs` |

A step that fails stops its version and names the step (`✗ R4: the coreschema step failed`); the script goes on to the
other versions and exits non-zero. On success the outputs overwrite the checked-in files, so `git status` shows exactly
what changed, and nothing else.

The other modes are run directly, with an optional output directory as the last argument:

```bash
dotnet run --project Ignixa.Specification.Generators -c Release -- <mode> <version> [outputDir]
```

Modes: `search`, `compartment` (both to `src/Core/Ignixa.Search/Generated`), `valueset`, `valueset-provider`,
`invariant`, `validation-terminology`, `narrative-template`, and `typed-model` (multi-version; it owns its output paths).

## Code-system mappings

`codesystem` mode gives each resource element of type `code` the code system its binding implies, which the search
indexer attaches to the element's values (`Patient.gender` → `http://hl7.org/fhir/administrative-gender`). It maps an
element only when its binding is required or extensible, or has a required `elementdefinition-maxValueSet`, and the
ValueSet that names includes exactly one system. Every other element is left without a system, and the run logs it with
the reason: a ValueSet that is not in the package, includes another ValueSet, or draws on several code systems.

## Troubleshooting

- **Build errors:** check the submodule is initialized and that you build `IgnixaCodegen.sln` from within `codegen/`.
- **NU1008 (CPM) errors:** these only occur when building from the repository root; `codegen/Directory.Build.props` and
  `codegen/Directory.Packages.props` disable CPM for this folder.
- **Package download failures:** check connectivity to `https://packages.fhir.org/`, or pre-populate `~/.fhir/packages/`.
