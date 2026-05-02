# Migration Plan: Anonymizer Library → DeId

## Executive Summary

This plan covers the rename of `Ignixa.Anonymizer` to `Ignixa.DeId` and analyzes alignment with three emerging FHIR Implementation Guides for de-identification:

- **IHE ITI De-Identification Handbook** (`ITI.DeIdHandbook`) — process framework and technique catalog
- **HL7 FHIR DARTS** — Data Anonymization for Research and Transparency Services
- **HL7 FHIR DAPL** — De-identified/Anonymized FHIR Profiles Library

**Bottom line**: The rename is straightforward. The current engine covers ~40% of the IHE technique catalog and aligns well with DARTS/DAPL architecturally, but significant gaps exist in advanced privacy models (k-anonymity, differential privacy), statistical disclosure control (microaggregation, small-cell suppression), and governance workflows.

---

## 1. Naming Decision

| Candidate | Package | Namespace | Engine Type | Verdict |
|-----------|---------|-----------|-------------|---------|
| **DeId** | `Ignixa.DeId` | `Ignixa.DeId` | `DeIdEngine` | **Recommended** |
| DeIdentifier | `Ignixa.DeIdentifier` | `Ignixa.DeIdentifier` | `DeIdentifierEngine` | Too verbose; reads like an agent noun |
| DeID | `Ignixa.DeID` | `Ignixa.DeID` | `DeIDEngine` | Violates .NET camelCase for multi-word identifiers |

**Rationale**: `DeId` follows .NET conventions (e.g., `HttpClient`, `XmlDocument`, `ResourceId`). "De" is the prefix (de-identification), "Id" is the standard .NET casing for identifier. It is concise, unambiguous, and matches the industry term "DeID".

**Type renames**:

| Current | New |
|---------|-----|
| `AnonymizerEngine` | `DeIdEngine` |
| `AnonymizerOptions` | `DeIdOptions` |
| `IAnonymizerEngine` | `IDeIdEngine` |
| `IAnonymizerProcessor` | `IDeIdProcessor` |
| `IAnonymizerPipeline` | `IDeIdPipeline` |
| `AnonymizerMethod` | `DeIdMethod` |
| `AnonymizerContext` | `DeIdContext` |
| `AnonymizerPipeline` | `DeIdPipeline` |
| `AnonymizerRule` | `DeIdRule` |
| `AnonymizerError` | `DeIdError` |
| `AnonymizationResult` | `DeIdResult` |
| `FhirPartitionedExecutor` | No change (generic batch utility) |

---

## 2. Rename Scope

### 2.1 Code

| Area | Files | Effort |
|------|-------|--------|
| `src/Core/Ignixa.Anonymizer/` | Rename directory; update namespace declarations in ~60 `.cs` files | Medium |
| `src/Core/Ignixa.Anonymizer.Tests/` | Update usings, type references | Medium |
| `tools/Ignixa.Anonymizer.Cli/` | Rename project, update package refs, CLI command names | Medium |
| Solution file (`All.sln`) | Update project paths and names | Low |
| `Directory.Build.props` / `Directory.Packages.props` | Update package metadata if published | Low |

### 2.2 Documentation

| Area | Updates |
|------|---------|
| `docs/adr/adr-2602-anonymizer-library.md` | Rename ADR file; update internal references; add "Superseded by" note or update in place |
| `docs/site/docs/core-sdk/anonymizer.md` | Rename to `deid.md`; update all code samples and type names |
| `README.md` (library root) | Update installation command, namespace examples |
| `docs/site/sidebars.ts` | Update navigation link |
| `docs/site/docusaurus.config.ts` | Search for any hard-coded paths |

### 2.3 Configuration & Backwards Compatibility

- **Breaking change**: This is a major-version bump for the package.
- **Config JSON**: The `method` string values (`cryptoHash`, `dateShift`, `redact`, etc.) should remain unchanged to avoid breaking user configurations.
- **CLI commands**: Rename `ignixa-anonymizer` → `ignixa-deid`.

---

## 3. IG Alignment Analysis

### 3.1 IHE ITI DeId Handbook

The IHE handbook defines a **multi-stage process** and a **technique catalog** for de-identification. It does NOT define custom FHIR operations or resource profiles for the engine itself — it focuses on process, risk metrics, and AuditEvent logging.

#### 3.1.1 Process Alignment

The IHE three-stage workflow maps cleanly to the existing processor pipeline:

| IHE Stage | Purpose | Current Mapping |
|-----------|---------|-----------------|
| Stage 1: Reversible Pseudonymization | Replace direct IDs with recoverable pseudonyms | `EncryptProcessor` (reversible) or `SubstituteProcessor` with mapping |
| Stage 2: Irreversible Pseudonymization + Anonymization | One-way transforms + quasi-identifier processing | `CryptoHashProcessor`, `RedactProcessor`, `DateShiftProcessor`, `GeneralizeProcessor`, `PerturbProcessor` |
| Stage 3: Recipient Risk Verification | Validate output meets risk threshold | **Gap** — no built-in risk scoring |

#### 3.1.2 AuditEvent Logging

The IHE handbook requires `AuditEvent` recording for de-identification actions. The current library adds `meta.security` labels to resources but does NOT emit `AuditEvent` resources.

| IHE Requirement | Current State | Gap |
|-----------------|---------------|-----|
| AuditEvent for de-identification at source | Not implemented | **Medium** — add optional `IAuditEventLogger` |
| AuditEvent for authorized re-identification | Not implemented | **Medium** |
| ISO 21089 lifecycle code for de-identification | Not used | **Low** — add to AuditEvent when implemented |

#### 3.1.3 Security Labels

The IHE handbook references DS4P security labels. The current labels are partially aligned:

| Current Label | IHE/DS4P Equivalent | Status |
|---------------|---------------------|--------|
| `REDACTED` | `REDACTED` | Aligned |
| `CRYTOHASH` | `PSEUDED` (pseudonymized) | **Gap** — add `PSEUDED` |
| `ABSTRED` | — | Ignixa-specific |
| `MASKED` | `ANONYED` (anonymized) | **Gap** — add `ANONYED` |
| `PERTURBED` | — | Ignixa-specific |
| `SUBSTITUTED` | — | Ignixa-specific |
| `GENERALIZED` | — | Ignixa-specific |

**Opportunity**: Add `PSEUDED` and `ANONYED` from the HL7 v3 ObservationValue code system to align with IHE/DS4P conventions.

---

### 3.2 HL7 FHIR DARTS

DARTS defines **services** that consume identifiable data and produce de-identified/anonymized output using **policy identifiers** to drive rule selection. It targets USCDI resources for federal reporting (HRSA, CDC, SAMHSA).

| DARTS Capability | Current State | Gap |
|------------------|---------------|-----|
| Policy-driven de-identification (policy identifier) | JSON config file only | **Medium** — no policy registry or dynamic policy resolution |
| USCDI resource scope | Generic FHIR (all resources) | **Low** — could add USCDI-scoped preset configs |
| Line-level de-identified output | Supported (resource-by-resource) | Aligned |
| Pseudonymize patient data | Partial (`CryptoHash`/`Encrypt` on identifiers) | **Medium** — no dedicated pseudonymization service with mapping table |
| Produce DAPL profiles as output | Not implemented | **Medium** — no DAPL profile generation |

**Opportunity**: The current FHIRPath rule engine is architecturally compatible with DARTS. A future enhancement could add a `IPolicyResolver` interface that maps policy identifiers to rule configurations, enabling DARTS-style policy-driven execution.

---

### 3.3 HL7 FHIR DAPL

DAPL provides **FHIR profiles** for de-identified and anonymized data. It is explicitly a data structure specification, not a process specification. DARTS consumes DAPL profiles for output.

| DAPL Aspect | Current State | Gap |
|-------------|---------------|-----|
| De-identified Patient profile | Not implemented | **Low** — add optional output profile enforcement |
| De-identified Observation profile | Not implemented | **Low** |
| Removal of US Core mandatory elements | Configurable via FHIRPath rules | Partially achievable today |

**Opportunity**: DAPL profile validation could be added as an optional post-processing step (e.g., `ValidationHandler` in the pipeline) to ensure output conforms to DAPL profiles.

---

## 4. Capability Gap Matrix

### 4.1 Direct Identifier Transformations

| Technique | Current Support | Processor | Gap Level | Notes |
|-----------|-----------------|-----------|-----------|-------|
| Masking / removal | **Full** | `RedactProcessor` | — | Handles primitive + complex removal |
| Pseudonymization (recoverable) | **Partial** | `EncryptProcessor` | Medium | No mapping table / linking key management |
| Pseudonymization (cryptographic) | **Full** | `CryptoHashProcessor` | — | HMAC-SHA256 deterministic |
| Canonicalization before transform | **None** | — | Low | Trim, lowercase, normalize formats before hashing |

### 4.2 Quasi-Identifier Transformations (Categorical)

| Technique | Current Support | Processor | Gap Level | Notes |
|-----------|-----------------|-----------|-----------|-------|
| Generalization (code hierarchies) | **Full** | `GeneralizeProcessor` | — | FHIRPath case expressions |
| Suppression (rare values) | **None** | — | Medium | Remove outlier categories |
| Small-cell suppression (k≥3/5) | **None** | — | High | Requires dataset-level analysis |
| Permutation (reorder values) | **None** | — | Medium | Shuffle values across records |
| Blanking and imputing | **Partial** | `SubstituteProcessor` | Medium | No statistical plausibility engine |

### 4.3 Quasi-Identifier Transformations (Numeric)

| Technique | Current Support | Processor | Gap Level | Notes |
|-----------|-----------------|-----------|-----------|-------|
| Top/bottom coding | **None** | — | Low | Cap extreme values |
| Microaggregation (k≥3) | **None** | — | High | Group records, replace with cluster average |
| Generalize small counts | **None** | — | Medium | Merge sparse categories |
| Noise addition | **Partial** | `PerturbProcessor` | Low | Uniform noise only; no Gaussian/Laplace |

### 4.4 Temporal Data Transformations

| Technique | Current Support | Processor | Gap Level | Notes |
|-----------|-----------------|-----------|-----------|-------|
| Date shifting | **Full** | `DateShiftProcessor` | — | Consistent per-person shift |
| Coarsening (year/month) | **Full** | `RedactProcessor` | — | Partial date redaction |
| Time coarsening (hour bins) | **None** | — | Low | Round time components |
| Age binning | **None** | — | Low | Convert birthDate to age ranges |

### 4.5 Privacy Models

| Technique | Current Support | Gap Level | Notes |
|-----------|-----------------|----------|-------|
| k-Anonymity analysis | **None** | High | Dataset-level indistinguishability |
| Differential Privacy (ε) | **None** | High | Bounded individual impact |
| Risk scoring (prosecutor/population) | **None** | Medium | θj = 1/fj metrics |

### 4.6 Cryptographic Methods

| Technique | Current Support | Gap Level | Notes |
|-----------|-----------------|----------|-------|
| HMAC-SHA256 | **Full** | — | Default |
| SHA-512 with salt | **None** | Low | Alternative hash algorithm |
| AES-CBC (reversible) | **Full** | — | Current `EncryptProcessor` |
| AES-256-GCM | **None** | Low | Add authenticated encryption mode |
| Format-preserving encryption (FPE) | **None** | Medium | Preserve structure (e.g., SSN format) |
| Homomorphic encryption | **None** | High | Computation on encrypted data |

### 4.7 Text and Media

| Technique | Current Support | Gap Level | Notes |
|-----------|-----------------|----------|-------|
| NLP-based redaction | **None** | High | Free-text PHI detection (names, dates, MRNs) |
| Pixel scrubbing | **None** | High | Burnt-in identifiers in images |

### 4.8 Governance and Workflow

| Technique | Current Support | Gap Level | Notes |
|-----------|-----------------|----------|-------|
| AuditEvent generation | **None** | Medium | IHE requirement |
| Policy identifier resolution | **None** | Medium | DARTS requirement |
| Data Use Agreement (DUA) tracking | **None** | Low | Workflow metadata |
| Risk assessment automation | **None** | Medium | Pre/post de-id risk scoring |
| Re-identification authorization | **None** | Medium | IHE AuditEvent for re-id |

---

## 5. Prioritized Opportunities

### 5.1 P1: Rename (Immediate)

Execute the rename to `Ignixa.DeId`. This is pure refactoring with no functional changes.

### 5.2 P2: Security Label Alignment (Low Effort, High Standards Value)

Add `PSEUDED` and `ANONYED` labels from HL7 v3 ObservationValue to align with IHE/DS4P.

### 5.3 P3: AuditEvent Logging (Medium Effort, High Compliance Value)

Add an optional `IAuditEventLogger` interface and `AuditEvent` generator. This satisfies IHE DeId Handbook requirements and enables DARTS compliance.

```csharp
public interface IDeIdAuditLogger
{
    ValueTask LogDeidentificationAsync(
        DeIdContext context,
        IReadOnlyList<ProcessorResult> operations,
        CancellationToken cancellationToken);
}
```

### 5.4 P4: Policy Resolver (Medium Effort, High DARTS Value)

Introduce `IPolicyResolver` to map policy identifiers to rule configurations. Enables DARTS-style policy-driven execution.

```csharp
public interface IPolicyResolver
{
    ValueTask<DeIdOptions> ResolveAsync(string policyId, CancellationToken cancellationToken);
}
```

### 5.5 P5: Additional Processors (Variable Effort)

| Processor | Effort | Value | Privacy Model |
|-----------|--------|-------|---------------|
| `TopBottomCodingProcessor` | Low | Medium | Disclosure control |
| `TimeCoarseningProcessor` | Low | Medium | Temporal |
| `CanonicalizationProcessor` | Low | Low | Data quality |
| `GaussianNoiseProcessor` | Low | Medium | Statistical |
| `FormatPreservingEncryptProcessor` | Medium | Medium | Cryptographic |
| `SmallCellSuppressionProcessor` | High | High | k-anonymity |
| `MicroaggregationProcessor` | High | High | k-anonymity |
| `DifferentialPrivacyProcessor` | High | High | ε-differential privacy |
| `NlpRedactionProcessor` | High | High | Text |

### 5.6 P6: DAPL Profile Output (Low Effort, Medium Value)

Add optional DAPL profile validation as a pipeline post-processing step. Can reuse existing `ValidationHandler` pattern.

---

## 6. Migration Steps

### Phase 1: Code Rename (1-2 days)

1. Rename `src/Core/Ignixa.Anonymizer/` → `src/Core/Ignixa.DeId/`
2. Rename `tools/Ignixa.Anonymizer.Cli/` → `tools/Ignixa.DeId.Cli/`
3. Update namespace declarations across all `.cs` files
4. Update type names (`AnonymizerEngine` → `DeIdEngine`, etc.)
5. Update test projects and usings
6. Update solution file
7. Update package references and build props
8. Run `dotnet build All.sln` → 0 warnings, 0 errors
9. Run `dotnet test All.sln` → all passing

### Phase 2: Documentation Rename (1 day)

1. Rename `docs/adr/adr-2602-anonymizer-library.md` → `docs/adr/adr-2602-deid-library.md`
2. Rename `docs/site/docs/core-sdk/anonymizer.md` → `docs/site/docs/core-sdk/deid.md`
3. Update all code samples and type references in docs
4. Update `README.md`
5. Update navigation (`sidebars.ts`, etc.)
6. Build docs site locally and verify

### Phase 3: ADR Update (0.5 day)

1. Update ADR-2602 with rename rationale
2. Add IG alignment summary to ADR
3. Reference this migration document

---

## 7. Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Breaking consumers of `Ignixa.Anonymizer` package | High | Major version bump; document migration guide; keep old package deprecated but not deleted |
| Configuration JSON compatibility | Medium | Keep `method` string values unchanged (`cryptoHash`, `redact`, etc.) |
| Test coverage regression | Medium | Full test run after rename; no logic changes means tests should pass identically |
| Documentation link rot | Low | Add redirects or update all internal links |

---

## 8. Appendix: IG Reference Links

- IHE ITI DeId Handbook: `https://build.fhir.org/ig/IHE/ITI.DeIdHandbook/branches/main/`
- HL7 FHIR DARTS: `https://build.fhir.org/ig/HL7/fhir-darts/`
- HL7 FHIR DAPL: `https://build.fhir.org/ig/HL7/fhir-dapl/`
