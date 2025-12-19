# Feature: FHIR Operations

Advanced FHIR operation implementations including clinical operations, terminology services, and document generation.

## Status

In Progress

## Overview

This feature implements FHIR operations that extend the base CRUD functionality with advanced capabilities for clinical workflows, terminology services, and document handling.

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [Advanced Operations](investigations/advanced-operations.md) | Proposed | 2025-11-18 | Implementation plan for $docref, $member-match, $submit-attachment, $questionnaire-package, and $document operations |
| [Support Analysis](investigations/support-analysis.md) | Complete | 2025-11-18 | Comprehensive analysis of FHIR operations across US Core, IPA, and Da Vinci IGs |
| [Patient $everything](investigations/patient-everything.md) | Investigation | 2025-11-18 | Optimized SQL-based implementation for Patient $everything operation |
| [IPS Generator](investigations/ips-generator.md) | Proposed | 2025-12-16 | International Patient Summary (IPS) generation implementation |
| [Narrative Architecture](investigations/narrative-architecture.md) | Proposed | 2025-01-16 | Narrative generator architecture analysis for XHTML narratives |
| [Narrative Library](investigations/narrative-library.md) | Merged | 2025-01-16 | Narrative generator library implementation with Scriban templates |
| [Mapping Language](investigations/mapping-language.md) | Investigation | 2025-11-18 | FHIR Mapping Language analysis and implementation approach |

## Key Components

- Operation registration infrastructure
- Multi-tenant operation routing
- DurableTask async operation support
- Terminology services integration
- Document generation and narrative rendering

## Related ADRs

- [ADR-2510: FHIR Patch Operations](../../adr/adr-2510-patch-operations.md)
- [ADR-2512: $member-match Operation](../../adr/adr-2512-member-match-operation.md)
- [ADR-2512: Narrative Generator Library](../../adr/adr-2512-narrative-generator.md)

## Related Features

- [Terminology Services](../terminology-services/readme.md)
- [Validation](../validation/readme.md)
- [StructureMap](../structuremap/readme.md)
