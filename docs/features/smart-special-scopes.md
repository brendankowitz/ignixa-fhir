# SMART Special Scopes Support

## Overview

The SmartScopeParser now supports parsing special (non-FHIR resource) SMART scopes including:
- OpenID Connect scopes
- Offline access scopes
- Launch context scopes

These scopes are defined in the SMART App Launch and OpenID Connect specifications.

## Supported Special Scopes

### OpenID Connect Scopes
- `openid` - Required for OpenID Connect authentication
- `profile` - Request access to user profile information
- `email` - Request access to user email address
- `fhirUser` - Request FHIR user reference (e.g., "Practitioner/123")

### Offline Access
- `offline_access` - Request refresh tokens for long-lived access

### Launch Context
- `launch` - Standalone launch (no specific context)
- `launch/patient` - EHR launch with patient context
- `launch/encounter` - EHR launch with encounter context

## Usage

### Parsing Special Scopes

```csharp
using Ignixa.Application.Features.Authorization.Smart;

// Parse a single special scope
var scope = SmartScopeParser.ParseSpecialScope("openid");
// Returns: SpecialScope("openid", SpecialScopeType.OpenIdConnect)

// Parse multiple special scopes from a space-separated string
var scopes = SmartScopeParser.ParseSpecialScopes("openid profile offline_access launch/patient");
// Returns: List of 4 SpecialScope objects

// Check if a scope is a special scope
bool isSpecial = SmartScopeParser.IsSpecialScope("openid"); // true
bool isFhir = SmartScopeParser.IsSpecialScope("patient/Observation.rs"); // false
```

### Working with SmartTokenClaims

```csharp
using Ignixa.Application.Features.Authorization.Smart;

// Create SmartTokenClaims from a scope string
var scopeString = "openid profile patient/Observation.rs offline_access launch/patient";
var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

// Check special scope properties
bool hasOpenId = claims.HasOpenIdScope; // true
bool hasOfflineAccess = claims.HasOfflineAccess; // true
string? launchContext = claims.LaunchContext; // "patient"

// Get all special scopes
IReadOnlyList<SpecialScope> specialScopes = claims.SpecialScopes;
// Returns: [openid, profile, offline_access, launch/patient]

// Extension methods for working with special scopes
var oidcScopes = claims.GetOpenIdConnectScopes(); // ["openid", "profile"]
var launchScopes = claims.GetLaunchScopes(); // ["launch/patient"]
bool hasProfile = claims.HasSpecialScope("profile"); // true
bool isEhrLaunch = claims.IsEhrLaunch(); // true
bool isStandalone = claims.IsStandaloneLaunch(); // false
```

### Parsing Mixed Scopes

The parser correctly separates FHIR resource scopes from special scopes:

```csharp
var scopeString = "openid patient/Observation.rs profile user/Patient.cruds offline_access";

// Parse FHIR resource scopes only
var resourceScopes = SmartScopeParser.ParseScopes(scopeString);
// Returns: 2 SmartScope objects (patient/Observation.rs, user/Patient.cruds)

// Parse special scopes only
var specialScopes = SmartScopeParser.ParseSpecialScopes(scopeString);
// Returns: 3 SpecialScope objects (openid, profile, offline_access)
```

## SmartTokenClaims Properties

The `SmartTokenClaims` record now includes these special scope-related properties:

```csharp
public record SmartTokenClaims
{
    // ... existing properties ...

    /// <summary>Parsed special scopes from the token.</summary>
    public IReadOnlyList<SpecialScope> SpecialScopes { get; init; }

    /// <summary>True if token includes openid scope.</summary>
    public bool HasOpenIdScope { get; init; }

    /// <summary>True if token includes offline_access scope.</summary>
    public bool HasOfflineAccess { get; init; }

    /// <summary>
    /// Launch context type: "patient", "encounter", or null for standalone.
    /// </summary>
    public string? LaunchContext { get; init; }
}
```

## SpecialScope Record

```csharp
/// <summary>
/// Represents a special (non-FHIR resource) SMART scope.
/// </summary>
public record SpecialScope(string Name, SpecialScopeType Type);

public enum SpecialScopeType
{
    OpenIdConnect,  // openid, profile, email, fhirUser
    OfflineAccess,  // offline_access
    Launch          // launch, launch/patient, launch/encounter
}
```

## Examples

### Example 1: EHR Launch with Patient Context

```csharp
var scopeString = "openid fhirUser launch/patient patient/*.rs";
var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

// Claims properties
claims.HasOpenIdScope;     // true
claims.HasOfflineAccess;   // false
claims.LaunchContext;      // "patient"
claims.IsEhrLaunch();      // true
claims.IsStandaloneLaunch(); // false

// Special scopes
claims.SpecialScopes.Count; // 3 (openid, fhirUser, launch/patient)
claims.GetOpenIdConnectScopes(); // ["openid", "fhirUser"]
claims.GetLaunchScopes();  // ["launch/patient"]
```

### Example 2: Standalone Launch with Refresh Token

```csharp
var scopeString = "openid profile email offline_access patient/Observation.rs user/Patient.cruds";
var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

// Claims properties
claims.HasOpenIdScope;     // true
claims.HasOfflineAccess;   // true
claims.LaunchContext;      // null (standalone launch)
claims.IsEhrLaunch();      // false
claims.IsStandaloneLaunch(); // true

// Special scopes
claims.SpecialScopes.Count; // 4 (openid, profile, email, offline_access)
claims.Scopes.Count;        // 2 FHIR resource scopes
```

### Example 3: System-Level Access (Backend Service)

```csharp
var scopeString = "system/*.cruds";
var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

// Claims properties
claims.HasOpenIdScope;     // false (backend services don't use OIDC)
claims.HasOfflineAccess;   // false
claims.LaunchContext;      // null
claims.IsStandaloneLaunch(); // true

// Scopes
claims.SpecialScopes.Count; // 0
claims.Scopes.Count;        // 1 (system/*.cruds)
```

## Testing

Comprehensive unit tests are available in:
- `SpecialScopeParserTests.cs` - Tests for parsing special scopes
- `SmartTokenClaimsExtensionsTests.cs` - Tests for extension methods

All tests pass (34 + 24 = 58 new tests, 159 total authorization tests).

## Backward Compatibility

This feature is fully backward compatible:
- Existing FHIR resource scope parsing unchanged
- Special scopes are tracked separately from resource scopes
- No breaking changes to existing APIs

## References

- [SMART App Launch Framework](https://hl7.org/fhir/smart-app-launch/)
- [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html)
- [OAuth 2.0 RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749)
