# ADR 2602: FHIR Anonymizer Library

## Status

Accepted (Implemented in PR #221)

## Context

Healthcare data sharing requires compliance with privacy regulations (HIPAA Safe Harbor, GDPR Article 6) while preserving clinical utility for research, analytics, and secondary use. Organizations need to:

- **De-identify PHI/PII** for HIPAA Safe Harbor compliance (45 CFR §164.514(b)(2))
- **Enable data linkage** across anonymized datasets (deterministic anonymization)
- **Support batch processing** of FHIR resources at scale
- **Maintain FHIR validity** after anonymization (structure preservation)
- **Be version-agnostic** (STU3, R4, R4B, R5, R6 support)

**Why existing solutions weren't sufficient**:

- Microsoft's FHIR-Tools-for-Anonymization is tightly coupled to Firely SDK v3/v4 (incompatible with Ignixa's custom serialization)
- Hard-coded for specific FHIR versions (R4/STU3 only)
- Requires C# code changes to add custom anonymization rules

We forked Microsoft's tool and adapted it to Ignixa's architecture (JsonNode-based serialization, FHIRPath evaluation, multi-version schema support).

## Decision

Implement **Ignixa.Anonymizer** as a Core SDK library with the following architectural decisions:

```mermaid
flowchart TB
    subgraph "Anonymizer Architecture"
        Config[AnonymizerConfiguration<br/>JSON + FHIRPath rules]
        Engine[AnonymizerEngine<br/>Rule evaluation]
        Visitor[AnonymizationVisitor<br/>Tree traversal]

        Config --> Engine
        Engine --> Visitor

        subgraph "Processor Layer"
            CryptoHash[CryptoHashProcessor<br/>HMAC-SHA256]
            Encrypt[EncryptProcessor<br/>AES-256-GCM]
            Redact[RedactProcessor<br/>HIPAA Safe Harbor]
            DateShift[DateShiftProcessor<br/>+/- 50 days]
            Other[Substitute | Perturb<br/>Generalize | Keep]
        end

        Visitor --> CryptoHash
        Visitor --> Encrypt
        Visitor --> Redact
        Visitor --> DateShift
        Visitor --> Other

        subgraph "Batch Processing"
            Executor[FhirPartitionedExecutor<br/>8 partitions x 100 batch]
            Reader[IFhirDataReader]
            Consumer[IFhirDataConsumer]
        end

        Engine --> Executor
        Executor --> Reader
        Executor --> Consumer
    end
```

### 1. Hash-Based Anonymization (vs Encryption)

**Decision**: Use HMAC-SHA256 as the default anonymization method for identifiers.

| Choice | Rationale | Trade-off |
|--------|-----------|-----------|
| **HMAC-SHA256** (default) | Deterministic (enables linkage), one-way (GDPR compliant), FIPS 140-2 approved | Not reversible; use EncryptProcessor if re-identification needed |
| AES-256-GCM (optional) | Reversible if key is retained | Not one-way; doesn't meet GDPR "anonymization" standard |

**Implementation**:
```csharp
// CryptoHashProcessor uses HMAC-SHA256 with user-provided key
var hash = HMACSHA256.ComputeHash(plainData, key);
```

### 2. FHIRPath Rule Engine (vs Hard-Coded Rules)

**Decision**: JSON configuration with FHIRPath expressions for anonymization rules.

**Example Configuration**:
```json
{
  "fhirPathRules": [
    {
      "path": "Patient.identifier",
      "method": "cryptoHash"
    },
    {
      "path": "Patient.birthDate",
      "method": "redact"
    }
  ]
}
```

| Choice | Rationale | Trade-off |
|--------|-----------|-----------|
| **FHIRPath rules** | Declarative, FHIR-native, version-agnostic, config-driven | Slower than hard-coded (mitigated by FHIRPath AST caching) |
| C# delegates | Fastest performance | Requires code deployment for rule changes |

**Why FHIRPath**:
- FHIR-native query language (HL7 standard)
- Works across all FHIR versions (R4, R5, STU3, R4B, R6)
- Users can update rules without code changes
- Cached AST compilation provides acceptable performance

### 3. Visitor Pattern (vs Recursive Transformation)

**Decision**: `AbstractElementNodeVisitor` with `Visit/EndVisit` hooks for resource tree traversal.

```csharp
public abstract class AbstractElementNodeVisitor
{
    public virtual bool Visit(ResourceJsonNode resource, IElement node) => true;
    public virtual void EndVisit(ResourceJsonNode resource, IElement node) { }
}
```

| Pattern | Rationale | Trade-off |
|---------|-----------|-----------|
| **Visitor** | Separation of concerns, extensibility, state management via stack | More classes, higher abstraction |
| Recursive transform | Simpler, fewer classes | State management is harder, less extensible |

**Why Visitor**:
- Stateful processing (track anonymization context via `_contextStack`)
- Clean separation: `AnonymizationVisitor` handles traversal, `ResourceProcessor` handles rules
- Extensibility: add new visitors without modifying core logic

### 4. Processor Factory Pattern

**Decision**: `IAnonymizerProcessorFactory` for extensibility with custom processors.

```csharp
public interface IAnonymizerProcessorFactory
{
    IAnonymizerProcessor CreateProcessor(string name, Dictionary<string, object>? settings);
}
```

**Example**: Organizations can implement custom processors (e.g., tokenization, synthetic data generation) without modifying core library.

| Approach | Rationale | Trade-off |
|----------|-----------|-----------|
| **Factory pattern** | Open/Closed principle, dependency injection support | Requires interface implementation |
| Direct instantiation | Simpler | Hard to extend without library changes |

### 5. Security Model

**Decision**: User-provided keys via configuration or environment variables.

| Element | Choice | Rationale |
|---------|--------|-----------|
| **Algorithm** | HMAC-SHA256 | FIPS 140-2 compliant, deterministic, one-way |
| **Key length** | Minimum 32 characters | Security best practice |
| **Key storage** | User-managed (config/env vars) | Ignixa doesn't store keys; users control security |
| **Key rotation** | Not supported | Breaks determinism (linkage requirement) |

**Trade-off**: No automatic key rotation. If key is compromised, all anonymized data must be re-processed with new key (breaks linkage to prior data).

### 6. HIPAA Safe Harbor Implementation

**Decision**: Partial redaction for HIPAA identifiers (ages >89, 3-digit zip, year-only dates).

HIPAA Safe Harbor (45 CFR §164.514(b)(2)(i)) requires removal of 18 identifiers:

| Identifier | Implementation |
|------------|----------------|
| Dates | Year-only (or redact if age >89) |
| Ages >89 | Redact completely |
| Zip codes | Keep first 3 digits (or redact if restricted ZCTA) |
| Geographic subdivisions | Not implemented (user responsibility) |
| Other identifiers | Configurable via FHIRPath rules |

**Restricted ZCTAs** (hardcoded list, population <20,000):
```json
["036", "059", "102", "203", "205", "369", "556", "692", "821", "823", "878", "879", "884", "893"]
```

**Configuration**:
```json
{
  "parameters": {
    "enablePartialDatesForRedact": true,
    "enablePartialAgesForRedact": true,
    "enablePartialZipCodesForRedact": true,
    "restrictedZipCodeTabulationAreas": ["036", "059", ...]
  }
}
```

**Trade-off**: Restricted ZCTAs are hard-coded (not user-configurable without code changes). Future enhancement: load from external file.

### 7. FHIR Metadata (Provenance)

**Decision**: Add `meta.security` labels to anonymized resources.

**Example Output**:
```json
{
  "resourceType": "Patient",
  "meta": {
    "security": [
      {
        "system": "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "code": "REDACTED",
        "display": "Redacted"
      },
      {
        "system": "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "code": "CRYTOHASH",
        "display": "Cryptographic Hash Function"
      }
    ]
  }
}
```

| Approach | Rationale | Trade-off |
|----------|-----------|-----------|
| **meta.security labels** | Standards-compliant (HL7 Observation Value), queryable via `_security` parameter | Modifies resource (adds metadata) |
| Separate AuditEvent | Doesn't modify resource | Harder to track which resources were anonymized |

**Why meta.security**:
- FHIR-native provenance mechanism
- Enables queries like `GET /Patient?_security=REDACTED`
- Immutable history (security labels are preserved in versioned resources)

### 8. Batch Processing

**Decision**: `FhirPartitionedExecutor` with configurable partitions and batch size.

| Parameter | Default | Range | Purpose |
|-----------|---------|-------|---------|
| `PartitionCount` | 8 | 1-32 | Parallelism (CPU cores) |
| `BatchSize` | 100 | 1-1000 | Memory-bounded batching |
| `KeepOrder` | true | true/false | Preserve input order (important for NDJSON) |

**Architecture**:
```
Input Stream → Partition 1 (Batch 100) → Anonymize → Output Stream
            → Partition 2 (Batch 100) → Anonymize → Output Stream
            → ...
            → Partition 8 (Batch 100) → Anonymize → Output Stream
```

**Why partitioned execution**:
- Parallelism: 8 concurrent batches on 8-core machine
- Memory-bounded: Process 100 resources at a time (prevents OOM on large datasets)
- Order preservation: Important for NDJSON export (line numbers match input)

## Consequences

### Positive

- **Compliance-ready**: HIPAA Safe Harbor and GDPR Article 6 support
- **Extensible**: Custom processors via factory pattern
- **Performant**: Batch processing with parallelism (8 partitions default)
- **Portable**: Core SDK library (no API dependencies)
- **Multi-version**: STU3, R4, R4B, R5, R6 support
- **Standards-compliant**: FHIRPath rules, meta.security labels

### Negative

- **Key management burden**: Users must securely store cryptographic keys
- **No key rotation**: Breaks determinism (design trade-off for linkage)
- **Configuration complexity**: FHIRPath knowledge required for advanced rules
- **Hard-coded restricted ZCTAs**: Not externally configurable
- **No federated anonymization**: Single-engine only (no distributed processing)

### Trade-offs Table

| Decision | Alternative | Rationale for Choice |
|----------|-------------|----------------------|
| HMAC-SHA256 | AES-256-GCM | Determinism for linkage, GDPR compliance |
| FHIRPath rules | C# delegates | Config without code deployment |
| Visitor pattern | Recursive transform | State management, extensibility |
| Processor factory | Direct instantiation | Open/Closed principle |
| User-managed keys | Built-in key vault | No infrastructure dependencies |
| meta.security labels | AuditEvent | Standards-compliant provenance |
| Partitioned executor | Single-threaded | Parallelism for batch processing |

## Integration Strategy

**Current**: Standalone library (no API integration).

**Future Options** (to be decided in separate ADR):
- `$anonymize` custom operation (POST /Patient/$anonymize)
- `$export` integration (anonymize during bulk export)
- CLI tool (command-line batch processing) - **Already implemented** in `tools/Ignixa.Anonymizer.Cli`

**CLI Tool Example**:
```bash
# Anonymize R4 resources
ignixa-anonymizer r4 anonymize \
  --input-folder ./raw-fhir \
  --output-folder ./anonymized \
  --config-file ./config.json \
  --input-format ndjson \
  --output-format ndjson
```

## References

- **Source**: Forked from [Microsoft FHIR-Tools-for-Anonymization](https://github.com/microsoft/FHIR-Tools-for-Anonymization)
- **HIPAA Safe Harbor**: 45 CFR §164.514(b)(2) - [Link](https://www.hhs.gov/hipaa/for-professionals/privacy/special-topics/de-identification/index.html)
- **GDPR**: Article 6 (Lawfulness of processing) - [Link](https://gdpr-info.eu/art-6-gdpr/)
- **FHIRPath Spec**: [http://hl7.org/fhirpath/](http://hl7.org/fhirpath/)
- **Implementation**: PR #221 - Adds Anonymizer library
- **CLI Tool**: `tools/Ignixa.Anonymizer.Cli/Program.cs`
- **Configuration Examples**: `test/Ignixa.Anonymizer.Tests/Configurations/common-config.json`
