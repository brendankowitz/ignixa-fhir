# Feature: Custom Audit Headers (`X-IGNIXA-AUDIT-*`, AHDS-compatible)

**Status**: Implemented (proposed ADR pending acceptance)
**Created**: 2026-10-06

## Problem Statement

Existing Azure Health Data Services (AHDS) callers attach their own audit fields with
`X-MS-AZUREFHIR-AUDIT-*` request headers, for example `X-MS-AZUREFHIR-AUDIT-OPERATIONID` and
`X-MS-AZUREFHIR-AUDIT-BUNDLEID`, and rely on the FHIR server's audit log recording them. AHDS and
microsoft/fhir-server do this; Ignixa did not. That blocked AHDS-compatible clients from moving to Ignixa
without losing their audit trail.

## Constraints

- Ignixa's own prefix is `X-IGNIXA-AUDIT-`. The AHDS prefix `X-MS-AZUREFHIR-AUDIT-` must keep working unchanged for AHDS-compatible clients.
- Match AHDS / fhir-server semantics: the limits of 10 headers and 2048 characters, a 431 response, and the OperationOutcome text.
- Header values are audit data. They go only to `IAuditLogger`, never to diagnostic logs or exception messages.
- Keep the sidecar contract backward-compatible.
- Bundle entries and bulk jobs must carry the caller's audit headers. This scope goes beyond fhir-server.

## Decision

See [adr-2610-custom-audit-headers.md](adr-2610-custom-audit-headers.md).
