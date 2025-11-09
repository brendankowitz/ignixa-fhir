# ADR-2532 Revision Summary: Custom Resource Support Architecture

**Date**: 2025-01-08
**Status**: ✅ COMPLETE - Updated and ready for implementation

---

## What Was Revised

ADR-2532 (Unified Validation, Terminology & Package Management Architecture) has been **revised to include a comprehensive new section** on Custom Resource Support Architecture.

### New Section Added

**"Custom Resource Support Architecture"** (lines 1210-1912)

This section addresses how custom resources (resource types not in the core FHIR specification) fit into the unified validation, terminology, and package architecture.

---

## Key Decisions Documented

### 1. Middleware Acceptance Strategy
**Hybrid Validation Approach**

✅ Accept custom resources IF:
- IG defining them is loaded (StructureDefinition available), OR
- Tenant configuration allows `AllowUnknownResourceTypes = true`

Includes implementation example of `FhirResourceTypeValidator` that:
- Checks core FHIR resources first
- Resolves StructureDefinition from PackageResource table
- Falls back to tenant configuration
- Logs warnings for unvalidated custom resources

### 2. Validation Tier Assignment
**Custom resources follow same validation tiers as core resources**

| Tier | When Applied | Custom Resources |
|------|-------------|-----------------|
| **Tier 1** | Always | ✅ Always (JSON structure) |
| **Tier 2** | If IG loaded | ✅ Applied if StructureDefinition available |
| **Tier 3** | If terminology available | ✅ Applied if profiles loaded |

**Strictness Modes**:
- `Lenient`: Tier 1 only, accept any custom resource
- `Moderate`: Tier 1+2 if IG loaded, warn if not (recommended)
- `Strict`: Tier 1+2+3 required, reject if IG not loaded

### 3. Storage Architecture
**Custom resource instances stored in Resource table (same as core resources)**

```
Resource Table:
  - Core resources (Patient, Observation, etc.)
  - Custom resources (ViewDefinition, etc.)
  - Multi-tenant isolated (same infrastructure)

PackageResource Table:
  - StructureDefinitions from IGs (metadata)
  - Shared across tenants
```

**Rationale**:
- ✅ No schema changes needed
- ✅ Same multi-tenant isolation
- ✅ Same versioning, history, search indexing

### 4. Custom Resource Detection
**Standardized algorithm based on FHIR spec**

A custom resource is identified when ALL conditions met:

1. `StructureDefinition.kind == "resource"`
2. `StructureDefinition.derivation == "specialization"` (not a profile)
3. `StructureDefinition.type` NOT in core FHIR resources (153 for R4)
4. `StructureDefinition.abstract != true`

Includes full implementation of `CustomResourceDetector` with:
- All 153 core FHIR R4 resource types hardcoded
- Support for R5 (165 resources) and STU3 (94 resources)
- Usage examples

### 5. CapabilityStatement Advertisement
**Auto-advertise custom resources when IG loaded**

Custom resources automatically added to CapabilityStatement when:
1. StructureDefinition loaded into PackageResource table
2. CustomResourceDetector identifies it as custom
3. Tenant has access to IG

Includes full implementation of `CapabilityStatementBuilder` with:
- Addition of core resources
- Addition of custom resources from IGs
- Profile URLs and SearchParameters

### 6. SearchParameter Registration
**Extract SearchParameters from IG during loading**

When IG loaded:
1. Extract SearchParameters from package
2. Register with SearchIndexer
3. Optional: Reindex existing resources
4. Log results

Includes full implementation of `ImplementationGuideLoader` with:
- Detecting SearchParameters for custom resources
- Registration logic
- Reindex options

### 7. End-to-End Workflow Example
**Complete ViewDefinition workflow documented**

4-step example showing:
1. Load SQL-on-FHIR v2 IG
2. Check CapabilityStatement (ViewDefinition now advertised)
3. Create ViewDefinition instance
4. Search ViewDefinitions using indexed SearchParameters

---

## FHIR Specification Grounding

All decisions based on FHIR R4/R5/R6 specifications:

- **StructureDefinition.kind**: https://hl7.org/fhir/R4/structuredefinition-definitions.html#StructureDefinition.kind
- **StructureDefinition.derivation**: https://hl7.org/fhir/R4/structuredefinition-definitions.html#StructureDefinition.derivation
- **RESTful API**: https://hl7.org/fhir/R4/http.html#general
- **CapabilityStatement**: https://hl7.org/fhir/R4/capabilitystatement.html

**Key findings**:
- ✅ Custom resources defined via StructureDefinition with `kind="resource"` and `derivation="specialization"`
- ✅ Servers have discretion to accept/reject unknown resource types (404 Not Found is standard)
- ✅ Validation is **discretionary** - servers choose how much to perform
- ✅ Same validation approach as core resources when StructureDefinition available
- ✅ Servers MUST declare supported resources in CapabilityStatement

---

## Integration with Existing ADR-2532

Custom resources fit seamlessly into unified architecture:

| Component | Integration |
|-----------|-----------|
| **Package Management** | StructureDefinitions extracted from IGs → PackageResource table |
| **Validation System** | Custom StructureDefinitions build ValidationSchemas (Tier 2+3) |
| **Terminology Services** | Custom resource ValueSet bindings validated same as core |
| **CapabilityStatement** | Auto-updated when custom StructureDefinitions loaded |
| **Search Indexing** | Custom SearchParameters registered and indexed |
| **Multi-tenancy** | Custom resources isolated per tenant (Resource table) |
| **Caching** | StructureDefinitions cached via ConformanceCache |

---

## Immediate Next Steps

### Phase 1: Foundation (Already documented in ADR-2532)
- Implement PackageResource table (✓ documented)
- Implement NPM package loader (✓ documented)
- Implement conformance cache (✓ documented)

### Phase 2: Validation System (Already documented in ADR-2532)
- Implement validation schema builder (✓ documented)
- Implement basic assertions (✓ documented)
- Integrate terminology (✓ documented)

### New: Phase X: Custom Resource Support
Based on revised ADR-2532, implement:

1. **CustomResourceDetector class**
   - Detect custom resources from StructureDefinition metadata
   - Support R4, R5, STU3 versions

2. **Update FhirResourceTypeValidator**
   - Check core FHIR resources
   - Resolve StructureDefinition from packages for unknown types
   - Check tenant configuration

3. **Update CapabilityStatementBuilder**
   - Auto-advertise custom resources from loaded IGs
   - Include SearchParameters for custom resources

4. **Update ImplementationGuideLoader**
   - Extract SearchParameters from IG
   - Register SearchParameters for custom resources
   - Optional reindexing

5. **Testing**
   - Load SQL-on-FHIR v2 IG
   - Create ViewDefinition instance
   - Search using indexed parameters
   - Validate against StructureDefinition

---

## Success Criteria

✅ Custom resources accepted when IG loaded
✅ Middleware accepts ViewDefinition (and other custom resources)
✅ Validation tiers applied consistently
✅ CapabilityStatement advertises custom resources
✅ SearchParameters for custom resources indexed and functional
✅ End-to-end workflow: Load IG → Create instance → Search → Validate

---

## Files Updated

- **ADR-2532**: Added 700+ lines documenting custom resource architecture
  - Location: `docs/investigations/ADR-2532-unified-validation-terminology-package-architecture.md`
  - Section: "Custom Resource Support Architecture" (lines 1210-1912)

---

## Implementation Ready

✅ Architecture documented
✅ FHIR spec compliance verified
✅ Code examples provided for all components
✅ Integration points mapped to existing ADR-2532
✅ Success criteria defined
✅ Next steps clear

**Recommendation**: Use revised ADR-2532 as specification for implementing custom resource support. All architectural decisions, code examples, and integration points are documented and ready for development.
