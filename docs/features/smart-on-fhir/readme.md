# Feature: SMART on FHIR

**Status**: Proposed
**Created**: 2025-10-08

## Problem Statement

FHIR servers need to support SMART on FHIR authorization framework to enable secure access to FHIR resources by third-party applications. This includes:

- OAuth 2.0 / OIDC integration with multiple identity providers
- Support for EHR launch and standalone launch workflows
- Scope-based authorization for FHIR resources
- Patient and practitioner context

## Constraints

- Must support multiple identity providers (Entra ID, Azure B2C, Auth0, Okta, custom)
- Must comply with SMART App Launch Framework v2
- Must integrate with existing multi-tenancy architecture
- Must support both patient-facing and provider-facing applications
- Must provide secure token validation and refresh

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [identity-provider](investigations/identity-provider.md) | Viable | Provider-agnostic identity abstraction for SMART on FHIR v2 |
| [v2-implementation](investigations/v2-implementation.md) | Viable | SMART on FHIR v2 implementation architecture |

## Decision

*No ADR yet - investigations in progress*
