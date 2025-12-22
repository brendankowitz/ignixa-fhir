---
sidebar_position: 1
title: Authentication
description: Authentication options for Ignixa FHIR Server
---

# Authentication

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

### Azure AD Configuration

```json
{
  "Authentication": {
    "Provider": "AzureAD",
    "Authority": "https://login.microsoftonline.com/{tenant-id}",
    "ClientId": "{client-id}",
    "ValidateIssuer": true,
    "ValidAudiences": ["api://ignixa-fhir"]
  }
}
```

### Generic OIDC

```json
{
  "Authentication": {
    "Provider": "OpenIdConnect",
    "Authority": "https://auth.example.org",
    "ClientId": "{client-id}",
    "ClientSecret": "{secret}",
    "ResponseType": "code",
    "Scopes": ["openid", "profile", "fhir"]
  }
}
```

## API Keys

For server-to-server communication:

### Configuration

```json
{
  "Authentication": {
    "ApiKeys": {
      "Enabled": true,
      "HeaderName": "X-API-Key",
      "Keys": {
        "integration-service": {
          "Hash": "sha256:...",
          "Scopes": ["system/*.read", "system/*.write"],
          "Tenants": [1, 2]
        }
      }
    }
  }
}
```

### Usage

```bash
curl -H "X-API-Key: your-api-key" http://localhost:8080/Patient
```

## JWT Configuration

### Token Validation

```json
{
  "Authentication": {
    "Jwt": {
      "ValidateIssuer": true,
      "ValidIssuers": ["https://auth.example.org"],
      "ValidateAudience": true,
      "ValidAudiences": ["api://ignixa-fhir"],
      "ValidateLifetime": true,
      "ClockSkew": "00:05:00"
    }
  }
}
```

### Custom Claims

Map claims to FHIR context:

```json
{
  "Authentication": {
    "ClaimMappings": {
      "PatientId": "patient_id",
      "TenantId": "tenant",
      "Scopes": "scope"
    }
  }
}
```

## Anonymous Access

For public read-only access:

```json
{
  "Authentication": {
    "AllowAnonymous": {
      "Enabled": true,
      "AllowedPaths": ["/metadata", "/.well-known/*"],
      "AllowedMethods": ["GET"]
    }
  }
}
```

## Request Headers

### Required Headers

| Header | Description |
|--------|-------------|
| `Authorization` | Bearer token |
| `Content-Type` | FHIR content type |

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
- [ADR: Authorization](/docs/adr/adr-2501-authorization)
