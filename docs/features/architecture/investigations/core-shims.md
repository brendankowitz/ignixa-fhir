# Investigation: Ignixa Core + Shims Architecture

**Feature**: architecture
**Status**: Viable
**Created**: 2025-01-07

## Executive Summary

Replace current `Ignixa.Abstractions` (Firely-compatible) with:
1. **Ignixa.Abstractions.Core** - High-performance, zero dependencies
2. **Ignixa.Shims.FirelySdk** - Firely SDK interop (separate package)

All Ignixa libraries use **Core only** (no compatibility overloads). Consumers opt-into Firely interop via shim package.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                     Consumer Code                            │
└────────────┬──────────────────────────────┬─────────────────┘
             │                              │
   ┌─────────▼────────┐         ┌──────────▼──────────┐
   │ Ignixa Libraries │         │ External Firely     │
   │ (Pure Core)      │         │ Tools               │
   │                  │         │                     │
   │ - Validation     │         │ - Hl7.FhirPath      │
   │ - FhirPath       │         │ - Firely Validator  │
   │ - Search         │         │ - Community libs    │
   └─────────┬────────┘         └──────────┬──────────┘
             │                              │
             ▼                              ▼
   ┌──────────────────────┐      ┌──────────────────────────┐
   │ Ignixa.Abstractions  │      │ Ignixa.Shims.FirelySdk   │
   │      .Core           │◄─────┤ (Adapter Package)        │
   │ (Zero dependencies)  │      │                          │
   │                      │      │ Depends on:              │
   │ - IElement           │      │ - Ignixa.Abstractions    │
   │ - IType              │      │   .Core                  │
   │ - TypeInfo           │      │ - Hl7.Fhir.ElementModel  │
   └──────────────────────┘      └──────────────────────────┘
```

## Package Structure

### Ignixa.Abstractions.Core

**Purpose:** High-performance abstractions, zero external dependencies

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- Zero dependencies! -->
  </PropertyGroup>
</Project>
```

**Files:**
```
Ignixa.Abstractions.Core/
├── IElement.cs              - Runtime element navigation (ReadOnlySpan)
├── IType.cs                 - Schema metadata (includes Order property)
├── ISchema.cs               - Schema provider
├── TypeInfo.cs              - Strongly-typed type info (struct)
├── FhirPrimitive.cs         - FHIR primitive enum (byte-backed)
└── ElementValue.cs          - Primitive value container
```

### Ignixa.Shims.FirelySdk

**Purpose:** Convert between Ignixa Core ↔ Firely SDK

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ignixa.Abstractions.Core\..." />
    <PackageReference Include="Hl7.Fhir.ElementModel" />
  </ItemGroup>
</Project>
```

**Files:**
```
Ignixa.Shims.FirelySdk/
├── FirelySdkExtensions.cs        - IElement.ToTypedElement()
├── CoreExtensions.cs             - ITypedElement.ToCoreElement()
├── TypedElementAdapter.cs        - Core → Firely wrapper
└── CoreElementAdapter.cs         - Firely → Core wrapper
```

## Usage Patterns

### Pattern 1: Pure Ignixa (No Firely Dependency)

```csharp
using Ignixa.Abstractions.Core;
using Ignixa.Validation;

public class MyValidator
{
    public ValidationResult Validate(IElement element)
    {
        // Zero allocation, high performance
        foreach (var identifier in element.Children("identifier"))
        {
            ValidateIdentifier(identifier);
        }
        return ValidationResult.Success;
    }
}
```

**Packages needed:**
- ✅ `Ignixa.Abstractions.Core`
- ✅ `Ignixa.Validation`
- ❌ No Firely SDK!

### Pattern 2: Ignixa + Firely Interop

```csharp
using Ignixa.Abstractions.Core;
using Ignixa.Shims.FirelySdk;  // Extension methods
using Hl7.FhirPath;             // External Firely tool

public class HybridValidator
{
    public bool ValidateWithFirelyTool(IElement coreElement)
    {
        // Convert Core → Firely using shim
        ITypedElement firlyElement = coreElement.ToTypedElement();

        // Use Firely's FhirPath engine
        var result = firlyElement.Select("name.exists()");

        return result.Any();
    }
}
```

**Packages needed:**
- ✅ `Ignixa.Abstractions.Core`
- ✅ `Ignixa.Shims.FirelySdk` (brings Firely SDK transitively)
- ✅ `Hl7.FhirPath`

### Pattern 3: Bidirectional Conversion

```csharp
using Ignixa.Abstractions.Core;
using Ignixa.Shims.FirelySdk;

public class Processor
{
    public IElement Process(ITypedElement firlyInput)
    {
        // 1. Firely → Core
        IElement coreElement = firlyInput.ToCoreElement();

        // 2. Process with Ignixa (fast!)
        IElement result = DoIgnixaProcessing(coreElement);

        // 3. Return as Core (consumer can convert if needed)
        return result;
    }
}
```

## Key Decisions

### ✅ DO: Single API Surface in Ignixa Libraries

```csharp
// Ignixa.Validation - ONLY Core API
public class FhirValidator
{
    public ValidationResult Validate(IElement element) { ... }

    // ❌ NO ITypedElement overload!
}
```

**Rationale:**
- Simpler maintenance (one API, not two)
- Forces clear dependency choice (Core vs Firely)
- Consumers use shim for conversion when needed

### ✅ DO: Consumers Control Firely Dependency

```csharp
// Scenario A: Pure Ignixa
Install-Package Ignixa.Abstractions.Core
Install-Package Ignixa.FhirPath
// → Zero Firely SDK dependency

// Scenario B: Need Firely interop
Install-Package Ignixa.Abstractions.Core
Install-Package Ignixa.Shims.FirelySdk
Install-Package Hl7.FhirPath
// → Firely SDK via shim (explicit opt-in)
```

### ❌ DON'T: Compatibility Overloads in Libraries

```csharp
// ❌ WRONG: Don't add compatibility overloads
public class Validator
{
    public bool Validate(IElement element) { ... }
    public bool Validate(ITypedElement element) => Validate(element.ToCoreElement());
}

// ✅ RIGHT: Single API, consumers use shim
public class Validator
{
    public bool Validate(IElement element) { ... }
}

// Consumer handles conversion:
ITypedElement firlyElement = ...;
validator.Validate(firlyElement.ToCoreElement());  // Shim provides extension
```

## Migration Plan

### Phase 1: Create Core Package (Week 1)

- [ ] Create `Ignixa.Abstractions.Core` project
- [ ] Define `IElement`, `IType`, `ISchema` interfaces
- [ ] Implement `TypeInfo`, `FhirPrimitive`, `ElementValue`
- [ ] Unit tests for Core types

### Phase 2: Create Shim Package (Week 1)

- [ ] Create `Ignixa.Shims.FirelySdk` project
- [ ] Implement `ToTypedElement()` extension
- [ ] Implement `ToCoreElement()` extension
- [ ] Implement adapter wrappers
- [ ] Integration tests with actual Firely SDK

### Phase 3: Migrate Ignixa Libraries (Week 2)

- [ ] Migrate `Ignixa.Serialization` to Core
- [ ] Migrate `Ignixa.FhirPath` to Core
- [ ] Migrate `Ignixa.Validation` to Core
- [ ] Migrate `Ignixa.Search` to Core
- [ ] All libraries use ONLY Core interfaces

### Phase 4: Update Structure Providers (Week 2)

- [ ] Update codegen to generate Core-based providers
- [ ] Regenerate R4/R4B/R5/R6/STU3 providers
- [ ] Benchmark: Verify performance improvements

### Phase 5: Deprecate Old Abstractions

- [ ] Mark `Ignixa.Abstractions` as `[Obsolete]`
- [ ] Migration guide for consumers
- [ ] Example projects showing shim usage

## Benefits

### 1. Clean Separation

| Aspect | Core | Shims |
|--------|------|-------|
| Dependencies | Zero | Hl7.Fhir.* |
| Purpose | High performance | Firely interop |
| Used by | All Ignixa libs | Consumers only |
| Breaking changes | Rare | Can evolve with Firely |

### 2. Pay-for-What-You-Use

```csharp
// 95% of users (pure Ignixa)
using Ignixa.Abstractions.Core;
// → Zero Firely SDK cost

// 5% of users (need Firely)
using Ignixa.Shims.FirelySdk;
// → Explicit opt-in to Firely SDK
```

### 3. Version Flexibility

```xml
<!-- Consumer A: Firely R4 -->
<PackageReference Include="Ignixa.Shims.FirelySdk" />
<PackageReference Include="Hl7.Fhir.R4" Version="6.0.0" />

<!-- Consumer B: Firely R5 -->
<PackageReference Include="Ignixa.Shims.FirelySdk" />
<PackageReference Include="Hl7.Fhir.R5" Version="6.0.0" />
```

Both work! Shim adapts to whatever Firely version consumer chooses.

### 4. Future-Proof

- Core remains stable (breaking changes rare)
- Shims evolve with Firely SDK
- Can support multiple Firely versions:
  - `Ignixa.Shims.FirelySdk.V5` (Firely 5.x)
  - `Ignixa.Shims.FirelySdk.V6` (Firely 6.x)

## Performance Expectations

| Metric | Current (Abstractions) | Core | Shim Overhead |
|--------|------------------------|------|---------------|
| Element navigation | 850 ns, 384 B | 120 ns, 0 B | +100 ns (one-time) |
| Type checking | 45 ns (string) | 2 ns (enum) | +5 ns |
| Child access | Allocates `IEnumerable` | `ReadOnlySpan` (zero) | N/A |

**With shims:** Conversion adds 100-150 ns one-time cost, then full Core performance.

## Open Questions

1. **Should shim cache conversions?**
   - Pro: Faster repeated conversions
   - Con: Memory overhead
   - **Decision**: Start without caching, add if profiling shows value

2. **Support multiple Firely SDK versions?**
   - Pro: Consumers control Firely version
   - Con: More shim packages to maintain
   - **Decision**: Single shim targets Firely 6.x, evaluate multi-version later

3. **What about `Ignixa.Abstractions` (current)?**
   - **Decision**: Mark `[Obsolete]`, remove in v2.0.0

## Success Metrics

- ✅ All Ignixa libraries use Core only (zero Firely SDK references)
- ✅ Shim package enables bidirectional conversion
- ✅ Benchmark shows 5-10x faster element navigation
- ✅ Zero breaking changes for consumers (via shim)
