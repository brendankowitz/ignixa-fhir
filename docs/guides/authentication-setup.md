# Authentication Setup Guide

Ignixa FHIR Server supports OAuth2/OIDC authentication with automatic endpoint discovery.

## Simplified Configuration (Recommended)

**Any OIDC-compliant OAuth2 server works with just the Authority URL:**

```json
{
  "Authentication": {
    "Authority": "https://your-oauth-server.example.com",
    "Audience": "fhir-api"
  }
}
```

The system automatically discovers OAuth2 endpoints via `/.well-known/openid-configuration`.

## Quick Reference

| Scenario | Authority URL Example |
|----------|----------------------|
| Local development (OpenIddict) | `https://localhost:7058` |
| Azure Entra ID | `https://login.microsoftonline.com/{tenant-id}/v2.0` |
| Okta | `https://your-org.okta.com` |
| Auth0 | `https://your-tenant.auth0.com` |
| Keycloak | `https://keycloak.example.com/realms/{realm}` |
| Any OIDC provider | Set to your OAuth2 server's base URL |

---

## 1. Development Mode (Embedded OpenIddict)

The embedded OpenIddict server provides a zero-configuration auth solution for development and self-hosted scenarios.

### Enable OpenIddict

Add to `appsettings.Development.json`:

```json
{
  "Authentication": {
    "Authority": "https://localhost:7058",
    "Audience": "fhir-api"
  },
  "OpenIddict": {
    "Enabled": true,
    "UseInMemoryStorage": true,
    "DisableHttpsRequirement": true,
    "DisableAccessTokenEncryption": true,
    "ClientApplications": [
      {
        "ClientId": "fhir-admin-client",
        "ClientSecret": "dev-secret",
        "DisplayName": "Admin Client",
        "GrantTypes": ["client_credentials"],
        "Scopes": ["system/*.cruds"],
        "Roles": ["Admin"]
      },
      {
        "ClientId": "smart-app",
        "ClientSecret": "smart-secret",
        "DisplayName": "SMART App",
        "RedirectUris": ["http://localhost:3000/callback"],
        "GrantTypes": ["authorization_code", "refresh_token"],
        "Scopes": ["openid", "profile", "fhirUser", "launch", "patient/*.read"],
        "IsPublicClient": false
      }
    ],
    "DevelopmentUsers": [
      {
        "Username": "admin",
        "Password": "admin123",
        "FhirUser": "Practitioner/admin",
        "Roles": ["Admin"]
      },
      {
        "Username": "doctor",
        "Password": "doctor123",
        "FhirUser": "Practitioner/doctor1",
        "Roles": ["Clinician"]
      }
    ]
  }
}
```

**Key point**: Setting `Authentication:Authority` enables automatic OIDC discovery. The system fetches OAuth2 endpoints from `https://localhost:7058/.well-known/openid-configuration`.

### Get a Token

**Client Credentials Flow** (machine-to-machine):

```bash
curl -X POST https://localhost:7058/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=fhir-admin-client" \
  -d "client_secret=dev-secret" \
  -d "scope=system/*.cruds"
```

**Password Flow** (development users):

```bash
curl -X POST https://localhost:7058/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password" \
  -d "client_id=postman-client" \
  -d "username=admin" \
  -d "password=admin123" \
  -d "scope=user/*.read"
```

### Use the Token

```bash
curl https://localhost:7058/Patient \
  -H "Authorization: Bearer <access_token>"
```

### SMART on FHIR Scopes

OpenIddict supports full SMART v2 scope syntax:

| Scope Pattern | Description |
|---------------|-------------|
| `system/*.cruds` | Full system access (all resources, all operations) |
| `system/Patient.rs` | System read + search on Patient |
| `patient/Observation.r` | Patient-context read on Observation |
| `user/MedicationRequest.cruds` | User-context full access to MedicationRequest |
| `launch` | EHR launch context |
| `fhirUser` | Include fhirUser claim |
| `offline_access` | Refresh token support |

---

## 2. Production with Any OIDC Provider

**Simple configuration for any OAuth2/OIDC server:**

```json
{
  "Authentication": {
    "Authority": "https://your-oauth-server.example.com",
    "Audience": "your-api-identifier"
  },
  "Authorization": {
    "Enabled": true,
    "RequireAuthentication": true
  }
}
```

The system automatically:
- Fetches OIDC metadata from `{Authority}/.well-known/openid-configuration`
- Configures JWT validation using the discovered `jwks_uri`
- Validates tokens against the `Authority` (issuer) and `Audience`

### Provider-Specific Examples

**Azure Entra ID (Azure AD):**

```json
{
  "Authentication": {
    "Authority": "https://login.microsoftonline.com/{tenant-id}/v2.0",
    "Audience": "api://{client-id}"
  }
}
```

**Okta:**

```json
{
  "Authentication": {
    "Authority": "https://your-org.okta.com",
    "Audience": "api://fhir"
  }
}
```

**Auth0:**

```json
{
  "Authentication": {
    "Authority": "https://your-tenant.auth0.com",
    "Audience": "https://fhir-api.example.com"
  }
}
```

**Keycloak:**

```json
{
  "Authentication": {
    "Authority": "https://keycloak.example.com/realms/fhir",
    "Audience": "fhir-api"
  }
}
```

### Azure Managed Identity for Resources

For the FHIR server to access Azure services with Managed Identity:

```json
{
  "BlobStorage": {
    "Provider": "Azure",
    "UseManagedIdentity": true,
    "StorageAccountUri": "https://youraccount.blob.core.windows.net",
    "ContainerName": "fhirstorage"
  },
  "DurableTask": {
    "Provider": "AzureStorage",
    "AzureStorage": {
      "UseManagedIdentity": true,
      "StorageAccountName": "yourtaskstorage",
      "TaskHubName": "ignixa"
    }
  }
}
```

Ensure the App Service Managed Identity has **Storage Blob Data Contributor** role on the storage accounts.

---

## How OIDC Discovery Works

When you set `Authentication:Authority`, the system:

1. **Fetches OIDC metadata** from `{Authority}/.well-known/openid-configuration`
2. **Discovers OAuth2 endpoints** automatically:
   - `authorization_endpoint` - For authorization code flow
   - `token_endpoint` - For token requests
   - `jwks_uri` - For JWT signature validation
   - `introspection_endpoint` - For token introspection (optional)
   - `revocation_endpoint` - For token revocation (optional)
3. **Configures JWT validation** using the discovered signing keys
4. **Validates tokens** against the issuer and audience

**No manual endpoint configuration needed** - just set the Authority URL.

## Configuration Reference

### Authentication Section

| Setting | Description | Default | Required |
|---------|-------------|---------|----------|
| `Authority` | **Primary config**: OAuth2 server base URL. Used for OIDC discovery. | `null` | **Yes** |
| `Audience` | Expected `aud` claim in JWT tokens | `null` | **Yes** |
| `Provider` | Legacy provider type (optional - use Authority instead) | `JwtBearer` | No |

### Authorization Section

| Setting | Description | Default |
|---------|-------------|---------|
| `Enabled` | Enable auth middleware | `true` |
| `RequireAuthentication` | Require valid token | `true` |
| `EnforceTenantIsolation` | Validate tenant access | `true` |
| `EnforceCapabilities` | Check RBAC permissions | `true` |

### OpenIddict Section

| Setting | Description | Default |
|---------|-------------|---------|
| `Enabled` | Enable embedded server | `false` |
| `UseInMemoryStorage` | Use in-memory token store | `true` |
| `DisableHttpsRequirement` | Allow HTTP (dev only) | `false` |
| `DisableAccessTokenEncryption` | Plain JWT tokens | `true` |
| `ClientApplications` | Pre-registered clients | `[]` |
| `DevelopmentUsers` | Test users (password flow) | `[]` |

---

## SMART on FHIR Configuration

Configure the `.well-known/smart-configuration` endpoint:

```json
{
  "Authorization": {
    "SmartOnFhir": {
      "EnableSmartConfiguration": true,
      "EnableV1ScopeCompatibility": false,
      "AuthorizeUrl": "https://login.microsoftonline.com/<tenant>/oauth2/v2.0/authorize",
      "TokenUrl": "https://login.microsoftonline.com/<tenant>/oauth2/v2.0/token",
      "SupportedCapabilities": [
        "launch-ehr",
        "launch-standalone",
        "client-public",
        "client-confidential-symmetric",
        "sso-openid-connect",
        "permission-patient",
        "permission-user"
      ]
    }
  }
}
```

---

## Troubleshooting

### Token validation fails

1. Check `Authority` matches the token issuer exactly
2. Verify `Audience` matches the `aud` claim in the token
3. Enable debug logging: `"Microsoft.AspNetCore.Authentication": "Debug"`

### 401 Unauthorized with valid token

1. Check token hasn't expired
2. Verify required scopes are present
3. Check RBAC role assignments in `Authorization:DefaultRoles`

### OpenIddict endpoints return 405

1. Ensure `MapIgnixaOpenIddictEndpoints()` is called **before** `MapIgnixaEndpoints()`
2. Check `OpenIddict:Enabled` is `true`

### Managed Identity not working

1. Verify System-assigned identity is enabled on App Service
2. Check RBAC roles on target resources (Storage, SQL, etc.)
3. Use `Azure.Identity` debug logging to trace token acquisition

---

## Security Recommendations

1. **Production**: Always use `RequireAuthentication: true`
2. **HTTPS**: Never disable HTTPS requirement in production
3. **Secrets**: Use Azure Key Vault or environment variables for secrets
4. **Token encryption**: Enable access token encryption for sensitive deployments
5. **Audit logging**: Enable `IAuditLogger` for compliance tracking
