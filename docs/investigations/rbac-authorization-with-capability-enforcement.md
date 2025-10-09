# Investigation: RBAC Authorization with Capability Statement Enforcement

## Problem Statement

FHIR Server v2 needs a comprehensive authorization system that:

1. **RBAC (Role-Based Access Control)**: Traditional role-based permissions for administrative operations
2. **SMART on FHIR Scopes**: OAuth 2.0 scope-based authorization for API access (patient/*.read, user/*.write, etc.)
3. **Capability Statement Enforcement**: Server MUST respect its own CapabilityStatement - if an interaction isn't advertised, it should be rejected
4. **Multi-Tenant Isolation**: Ensure users can only access resources in their tenant
5. **Performance**: Authorization checks on every API call must be fast (<1ms)

**Key Insight**: The CapabilityStatement is not just documentation - it's a **contract** the server must enforce.

---

## Legacy Authorization Patterns

### Problem: No Capability Enforcement

Legacy FHIR server has OAuth/SMART authorization but **doesn't enforce CapabilityStatement**:

1. CapabilityStatement says "AuditEvent doesn't support update"
2. But if you send `PUT /AuditEvent/123` with valid auth, it works anyway!
3. This breaks FHIR conformance - server claims one thing, does another

### Legacy SMART Authorization (From Investigation)

From `smart-on-fhir-v2-implementation.md`:

```csharp
public class SmartAuthorizationHandler : AuthorizationHandler<SmartRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SmartRequirement requirement)
    {
        // 1. Extract token from Authorization header
        var accessToken = GetBearerToken(httpContext);

        // 2. Validate token and get claims
        var tokenClaims = await _tokenService.ValidateTokenAsync(accessToken);

        // 3. Parse scopes
        var scopes = tokenClaims.Scopes
            .Select(SmartScopeParser.ParseScope)
            .ToList();

        // 4. Check if scopes allow this operation
        var resourceType = GetResourceTypeFromRoute(httpContext);
        var interaction = GetInteractionFromMethod(httpContext.Request.Method);

        if (!scopes.Any(s => s.MatchesResource(resourceType) && s.MatchesInteraction(interaction)))
        {
            context.Fail();
            return;
        }

        context.Succeed(requirement);
    }
}
```

**Problem**: Only checks scopes, doesn't verify CapabilityStatement.

---

## V2 Solution: Layered Authorization Architecture

### Authorization Layers

| Layer | Checks | Example | Performance |
|-------|--------|---------|-------------|
| **1. Authentication** | Is token valid? | JWT signature, expiration | <0.5ms (cached) |
| **2. RBAC** | Does role allow this? | Admin can delete users | <0.1ms (in-memory) |
| **3. SMART Scopes** | Do scopes allow this? | patient/Observation.read | <0.5ms (parsed once) |
| **4. Capability Enforcement** | Does server support this? | Check CapabilityStatement | <0.5ms (cached) |
| **5. Data Filtering** | Filter by patient/compartment | Only return patient's data | Varies (query-level) |

**Total authorization overhead**: ~1.5ms per request (acceptable)

---

## Core Abstractions

### 1. Authorization Context

```csharp
// Per-request authorization context
public record FhirAuthorizationContext
{
    // Who is making the request?
    public required string? UserId { get; init; }
    public required string? TenantId { get; init; }
    public required string[]? Roles { get; init; }

    // SMART scopes (if authenticated via OAuth)
    public SmartAuthorizationContext? SmartContext { get; init; }

    // What are they trying to do?
    public required FhirInteraction Interaction { get; init; }
    public required string? ResourceType { get; init; }
    public required string? ResourceId { get; init; }

    // Request details
    public required HttpContext HttpContext { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public record SmartAuthorizationContext
{
    public required SmartTokenClaims TokenClaims { get; init; }
    public required SmartScope[] Scopes { get; init; }
    public string? PatientContext { get; init; }
    public string? EncounterContext { get; init; }
    public string? UserContext { get; init; }
}

public enum FhirInteraction
{
    Read,
    VRead,
    Update,
    Patch,
    Delete,
    HistoryInstance,
    HistoryType,
    Create,
    SearchType,
    SearchSystem,
    Capabilities,
    Batch,
    Transaction,
    OperationInstance,
    OperationType,
    OperationSystem
}
```

### 2. Authorization Pipeline

```csharp
// Main authorization service
public interface IFhirAuthorizationService
{
    /// <summary>
    /// Authorize a FHIR interaction
    /// </summary>
    ValueTask<AuthorizationResult> AuthorizeAsync(
        FhirAuthorizationContext context,
        CancellationToken ct = default);
}

public record AuthorizationResult
{
    public required bool Allowed { get; init; }
    public string? DenialReason { get; init; }
    public FhirAuthorizationFilter? Filter { get; init; } // Data filtering rules
}

public record FhirAuthorizationFilter
{
    // Patient compartment filtering (for patient/*.read scopes)
    public string? PatientFilter { get; init; }

    // Custom search parameter filters
    public Dictionary<string, string>? SearchFilters { get; init; }
}

// Pipeline implementation
public class FhirAuthorizationService : IFhirAuthorizationService
{
    private readonly IEnumerable<IAuthorizationHandler> _handlers;

    public async ValueTask<AuthorizationResult> AuthorizeAsync(
        FhirAuthorizationContext context,
        CancellationToken ct)
    {
        // Execute handlers in order (fail-fast)
        foreach (var handler in _handlers)
        {
            var result = await handler.HandleAsync(context, ct);

            if (!result.Allowed)
            {
                return result; // ← Deny immediately
            }

            // Merge filters from each handler
            context = context with
            {
                // Accumulate data filters
            };
        }

        return new AuthorizationResult { Allowed = true };
    }
}
```

### 3. Authorization Handlers

```csharp
public interface IAuthorizationHandler
{
    /// <summary>
    /// Priority (lower = earlier in pipeline)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Handle authorization check
    /// </summary>
    ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken ct);
}
```

---

## Authorization Handler Implementations

### Handler 1: Authentication Check (Priority: 10)

```csharp
public class AuthenticationHandler : IAuthorizationHandler
{
    public int Priority => 10; // Run first

    public async ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken ct)
    {
        // Allow unauthenticated access to /metadata
        if (context.Interaction == FhirInteraction.Capabilities)
        {
            return new AuthorizationResult { Allowed = true };
        }

        // Require authentication for all other endpoints
        if (context.UserId == null && context.SmartContext == null)
        {
            return new AuthorizationResult
            {
                Allowed = false,
                DenialReason = "Authentication required"
            };
        }

        return new AuthorizationResult { Allowed = true };
    }
}
```

### Handler 2: Tenant Isolation (Priority: 20)

```csharp
public class TenantIsolationHandler : IAuthorizationHandler
{
    public int Priority => 20;

    public async ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken ct)
    {
        // System admin can access all tenants
        if (context.Roles?.Contains("SystemAdmin") == true)
        {
            return new AuthorizationResult { Allowed = true };
        }

        // User must have a tenant
        if (context.TenantId == null)
        {
            return new AuthorizationResult
            {
                Allowed = false,
                DenialReason = "No tenant context"
            };
        }

        // Tenant must match request
        var requestTenant = ExtractTenantFromRoute(context.HttpContext);
        if (requestTenant != null && requestTenant != context.TenantId)
        {
            return new AuthorizationResult
            {
                Allowed = false,
                DenialReason = $"Access denied to tenant {requestTenant}"
            };
        }

        return new AuthorizationResult { Allowed = true };
    }
}
```

### Handler 3: RBAC (Priority: 30)

```csharp
public class RbacAuthorizationHandler : IAuthorizationHandler
{
    private readonly IRolePermissionStore _permissionStore;

    public int Priority => 30;

    public async ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken ct)
    {
        // Skip if using SMART (SMART scopes take precedence)
        if (context.SmartContext != null)
        {
            return new AuthorizationResult { Allowed = true };
        }

        // Skip if no roles
        if (context.Roles == null || context.Roles.Length == 0)
        {
            return new AuthorizationResult { Allowed = true }; // Let next handler decide
        }

        // Check role permissions
        var permissions = await _permissionStore.GetPermissionsAsync(
            context.TenantId!,
            context.Roles,
            ct);

        var required = BuildRequiredPermission(context);

        if (!permissions.Any(p => p.Matches(required)))
        {
            return new AuthorizationResult
            {
                Allowed = false,
                DenialReason = $"Role does not have permission: {required}"
            };
        }

        return new AuthorizationResult { Allowed = true };
    }

    private FhirPermission BuildRequiredPermission(FhirAuthorizationContext context)
    {
        return new FhirPermission(
            ResourceType: context.ResourceType ?? "*",
            Interaction: context.Interaction.ToString().ToLowerInvariant()
        );
    }
}

public record FhirPermission(string ResourceType, string Interaction)
{
    public bool Matches(FhirPermission required)
    {
        var resourceMatch = ResourceType == "*" || ResourceType == required.ResourceType;
        var interactionMatch = Interaction == "*" || Interaction == required.Interaction;

        return resourceMatch && interactionMatch;
    }
}

// Role permission store
public interface IRolePermissionStore
{
    ValueTask<FhirPermission[]> GetPermissionsAsync(
        string tenantId,
        string[] roles,
        CancellationToken ct);
}
```

### Handler 4: SMART Scope Authorization (Priority: 40)

```csharp
public class SmartScopeAuthorizationHandler : IAuthorizationHandler
{
    public int Priority => 40;

    public async ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken ct)
    {
        // Skip if not SMART authenticated
        if (context.SmartContext == null)
        {
            return new AuthorizationResult { Allowed = true };
        }

        var scopes = context.SmartContext.Scopes;
        var resourceType = context.ResourceType;
        var interaction = MapInteractionToSmart(context.Interaction);

        // Check if any scope matches
        var matchingScope = scopes.FirstOrDefault(scope =>
            scope.MatchesResource(resourceType ?? "*") &&
            scope.MatchesInteraction(interaction));

        if (matchingScope == null)
        {
            return new AuthorizationResult
            {
                Allowed = false,
                DenialReason = $"No scope grants {interaction} access to {resourceType}"
            };
        }

        // Build data filter for patient-scoped requests
        FhirAuthorizationFilter? filter = null;

        if (matchingScope.Type == SmartScopeType.Patient)
        {
            var patientId = context.SmartContext.PatientContext;
            if (patientId == null)
            {
                return new AuthorizationResult
                {
                    Allowed = false,
                    DenialReason = "Patient scope requires patient context"
                };
            }

            filter = new FhirAuthorizationFilter
            {
                PatientFilter = patientId,
                SearchFilters = new Dictionary<string, string>
                {
                    ["patient"] = patientId // ← Add to search automatically
                }
            };
        }

        return new AuthorizationResult
        {
            Allowed = true,
            Filter = filter
        };
    }

    private string MapInteractionToSmart(FhirInteraction interaction)
    {
        return interaction switch
        {
            FhirInteraction.Read or FhirInteraction.VRead or FhirInteraction.SearchType or FhirInteraction.SearchSystem => "read",
            FhirInteraction.Create => "create",
            FhirInteraction.Update or FhirInteraction.Patch => "update",
            FhirInteraction.Delete => "delete",
            _ => "*"
        };
    }
}
```

### Handler 5: **Capability Statement Enforcement** (Priority: 50) ⭐

This is the KEY handler that enforces CapabilityStatement!

```csharp
public class CapabilityEnforcementHandler : IAuthorizationHandler
{
    private readonly CapabilityStatementService _capabilityService;
    private readonly ITenantContextResolver _tenantResolver;
    private readonly IFhirVersionResolver _versionResolver;

    public int Priority => 50; // Run after scope checks

    public async ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken ct)
    {
        // Always allow /metadata
        if (context.Interaction == FhirInteraction.Capabilities)
        {
            return new AuthorizationResult { Allowed = true };
        }

        // Get capability statement for this tenant/version
        var capabilityContext = new CapabilityContext(
            FhirVersion: _versionResolver.Resolve(context.HttpContext),
            TenantId: context.TenantId
        );

        var statement = await _capabilityService.GetCapabilityStatementAsync(capabilityContext, ct);

        // Check if interaction is supported
        var isSupported = await CheckInteractionSupportedAsync(
            statement,
            context.ResourceType,
            context.Interaction,
            ct);

        if (!isSupported)
        {
            return new AuthorizationResult
            {
                Allowed = false,
                DenialReason = $"Server does not support {context.Interaction} on {context.ResourceType} (per CapabilityStatement)"
            };
        }

        return new AuthorizationResult { Allowed = true };
    }

    private async ValueTask<bool> CheckInteractionSupportedAsync(
        ITypedElement statement,
        string? resourceType,
        FhirInteraction interaction,
        CancellationToken ct)
    {
        // System-level interactions
        if (resourceType == null)
        {
            return await CheckSystemInteractionAsync(statement, interaction, ct);
        }

        // Resource-level interactions
        return await CheckResourceInteractionAsync(statement, resourceType, interaction, ct);
    }

    private async ValueTask<bool> CheckResourceInteractionAsync(
        ITypedElement statement,
        string resourceType,
        FhirInteraction interaction,
        CancellationToken ct)
    {
        // Navigate to rest.resource where type = resourceType
        var resourceComponent = statement
            .Select("rest.resource.where(type = '" + resourceType + "')").FirstOrDefault();

        if (resourceComponent == null)
        {
            return false; // Resource not listed in capability statement
        }

        // Check interaction
        var interactionCode = MapInteractionToFhir(interaction);
        var hasInteraction = resourceComponent
            .Select($"interaction.where(code = '{interactionCode}')").Any();

        return hasInteraction;
    }

    private async ValueTask<bool> CheckSystemInteractionAsync(
        ITypedElement statement,
        FhirInteraction interaction,
        CancellationToken ct)
    {
        var interactionCode = MapInteractionToFhir(interaction);

        // Check rest.interaction for system-level operations
        var hasInteraction = statement
            .Select($"rest.interaction.where(code = '{interactionCode}')").Any();

        return hasInteraction;
    }

    private string MapInteractionToFhir(FhirInteraction interaction)
    {
        return interaction switch
        {
            FhirInteraction.Read => "read",
            FhirInteraction.VRead => "vread",
            FhirInteraction.Update => "update",
            FhirInteraction.Patch => "patch",
            FhirInteraction.Delete => "delete",
            FhirInteraction.HistoryInstance => "history-instance",
            FhirInteraction.HistoryType => "history-type",
            FhirInteraction.Create => "create",
            FhirInteraction.SearchType => "search-type",
            FhirInteraction.SearchSystem => "search-system",
            FhirInteraction.Batch => "batch",
            FhirInteraction.Transaction => "transaction",
            _ => throw new ArgumentOutOfRangeException(nameof(interaction))
        };
    }
}
```

**Key Innovation**: Uses FHIRPath expressions to query CapabilityStatement at runtime!

---

## Performance Optimization: Capability Cache

Problem: Parsing CapabilityStatement with FHIRPath on every request is slow.

Solution: Build interaction lookup cache when CapabilityStatement is generated:

```csharp
public class CapabilityInteractionCache
{
    // Key: "{resourceType}:{interaction}", Value: true if supported
    private readonly ConcurrentDictionary<string, bool> _cache = new();

    public async Task BuildCacheAsync(ITypedElement statement, CancellationToken ct)
    {
        _cache.Clear();

        // Extract all resource interactions
        var resources = statement.Select("rest.resource");

        foreach (var resource in resources)
        {
            var resourceType = resource.Select("type").First().Value as string;
            var interactions = resource.Select("interaction.code").Select(e => e.Value as string);

            foreach (var interaction in interactions)
            {
                _cache[$"{resourceType}:{interaction}"] = true;
            }
        }

        // Extract system interactions
        var systemInteractions = statement.Select("rest.interaction.code").Select(e => e.Value as string);

        foreach (var interaction in systemInteractions)
        {
            _cache[$"_system:{interaction}"] = true;
        }
    }

    public bool IsSupported(string? resourceType, string interactionCode)
    {
        var key = resourceType == null
            ? $"_system:{interactionCode}"
            : $"{resourceType}:{interactionCode}";

        return _cache.TryGetValue(key, out var supported) && supported;
    }
}

// Modified CapabilityStatementService
public class CapabilityStatementService
{
    private readonly ConcurrentDictionary<string, CapabilityInteractionCache> _interactionCaches = new();

    public async ValueTask<ITypedElement> GetCapabilityStatementAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        // ... build statement ...

        // Build interaction cache
        var cacheKey = BuildCacheKey(context);
        var interactionCache = new CapabilityInteractionCache();
        await interactionCache.BuildCacheAsync(statement, ct);

        _interactionCaches[cacheKey] = interactionCache;

        return statement;
    }

    public bool IsInteractionSupported(
        CapabilityContext context,
        string? resourceType,
        string interactionCode)
    {
        var cacheKey = BuildCacheKey(context);

        if (_interactionCaches.TryGetValue(cacheKey, out var cache))
        {
            return cache.IsSupported(resourceType, interactionCode);
        }

        return false; // If no capability statement cached, deny
    }
}

// Optimized handler
public class CapabilityEnforcementHandler : IAuthorizationHandler
{
    private readonly CapabilityStatementService _capabilityService;

    public async ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken ct)
    {
        if (context.Interaction == FhirInteraction.Capabilities)
        {
            return new AuthorizationResult { Allowed = true };
        }

        var capabilityContext = new CapabilityContext(
            FhirVersion: _versionResolver.Resolve(context.HttpContext),
            TenantId: context.TenantId
        );

        var interactionCode = MapInteractionToFhir(context.Interaction);

        // O(1) lookup instead of FHIRPath query!
        var isSupported = _capabilityService.IsInteractionSupported(
            capabilityContext,
            context.ResourceType,
            interactionCode);

        if (!isSupported)
        {
            return new AuthorizationResult
            {
                Allowed = false,
                DenialReason = $"Server does not support {context.Interaction} on {context.ResourceType}"
            };
        }

        return new AuthorizationResult { Allowed = true };
    }
}
```

**Performance**: <0.1ms (dictionary lookup) instead of ~5ms (FHIRPath evaluation)

---

## ASP.NET Core Integration

### Minimal API Middleware

```csharp
public class FhirAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IFhirAuthorizationService _authzService;

    public async Task InvokeAsync(HttpContext httpContext)
    {
        // Skip /metadata
        if (httpContext.Request.Path == "/metadata")
        {
            await _next(httpContext);
            return;
        }

        // Build authorization context
        var context = await BuildAuthorizationContextAsync(httpContext);

        // Authorize
        var result = await _authzService.AuthorizeAsync(context);

        if (!result.Allowed)
        {
            httpContext.Response.StatusCode = 403;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "forbidden",
                        diagnostics = result.DenialReason
                    }
                }
            });
            return;
        }

        // Store filter in HttpContext for query layer to use
        if (result.Filter != null)
        {
            httpContext.Items["FhirAuthorizationFilter"] = result.Filter;
        }

        await _next(httpContext);
    }

    private async Task<FhirAuthorizationContext> BuildAuthorizationContextAsync(HttpContext httpContext)
    {
        // Extract user/tenant from claims
        var userId = httpContext.User.FindFirst("sub")?.Value;
        var tenantId = httpContext.User.FindFirst("tenant_id")?.Value;
        var roles = httpContext.User.FindAll("role").Select(c => c.Value).ToArray();

        // Extract SMART context if present
        SmartAuthorizationContext? smartContext = null;
        if (httpContext.Items.TryGetValue("SmartTokenClaims", out var claims))
        {
            var tokenClaims = (SmartTokenClaims)claims!;
            smartContext = new SmartAuthorizationContext
            {
                TokenClaims = tokenClaims,
                Scopes = tokenClaims.Scopes.Select(SmartScopeParser.ParseScope).ToArray(),
                PatientContext = tokenClaims.Patient,
                // ...
            };
        }

        // Extract FHIR interaction from route
        var (interaction, resourceType, resourceId) = ParseRoute(httpContext);

        return new FhirAuthorizationContext
        {
            UserId = userId,
            TenantId = tenantId,
            Roles = roles,
            SmartContext = smartContext,
            Interaction = interaction,
            ResourceType = resourceType,
            ResourceId = resourceId,
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private (FhirInteraction interaction, string? resourceType, string? resourceId) ParseRoute(HttpContext ctx)
    {
        var method = ctx.Request.Method;
        var path = ctx.Request.Path.Value?.TrimStart('/').Split('/');

        if (path == null || path.Length == 0)
        {
            return (FhirInteraction.SearchSystem, null, null);
        }

        var resourceType = path[0];

        return (method, path.Length) switch
        {
            ("GET", 1) => (FhirInteraction.SearchType, resourceType, null),
            ("GET", 2) => (FhirInteraction.Read, resourceType, path[1]),
            ("GET", 4) when path[2] == "_history" => (FhirInteraction.VRead, resourceType, path[1]),
            ("PUT", 2) => (FhirInteraction.Update, resourceType, path[1]),
            ("POST", 1) => (FhirInteraction.Create, resourceType, null),
            ("DELETE", 2) => (FhirInteraction.Delete, resourceType, path[1]),
            ("PATCH", 2) => (FhirInteraction.Patch, resourceType, path[1]),
            _ => throw new NotSupportedException($"Unknown route: {method} {ctx.Request.Path}")
        };
    }
}

// Register in Program.cs
app.UseMiddleware<FhirAuthorizationMiddleware>();
```

---

## Data Filtering (Patient Compartment)

For `patient/*.read` scopes, filter search results to only patient's data:

```csharp
public class SearchQueryHandler : IQueryHandler<SearchQuery, SearchResult>
{
    public async ValueTask<SearchResult> HandleAsync(
        SearchQuery query,
        CancellationToken ct)
    {
        // Get authorization filter from HttpContext
        var filter = _httpContext.Items["FhirAuthorizationFilter"] as FhirAuthorizationFilter;

        if (filter?.PatientFilter != null)
        {
            // Add patient filter to search parameters
            query = query with
            {
                Parameters = query.Parameters.Append(
                    new SearchParameter("patient", filter.PatientFilter)
                ).ToArray()
            };
        }

        // Execute search with filters applied
        return await _searchService.SearchAsync(query, ct);
    }
}
```

---

## Example: Full Authorization Flow

**Scenario**: User with `patient/Observation.read` scope requests `GET /Observation?code=12345`

### Step 1: Extract Context

```
UserId: "user123"
TenantId: "tenant-a"
SmartContext:
  Scopes: ["patient/Observation.read"]
  PatientContext: "patient-456"
Interaction: SearchType
ResourceType: "Observation"
```

### Step 2: Run Handlers

#### Handler 1: Authentication ✅
- User is authenticated → Allow

#### Handler 2: Tenant Isolation ✅
- User in tenant-a, request to tenant-a → Allow

#### Handler 3: RBAC ⏭️
- SMART context present → Skip

#### Handler 4: SMART Scopes ✅
- Scope `patient/Observation.read` matches `SearchType` on `Observation` → Allow
- Add filter: `{ PatientFilter: "patient-456" }`

#### Handler 5: Capability Enforcement ✅
- Check if `Observation.search-type` in CapabilityStatement → YES → Allow

### Step 3: Execute Search with Filter

```sql
SELECT * FROM Observation
WHERE TenantId = 'tenant-a'
  AND Patient = 'patient-456'  -- ← Added by authorization filter
  AND Code = '12345'
```

**Result**: User only sees observations for patient-456

---

## Configuration

### appsettings.json

```json
{
  "Authorization": {
    "Handlers": [
      {
        "Type": "AuthenticationHandler",
        "Priority": 10,
        "Enabled": true
      },
      {
        "Type": "TenantIsolationHandler",
        "Priority": 20,
        "Enabled": true
      },
      {
        "Type": "RbacAuthorizationHandler",
        "Priority": 30,
        "Enabled": true
      },
      {
        "Type": "SmartScopeAuthorizationHandler",
        "Priority": 40,
        "Enabled": true
      },
      {
        "Type": "CapabilityEnforcementHandler",
        "Priority": 50,
        "Enabled": true
      }
    ],
    "DefaultRoles": {
      "Admin": [
        { "ResourceType": "*", "Interaction": "*" }
      ],
      "Clinician": [
        { "ResourceType": "Patient", "Interaction": "*" },
        { "ResourceType": "Observation", "Interaction": "*" },
        { "ResourceType": "Encounter", "Interaction": "*" }
      ],
      "ReadOnly": [
        { "ResourceType": "*", "Interaction": "read" }
      ]
    }
  }
}
```

---

## Testing

### Unit Test: Capability Enforcement

```csharp
[Fact]
public async Task CapabilityEnforcement_DeniesUnsupportedInteraction()
{
    // Arrange
    var statement = CreateCapabilityStatement(resources: new[]
    {
        new { Type = "Patient", Interactions = new[] { "read", "create", "update" } },
        new { Type = "AuditEvent", Interactions = new[] { "read", "create" } } // No update!
    });

    var handler = new CapabilityEnforcementHandler(
        _capabilityService.Object,
        _tenantResolver.Object,
        _versionResolver.Object
    );

    var context = new FhirAuthorizationContext
    {
        Interaction = FhirInteraction.Update,
        ResourceType = "AuditEvent",
        // ...
    };

    // Act
    var result = await handler.HandleAsync(context, CancellationToken.None);

    // Assert
    Assert.False(result.Allowed);
    Assert.Contains("does not support", result.DenialReason);
}
```

### Integration Test: SMART Scope with Patient Filter

```csharp
[Fact]
public async Task SmartScope_FiltersToPatientCompartment()
{
    // 1. Create observations for 2 patients
    await CreateObservation("obs-1", "patient-1", "glucose", "100");
    await CreateObservation("obs-2", "patient-2", "glucose", "120");

    // 2. Get token with patient/Observation.read for patient-1
    var token = await GetSmartToken(scopes: "patient/Observation.read", patientId: "patient-1");

    // 3. Search observations
    var response = await _client.GetAsync("/Observation?code=glucose",
        headers: new { Authorization = $"Bearer {token}" });

    var bundle = await response.Content.ReadAsAsync<Bundle>();

    // 4. Assert: only patient-1's observation returned
    Assert.Single(bundle.Entry);
    Assert.Equal("obs-1", bundle.Entry[0].Resource.Id);
}
```

---

## Implementation Phases

### Phase 1.2 - Search Implementation (ADR-2503)
**Status**: Basic authorization

Implement:
- `FhirAuthorizationMiddleware`
- `AuthenticationHandler`
- Simple role-based checks (no SMART yet)

### Phase 3 - Validation (ADR-2506)
**Status**: Add capability enforcement

Implement:
- `CapabilityEnforcementHandler`
- `CapabilityInteractionCache`
- Integration with CapabilityStatementService

### Phase 6 - Multi-Tenant (ADR-2509)
**Status**: Tenant isolation

Implement:
- `TenantIsolationHandler`
- Tenant-specific role permissions

### Phase 10 - SMART on FHIR (ADR-2513)
**Status**: Full SMART authorization

Implement:
- `SmartScopeAuthorizationHandler`
- Patient compartment filtering
- OAuth token validation

---

## Benefits

### ✅ Compared to Legacy

| Aspect | Legacy | V2 |
|--------|--------|-----|
| **Capability enforcement** | ❌ Not enforced | ✅ Automatically enforced |
| **Authorization layers** | Single check | Pipeline with 5 handlers |
| **Patient filtering** | Manual in queries | Automatic via filter |
| **Performance** | ~2-5ms per request | <1.5ms (cached lookups) |
| **Extensibility** | Hard-coded logic | Pluggable handlers |
| **Multi-tenant** | Not supported | Built-in with isolation |

### ✅ Key Improvements

1. **Capability Contract Enforcement**: Server MUST respect its CapabilityStatement
2. **Layered Security**: Multiple defense layers (auth, RBAC, scopes, capability)
3. **Automatic Data Filtering**: Patient-scoped requests automatically filtered
4. **Performance**: O(1) capability checks via pre-built cache
5. **Extensible**: Easy to add new authorization handlers
6. **Multi-Tenant**: Tenant isolation enforced at authorization layer

---

## Consequences

### Positive

1. **FHIR Conformance**: Server behavior matches advertised capabilities
2. **Security in Depth**: Multiple authorization layers
3. **SMART Compliant**: Full patient/*.read compartment filtering
4. **Performance**: <1.5ms authorization overhead
5. **Auditability**: All authorization decisions logged

### Negative

1. **Complexity**: More code than simple allow/deny
2. **Cache Invalidation**: Must rebuild capability cache when statement changes
3. **Configuration**: Requires proper role/permission setup

### Mitigation

1. **Incremental Implementation**: Start simple, add handlers as needed
2. **Cache Management**: Reuse capability cache invalidation from capability statement investigation
3. **Default Deny**: Fail closed for security

---

## References

- Investigation: `smart-on-fhir-v2-implementation.md` (SMART scopes)
- Investigation: `dynamic-capability-statement-generation.md` (Capability caching)
- Investigation: `distributed-messaging-architecture.md` (Event bus for cache invalidation)
- ADR-2503: Phase 1.2 - Search Implementation (basic auth)
- ADR-2506: Phase 3 - Validation (capability enforcement)
- ADR-2513: Phase 10 - SMART on FHIR (full SMART auth)
- SMART App Launch Framework: http://hl7.org/fhir/smart-app-launch/
- FHIR Security: http://hl7.org/fhir/security.html

---

## Next Steps

1. **Phase 1.2**: Implement basic authorization middleware + authentication handler
2. **Phase 3**: Add capability enforcement handler with interaction cache
3. **Phase 6**: Add tenant isolation handler
4. **Phase 10**: Implement full SMART authorization with compartment filtering
