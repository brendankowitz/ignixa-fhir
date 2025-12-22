---
sidebar_position: 2
title: Supported Resources
description: FHIR resources supported by Ignixa
---

# Supported Resources

Ignixa supports all standard FHIR R4, R4B, and R5 resources. This page details resource-specific capabilities.

## Resource Categories

### Patient Administration

| Resource | Read | Create | Update | Delete | Search |
|----------|------|--------|--------|--------|--------|
| Patient | ✅ | ✅ | ✅ | ✅ | ✅ |
| Practitioner | ✅ | ✅ | ✅ | ✅ | ✅ |
| PractitionerRole | ✅ | ✅ | ✅ | ✅ | ✅ |
| Organization | ✅ | ✅ | ✅ | ✅ | ✅ |
| Location | ✅ | ✅ | ✅ | ✅ | ✅ |
| HealthcareService | ✅ | ✅ | ✅ | ✅ | ✅ |
| Endpoint | ✅ | ✅ | ✅ | ✅ | ✅ |

### Clinical

| Resource | Read | Create | Update | Delete | Search |
|----------|------|--------|--------|--------|--------|
| Observation | ✅ | ✅ | ✅ | ✅ | ✅ |
| Condition | ✅ | ✅ | ✅ | ✅ | ✅ |
| Procedure | ✅ | ✅ | ✅ | ✅ | ✅ |
| DiagnosticReport | ✅ | ✅ | ✅ | ✅ | ✅ |
| Encounter | ✅ | ✅ | ✅ | ✅ | ✅ |
| Immunization | ✅ | ✅ | ✅ | ✅ | ✅ |
| AllergyIntolerance | ✅ | ✅ | ✅ | ✅ | ✅ |
| MedicationRequest | ✅ | ✅ | ✅ | ✅ | ✅ |
| MedicationStatement | ✅ | ✅ | ✅ | ✅ | ✅ |
| CarePlan | ✅ | ✅ | ✅ | ✅ | ✅ |
| CareTeam | ✅ | ✅ | ✅ | ✅ | ✅ |
| Goal | ✅ | ✅ | ✅ | ✅ | ✅ |

### Diagnostic

| Resource | Read | Create | Update | Delete | Search |
|----------|------|--------|--------|--------|--------|
| ServiceRequest | ✅ | ✅ | ✅ | ✅ | ✅ |
| Specimen | ✅ | ✅ | ✅ | ✅ | ✅ |
| ImagingStudy | ✅ | ✅ | ✅ | ✅ | ✅ |
| Media | ✅ | ✅ | ✅ | ✅ | ✅ |

### Documents & Communication

| Resource | Read | Create | Update | Delete | Search |
|----------|------|--------|--------|--------|--------|
| DocumentReference | ✅ | ✅ | ✅ | ✅ | ✅ |
| DocumentManifest | ✅ | ✅ | ✅ | ✅ | ✅ |
| Communication | ✅ | ✅ | ✅ | ✅ | ✅ |
| CommunicationRequest | ✅ | ✅ | ✅ | ✅ | ✅ |
| Consent | ✅ | ✅ | ✅ | ✅ | ✅ |

### Financial

| Resource | Read | Create | Update | Delete | Search |
|----------|------|--------|--------|--------|--------|
| Coverage | ✅ | ✅ | ✅ | ✅ | ✅ |
| Claim | ✅ | ✅ | ✅ | ✅ | ✅ |
| ClaimResponse | ✅ | ✅ | ✅ | ✅ | ✅ |
| ExplanationOfBenefit | ✅ | ✅ | ✅ | ✅ | ✅ |

### Conformance

| Resource | Read | Create | Update | Delete | Search |
|----------|------|--------|--------|--------|--------|
| CapabilityStatement | ✅ | ✅ | ✅ | ✅ | ✅ |
| StructureDefinition | ✅ | ✅ | ✅ | ✅ | ✅ |
| ValueSet | ✅ | ✅ | ✅ | ✅ | ✅ |
| CodeSystem | ✅ | ✅ | ✅ | ✅ | ✅ |
| SearchParameter | ✅ | ✅ | ✅ | ✅ | ✅ |
| OperationDefinition | ✅ | ✅ | ✅ | ✅ | ✅ |

## Resource Version Support

| FHIR Version | Resources | Notes |
|--------------|-----------|-------|
| R4 (4.0.1) | All R4 resources | Primary target |
| R4B (4.3.0) | All R4B resources | R4 + extensions |
| R5 (5.0.0) | All R5 resources | Full support |
| R6 (6.0.0-ballot2) | Preview | Limited support |
| STU3 (3.0.2) | Common resources | Legacy support |

## Custom Resources

Ignixa supports custom resource types defined via StructureDefinition:

```json
{
  "resourceType": "StructureDefinition",
  "id": "MyCustomResource",
  "type": "MyCustomResource",
  "baseDefinition": "http://hl7.org/fhir/StructureDefinition/DomainResource",
  "kind": "resource",
  ...
}
```

## Extensions

All standard FHIR extensions are supported. Custom extensions can be:

1. Defined in StructureDefinitions
2. Validated against profiles
3. Indexed for search

## Related Documentation

- [Search Parameters](/docs/server/fhir/search-parameters)
- [Validation](/docs/server/features/validation)
