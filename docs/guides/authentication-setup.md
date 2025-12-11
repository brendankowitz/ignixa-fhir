# Authentication Setup Guide

Ignixa FHIR Server supports multiple authentication modes for different deployment scenarios.

## Quick Reference

| Scenario | Provider | Use Case |
|----------|----------|----------|
| Local development | `OpenIddict` (embedded) | No external IdP required |
| Azure App Service | `Entra` | Managed Identity + Azure AD |
| Self-hosted production | `OIDC` | Any OpenID Connect provider |
| Enterprise | `Okta` | Okta integration |

---

## 1. Development Mode (Embedded OpenIddict)

The embedded OpenIddict server provides a zero-configuration auth solution for development and self-hosted scenarios.

### Enable OpenIddict

Add to `appsettings.Development.json`:

```json
{
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
  },
  "Authentication": {
    "Provider": "OpenIddict",
    "OpenIddict": {
      "Issuer": "https://localhost:7058",
      "Audience": "fhir-api"
    }
  }
}
```

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

## 2. Azure App Service with Entra ID (Managed Identity)

For production deployments on Azure App Service using Microsoft Entra ID.

### Prerequisites

1. Azure App Service with System-assigned Managed Identity enabled
2. Microsoft Entra ID (Azure AD) tenant
3. App Registration for the FHIR API

### Step 1: Create App Registration

1. Go to Azure Portal > Microsoft Entra ID > App registrations
2. Click **New registration**
3. Name: `Ignixa FHIR API`
4. Supported account types: **Single tenant**
5. Click **Register**

### Step 2: Configure App Registration

**Expose an API:**

1. Go to **Expose an API**
2. Set Application ID URI: `api://<client-id>` or custom URI
3. Add scopes:
   - `system.read` - Read FHIR resources
   - `system.write` - Write FHIR resources
   - `patient.read` - Patient-context read access

**App Roles (for RBAC):**

1. Go to **App roles**
2. Create roles:
   - `Admin` - Full access
   - `Clinician` - Clinical data access
   - `ReadOnly` - Read-only access

### Step 3: Configure App Service

**appsettings.json:**

```json
{
  "Authentication": {
    "Provider": "Entra",
    "Entra": {
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "<your-tenant-id>",
      "Audience": "api://<your-client-id>"
    }
  },
  "Authorization": {
    "Enabled": true,
    "RequireAuthentication": true
  }
}
```

**Environment Variables (App Service Configuration):**

```
Authentication__Provider=Entra
Authentication__Entra__TenantId=<your-tenant-id>
Authentication__Entra__Audience=api://<your-client-id>
```

### Step 4: Configure Client Applications

For applications calling the FHIR API:

1. Create another App Registration for the client
2. Add **API permissions** > **My APIs** > select your FHIR API
3. Grant the required scopes

**Client Credentials Token Request:**

```bash
curl -X POST https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=<client-app-id>" \
  -d "client_secret=<client-secret>" \
  -d "scope=api://<fhir-api-id>/.default"
```

### Step 5: Managed Identity for Azure Resources

For the FHIR server to access Azure Blob Storage with Managed Identity:

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

## 3. Generic OpenID Connect (Any Provider)

For Keycloak, Auth0, or other OIDC-compliant providers.

```json
{
  "Authentication": {
    "Provider": "OIDC",
    "OIDC": {
      "Authority": "https://your-idp.example.com/realms/fhir",
      "Audience": "fhir-api"
    }
  }
}
```

---

## 4. Okta Integration

```json
{
  "Authentication": {
    "Provider": "Okta",
    "Okta": {
      "Domain": "your-org.okta.com",
      "Audience": "api://fhir"
    }
  }
}
```

---

## Configuration Reference

### Authentication Section

| Setting | Description | Default |
|---------|-------------|---------|
| `Provider` | Auth provider type | `JwtBearer` |
| `Authority` | Token issuer URL | - |
| `Audience` | Expected audience claim | - |

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
