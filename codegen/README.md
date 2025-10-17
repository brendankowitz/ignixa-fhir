# FHIR Structure Definition Provider Code Generator

This folder contains the code generation infrastructure for creating `IStructureDefinitionSummaryProvider` implementations for different FHIR versions (R4, R4B, R5, STU3).

## Overview

The code generator uses Microsoft's [fhir-codegen](https://github.com/microsoft/fhir-codegen) tool with a custom `ILanguage` implementation to generate reliable, correct structure definition providers from official FHIR packages.

### Architecture

```
codegen/
├── IgnixaCodegen.sln               # Separate solution for code generation
├── Ignixa.Specification.Generators/ # Custom ILanguage implementation
│   └── CSharpStructureProviderLanguage.cs
├── fhir-codegen/                   # Git submodule (Microsoft fhir-codegen)
├── generate.ps1                    # PowerShell generation script
├── generate.sh                     # Bash generation script
└── Directory.Build.props           # Disables CPM for codegen
```

### Why a Separate Solution?

The main `All.sln` uses Central Package Management (CPM), which conflicts with the fhir-codegen submodule's explicit package versions. By isolating code generation in `IgnixaCodegen.sln`, we:

1. Keep the main solution simple and fast to build
2. Avoid CPM conflicts with third-party dependencies
3. Generate files on-demand rather than on every build
4. Make the build process more transparent

## Usage

### Prerequisites

- .NET 8 SDK or later
- PowerShell 7+ (for `generate.ps1`) or Bash (for `generate.sh`)

### Generating Providers

#### Generate all FHIR versions:

**PowerShell:**
```powershell
cd codegen
./generate.ps1
```

**Bash:**
```bash
cd codegen
./generate.sh
```

#### Generate a specific version:

**PowerShell:**
```powershell
./generate.ps1 -FhirVersion R4
```

**Bash:**
```bash
./generate.sh R4
```

Supported versions: `R4`, `R4B`, `R5`, `STU3`, `All`

### Output

Generated files are placed in:
```
src/Ignixa.Specification/Generated/
├── R4StructureDefinitionSummaryProvider.g.cs
├── R4BStructureDefinitionSummaryProvider.g.cs
├── R5StructureDefinitionSummaryProvider.g.cs
└── STU3StructureDefinitionSummaryProvider.g.cs
```

These files are marked as `linguist-generated=true` in `.gitattributes`.

## Development

### Building the Code Generator

```bash
cd codegen
dotnet build IgnixaCodegen.sln -c Release
```

### Modifying the Generator

1. Edit `Ignixa.Specification.Generators/CSharpStructureProviderLanguage.cs`
2. Build: `dotnet build IgnixaCodegen.sln`
3. Run generation scripts to test changes

### Key Classes

- **CSharpStructureProviderLanguage**: Implements `ILanguage` interface from fhir-codegen
- **CSharpStructureProviderConfig**: Configuration for output directory and namespace
- **GenerateProviderCode**: Generates the actual C# code from `DefinitionCollection`

### How It Works

1. Scripts build both fhir-codegen and Ignixa.Specification.Generators
2. fhir-codegen downloads and parses FHIR packages (e.g., `hl7.fhir.r4.core`)
3. fhir-codegen creates a `DefinitionCollection` with all FHIR structures
4. Our custom `CSharpStructureProviderLanguage` traverses the collection
5. Generated C# code is written to `src/Ignixa.Specification/Generated/`

## Troubleshooting

### Build Errors

If you encounter build errors, ensure:
1. The fhir-codegen submodule is initialized: `git submodule update --init --recursive`
2. .NET 8 SDK is installed
3. You're building from the `codegen/` directory

### CPM Conflicts

If you see NU1008 errors about package versions:
- These should only occur when building from the root directory
- Always build IgnixaCodegen.sln from within the `codegen/` folder
- The `codegen/Directory.Build.props` and `codegen/Directory.Packages.props` files disable CPM for this folder

### Missing Packages

If fhir-codegen fails to download packages:
- Ensure you have internet connectivity
- The tool automatically downloads from `https://packages.fhir.org/`
- Packages are cached in `~/.fhir/packages/`

## Implementation Details

### Complete IStructureDefinitionSummaryProvider Generation

The generator now produces **complete, production-ready** implementations with:

1. **Full Type Dictionary** (~210 types: 148 resources + 41 complex types + 20 primitives)
   - Lazy element initialization for memory efficiency
   - Accurate IsAbstract, IsResource flags from StructureDefinition

2. **Complete Element Definitions** (~59,000 lines of generated code)
   - All `IElementDefinitionSummary` properties mapped correctly:
     - **Cardinality**: Accurate min/max from ElementDefinition (not binary guesses)
     - **InSummary**: Actual `IsSummary` flag (not guessed from `IsRequired`)
     - **IsModifier**: Actual `IsModifier` flag (was always false in old implementation)
     - **IsRequired**: Accurate `Min >= 1` (not just binary required/optional)
     - **IsCollection**: Accurate `Max != "1"` (supports max constraints like "0..5")
   - Choice types with all type options (e.g., Observation.value[x] has 11 types)
   - XML representation detection (XmlAttr for id/lang, XHtml for div, XmlElement default)
   - Proper field ordering from StructureDefinition

3. **Memory-Efficient Design**
   - Lazy initialization: Elements not created until first `GetElements()` call
   - Shared type instances: All references to same type share one instance
   - Immutable collections: `IReadOnlyCollection` for safety

4. **Nested Implementation Classes**
   - `GeneratedStructureDefinitionSummary`: Implements `IStructureDefinitionSummary`
   - `GeneratedElementDefinitionSummary`: Implements `IElementDefinitionSummary`
   - Both are private sealed classes within the generated provider

### Data Source Comparison

See `src/Ignixa.Specification/Schema/gaps.md` for a detailed analysis of improvements over the old JSON schema-based implementation.

| Feature | JSON Schema (Old) | fhir-codegen (New) |
|---------|------------------|---------------------|
| **Cardinality** | ⚠️ Binary (0/1 or */1) | ✅ Full (0..*, 1..5, etc.) |
| **Reference Targets** | ❌ Missing | ✅ Full list per type |
| **Modifier Elements** | ❌ Always false | ✅ Accurate from FHIR |
| **Summary Flag** | ⚠️ Guessed | ✅ Accurate from FHIR |
| **Bindings** | ❌ Missing | ✅ ValueSet + strength |
| **Constraints** | ❌ Missing | ✅ FHIRPath invariants |
| **Maintenance** | ⚠️ Manual updates | ✅ Auto-regenerate |

### Generated Code Stats (R4)

- **Total Lines**: ~59,318 lines
- **Resources**: 148 types
- **Complex Types**: 41 types
- **Primitive Types**: 20 types
- **Element Methods**: 209 type-specific methods
- **Build Time**: ~12 seconds (generation) + ~11 seconds (compile)
- **File Size**: ~3.5 MB

## Future Enhancements

- [x] ~~Generate full `IStructureDefinitionSummary` implementations~~ **COMPLETE**
- [x] ~~Generate `IElementDefinitionSummary` for each property~~ **COMPLETE**
- [x] ~~Include cardinality, data types, and reference targets~~ **COMPLETE**
- [ ] Support for profiles and extensions (Phase 4)
- [ ] ValueSet bindings and constraint metadata (Phase 4)
- [ ] Validation rule generation (Tier 1 validator, Phase 5)
