---
sidebar_position: 1
title: Authentication
description: Authentication options for Ignixa FHIR Server
---

# Authentication

:::caution Under Development
Authentication features are under active development. Configuration options and APIs may change.
:::

Ignixa supports multiple authentication mechanisms for securing FHIR endpoints.

## Overview

| Method | Use Case | Specification |
|--------|----------|---------------|
| SMART on FHIR | Healthcare apps | HL7 SMART |
| OAuth 2.0 / OIDC | Enterprise SSO | RFC 6749, OIDC |
| API Keys | Server-to-server | Custom |
| mTLS | High security | RFC 5246 |

## SMART on FHIR

SMART on FHIR is the recommended authentication method for healthcare applications.

### Configuration

```json
{
  "SmartOnFhir": {
    "Enabled": true,
    "Authority": "https://login.example.org",
    "ClientId": "ignixa-fhir",
    "Scopes": {
      "Launch": ["launch", "launch/patient"],
      "Clinical": ["patient/*.read", "patient/*.write"],
      "User": ["user/*.read", "user/*.write"],
      "System": ["system/*.read", "system/*.write"]
    }
  }
}
```

### Well-Known Endpoints

SMART on FHIR discovery:

```bash
GET /.well-known/smart-configuration
```

Response:

```json
{
  "authorization_endpoint": "https://login.example.org/authorize",
  "token_endpoint": "https://login.example.org/token",
  "capabilities": [
    "launch-ehr",
    "launch-standalone",
    "client-public",
    "client-confidential-symmetric",
    "context-ehr-patient",
    "sso-openid-connect"
  ],
  "scopes_supported": [
    "openid",
    "profile",
    "launch",
    "patient/*.read",
    "patient/*.write"
  ]
}
```

### Scopes

SMART scopes control access:

| Scope | Access |
|-------|--------|
| `patient/*.read` | Read patient compartment |
| `patient/*.write` | Write patient compartment |
| `user/*.read` | User-level read access |
| `user/*.write` | User-level write access |
| `system/*.read` | System-level read (backend) |
| `system/*.write` | System-level write (backend) |

### Launch Context

For EHR-launched apps:

```json
{
  "patient": "Patient/123",
  "encounter": "Encounter/456",
  "need_patient_banner": true,
  "smart_style_url": "https://ehr.example.org/smart-style.json"
}
```

## OAuth 2.0 / OpenID Connect

Ignixa supports any standard OIDC-compliant identity provider including Azure AD, Okta, Auth0, Keycloak, and others.

### Configuration

Configure any OIDC provider in `appsettings.json`:

```json
{
  "Authentication": {
    "Authority": "https://login.microsoftonline.com/{tenant-id}",
    "ClientId": "{client-id}"
  }
}
```

The implementation automatically handles:
- OIDC discovery (`.well-known/openid-configuration`)
- JWT signature validation
- Token expiration checks
- Issuer validation

### Supported Providers

- **Azure AD** - Authority: `https://login.microsoftonline.com/{tenant-id}`
- **Okta** - Authority: `https://{domain}.okta.com`
- **Auth0** - Authority: `https://{domain}.auth0.com`
- **Keycloak** - Authority: `https://keycloak.example.org/auth/realms/{realm}`
- **Generic OIDC** - Any OIDC-compliant provider

### Token Validation

Configurable JWT validation:

```json
{
  "Authentication": {
    "Authority": "https://auth.example.org",
    "ValidateIssuer": true,
    "ValidateAudience": true,
    "ValidAudiences": ["api://ignixa-fhir"],
    "ValidateLifetime": true,
    "ClockSkew": "00:05:00"
  }
}
```

## Request Headers

### Required Headers

| Header | Description |
|--------|-------------|
| `Authorization` | Bearer token (required when `Authorization:RequireAuthentication` is true) |

### Example Request

```bash
curl -X GET http://localhost:8080/Patient \
  -H "Authorization: Bearer eyJhbGciOiJS..." \
  -H "Accept: application/fhir+json"
```

## Error Responses

### 401 Unauthorized

```json
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "login",
    "diagnostics": "Authentication required"
  }]
}
```

### 403 Forbidden

```json
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "forbidden",
    "diagnostics": "Insufficient scope: requires patient/*.read"
  }]
}
```

## Related Documentation

- [Authorization](/docs/server/security/authorization)
- [ADR: Authorization](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2501-authorization.md)
