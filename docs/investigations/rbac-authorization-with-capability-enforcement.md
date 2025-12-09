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

### Architecture Decision: Endpoint Filters Over Middleware

**CRITICAL DESIGN DECISION**: Authorization and auditing MUST be implemented as endpoint filters, NOT middleware.

#### Middleware vs Endpoint Filter Comparison

| Concern | Middleware | Endpoint Filter | Winner |
|---------|-----------|-----------------|--------|
| **Bundle entry processing** | ❌ Bypassed (internal routing) | ✅ Always executed | **Filter** |
| **Per-endpoint customization** | ❌ Global only | ✅ Per-route or group | **Filter** |
| **Composition** | ⚠️ Order matters globally | ✅ Stack per endpoint | **Filter** |
| **Short-circuit** | ⚠️ Must call next() | ✅ Return early without next() | **Filter** |
| **Route access** | ⚠️ Must parse manually | ✅ RouteValues + metadata | **Filter** |
| **Testing** | ⚠️ Requires full pipeline | ✅ Unit test filter alone | **Filter** |
| **Performance** | ✅ Runs once per request | ⚠️ Runs per endpoint | **Tie** (filters cached) |

**Key Insight**: Bundle processing uses **internal request routing** to execute entries. This bypasses middleware but goes through endpoint filters.

#### The Bundle Processing Problem

When a transaction/batch bundle is submitted:
```
POST /tenant/1/   ← Middleware runs here
{
  "resourceType": "Bundle",
  "type": "transaction",
  "entry": [
    { "request": { "method": "POST", "url": "Patient" } },
    { "request": { "method": "PUT", "url": "Observation/123" } }
  ]
}

→ BundleProcessor internally routes to endpoints:
   POST /tenant/1/Patient        ← Middleware DOES NOT run (internal routing)
   PUT /tenant/1/Observation/123 ← Middleware DOES NOT run (internal routing)
```

❌ **Middleware approach**: Authorization only runs for outer bundle request, NOT for each entry
✅ **Endpoint filter approach**: Authorization runs for bundle + each entry

### Endpoint Filter Implementation

**CRITICAL DESIGN DECISION**: Authorization and auditing MUST be implemented as endpoint filters, NOT middleware.

**Why Endpoint Filters?**
1. **Bundle Processing**: Bundle entries bypass middleware but go through endpoint filters
2. **Per-Endpoint Granularity**: Different endpoints can have different authorization requirements
3. **Composition**: Stack multiple filters (auth → audit → validation)
4. **Short-Circuit**: Failed authorization prevents handler execution
5. **Type Safety**: Access to endpoint metadata and route parameters

**Architecture Pattern** (similar to existing `ResourceTypeValidationFilter`):

```csharp
/// <summary>
/// Endpoint filter that enforces FHIR authorization (RBAC, SMART scopes, capability enforcement).
/// Runs on EVERY FHIR endpoint, including bundle entry processing.
/// </summary>
public class FhirAuthorizationFilter : IEndpointFilter
{
    private readonly IFhirAuthorizationService _authzService;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<FhirAuthorizationFilter> _logger;

    public FhirAuthorizationFilter(
        IFhirAuthorizationService authzService,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<FhirAuthorizationFilter> logger)
    {
        _authzService = authzService;
        _contextAccessor = contextAccessor;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Build authorization context from route + request + user claims
        var authContext = await BuildAuthorizationContextAsync(httpContext);

        // Run authorization handlers (authentication → RBAC → SMART → capability)
        var result = await _authzService.AuthorizeAsync(authContext);

        if (!result.Allowed)
        {
            _logger.LogWarning(
                "Authorization denied: {Reason} (User: {User}, Resource: {ResourceType}/{ResourceId}, Interaction: {Interaction})",
                result.DenialReason,
                authContext.UserId,
                authContext.ResourceType,
                authContext.ResourceId,
                authContext.Interaction);

            // Return FHIR OperationOutcome with 403 Forbidden
            var outcome = new OperationOutcomeJsonNode();
            outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
                Code = OperationOutcomeJsonNode.IssueType.Forbidden,
                Diagnostics = result.DenialReason
            });

            return Results.Json(outcome, statusCode: StatusCodes.Status403Forbidden);
        }

        // Store filter in HttpContext for query layer (patient compartment filtering)
        if (result.Filter != null)
        {
            httpContext.Items["FhirAuthorizationFilter"] = result.Filter;
        }

        // Continue to next filter or handler
        return await next(context);
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
        var routeValues = ctx.Request.RouteValues;

        // Extract from route parameters (works for both direct calls and bundle entries)
        var resourceType = routeValues.TryGetValue("resourceType", out var rt) ? rt as string : null;
        var resourceId = routeValues.TryGetValue("id", out var id) ? id as string : null;

        // Determine interaction from method + route pattern
        return (method, resourceId) switch
        {
            ("GET", null) => (FhirInteraction.SearchType, resourceType, null),
            ("GET", _) => (FhirInteraction.Read, resourceType, resourceId),
            ("PUT", _) => (FhirInteraction.Update, resourceType, resourceId),
            ("POST", null) when ctx.Request.Path.Value?.EndsWith("/_search") == true => (FhirInteraction.SearchType, resourceType, null),
            ("POST", null) => (FhirInteraction.Create, resourceType, null),
            ("DELETE", _) => (FhirInteraction.Delete, resourceType, resourceId),
            ("PATCH", _) => (FhirInteraction.Patch, resourceType, resourceId),
            _ => throw new NotSupportedException($"Unknown route: {method} {ctx.Request.Path}")
        };
    }
}

// Register on endpoint groups (like ResourceTypeValidationFilter)
// src/Ignixa.Api/Endpoints/FhirEndpoints.cs
public static IEndpointRouteBuilder MapFhirTenantEndpoints(this IEndpointRouteBuilder endpoints)
{
    var tenantGroup = endpoints
        .MapGroup("/tenant/{tenantId:int}")
        .AddEndpointFilter<FhirAuthorizationFilter>()      // ← Authorization
        .AddEndpointFilter<FhirAuditFilter>()              // ← Auditing (after authz)
        .AddEndpointFilter<ResourceTypeValidationFilter>(); // ← Validation

    // All endpoints in this group now protected
    tenantGroup.MapGet("/{resourceType}/{id}", HandleGetResource);
    tenantGroup.MapPut("/{resourceType}/{id}", HandlePutResource);
    // ...

    return endpoints;
}
```

### Bundle Processing Authorization

**CRITICAL**: Bundle entries MUST go through the same authorization checks as direct API calls.

When processing a transaction/batch bundle:
1. Each bundle entry is internally routed to the appropriate endpoint
2. Endpoint filters (including `FhirAuthorizationFilter`) execute for EACH entry
3. Authorization failures in transactions → entire bundle fails (rollback)
4. Authorization failures in batches → entry marked as failed (409/403), other entries continue

**Example Flow**:
```
POST /tenant/1/
{
  "resourceType": "Bundle",
  "type": "transaction",
  "entry": [
    { "request": { "method": "POST", "url": "Patient" }, "resource": { ... } },
    { "request": { "method": "PUT", "url": "Observation/123" }, "resource": { ... } }
  ]
}

↓ Bundle processor routes internally to:

1. POST /tenant/1/Patient
   → FhirAuthorizationFilter runs
   → Checks: authenticated? tenant isolation? RBAC? SMART scopes? capability?
   → Authorized ✅

2. PUT /tenant/1/Observation/123
   → FhirAuthorizationFilter runs
   → Checks: authenticated? tenant isolation? RBAC? SMART scopes? capability?
   → Denied (no update permission) ❌

Result: Entire transaction fails (transaction semantic)
```

**Implementation Note**: The `BundleEntryExecutor` uses internal request routing, which automatically invokes all endpoint filters.

### Auditing with Endpoint Filter

Authorization and auditing are companion concerns. Both should be endpoint filters.

```csharp
/// <summary>
/// Endpoint filter that creates AuditEvent resources for FHIR operations.
/// Runs AFTER FhirAuthorizationFilter (only audit authorized requests).
/// </summary>
public class FhirAuditFilter : IEndpointFilter
{
    private readonly IMediator _mediator;
    private readonly IFhirRequestContextAccessor _contextAccessor;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var startTime = DateTimeOffset.UtcNow;

        // Execute endpoint handler
        var result = await next(context);

        var endTime = DateTimeOffset.UtcNow;

        // Create AuditEvent (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _mediator.SendAsync(new CreateAuditEventCommand
                {
                    Action = DetermineAuditAction(httpContext),
                    Agent = BuildAuditAgent(httpContext),
                    Entity = BuildAuditEntity(httpContext),
                    Outcome = DetermineOutcome(result),
                    Recorded = startTime,
                    // ...
                });
            }
            catch (Exception ex)
            {
                // Log but don't fail request
                _logger.LogError(ex, "Failed to create audit event");
            }
        });

        return result;
    }

    private string DetermineAuditAction(HttpContext ctx)
    {
        return ctx.Request.Method switch
        {
            "GET" => "R", // Read
            "POST" => "C", // Create
            "PUT" => "U", // Update
            "DELETE" => "D", // Delete
            _ => "E" // Execute
        };
    }
}

// Register AFTER authorization
var tenantGroup = endpoints
    .MapGroup("/tenant/{tenantId:int}")
    .AddEndpointFilter<FhirAuthorizationFilter>()      // ← Run first
    .AddEndpointFilter<FhirAuditFilter>()              // ← Only audit authorized requests
    .AddEndpointFilter<ResourceTypeValidationFilter>();
```

**Benefits**:
- Auditing applies to bundle entries (same as direct API calls)
- Failed authorization attempts are NOT audited (filter short-circuits)
- Successful operations are audited with full context
- Fire-and-forget pattern doesn't slow down responses

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

### Identity Provider Integration

**Development**: Use OpenIddict for local OAuth 2.0 / OpenID Connect server
**Production**: Delegate to external IdPs (Entra ID, Okta, Auth0, Keycloak)

#### OpenIddict for Local Development

OpenIddict provides a lightweight OAuth/OIDC server for development and testing without external dependencies.

**Benefits**:
- No external IdP needed for local dev
- Full SMART on FHIR scope support
- Test with multiple users/roles locally
- Can federate to external IdPs in production

**Installation**:
```bash
dotnet add package OpenIddict.AspNetCore
dotnet add package OpenIddict.EntityFrameworkCore
```

**Configuration** (appsettings.Development.json):
```json
{
  "Authentication": {
    "Provider": "OpenIddict",  // or "Entra", "Okta", "External"
    "OpenIddict": {
      "Enabled": true,
      "Issuer": "https://localhost:5001",
      "SigningKey": {
        "Type": "Development"  // Use cert in production
      },
      "Clients": [
        {
          "ClientId": "fhir-dev-client",
          "ClientSecret": "dev-secret-change-in-prod",
          "DisplayName": "FHIR Development Client",
          "RedirectUris": ["https://localhost:3000/callback"],
          "Permissions": [
            "oidc", "profile", "email",
            "patient/*.read", "user/*.write", "user/*.read"
          ]
        }
      ],
      "TestUsers": [
        {
          "Username": "admin@example.com",
          "Password": "Admin123!",  // Hashed in production
          "Roles": ["Admin"],
          "TenantId": "1"
        },
        {
          "Username": "clinician@example.com",
          "Password": "Clinician123!",
          "Roles": ["Clinician"],
          "TenantId": "1"
        }
      ]
    }
  }
}
```

**Startup Configuration**:
```csharp
// Program.cs
if (builder.Environment.IsDevelopment())
{
    // Register OpenIddict for local development
    builder.Services.AddOpenIddict()
        .AddCore(options =>
        {
            options.UseEntityFrameworkCore()
                   .UseDbContext<IdentityDbContext>();
        })
        .AddServer(options =>
        {
            options.SetIssuer(new Uri("https://localhost:5001"));

            // Enable OAuth 2.0 / OIDC flows
            options.AllowClientCredentialsFlow()
                   .AllowAuthorizationCodeFlow()
                   .RequireProofKeyForCodeExchange();

            // Register endpoints
            options.SetAuthorizationEndpointUris("/connect/authorize")
                   .SetTokenEndpointUris("/connect/token")
                   .SetUserinfoEndpointUris("/connect/userinfo");

            // Register SMART on FHIR scopes
            options.RegisterScopes("openid", "profile", "email", "fhirUser",
                "patient/*.read", "patient/*.write",
                "user/*.read", "user/*.write",
                "system/*.read", "system/*.write");

            // Use development signing key (replace with cert in production)
            options.AddDevelopmentEncryptionCertificate()
                   .AddDevelopmentSigningCertificate();

            // Register ASP.NET Core host
            options.UseAspNetCore()
                   .EnableAuthorizationEndpointPassthrough()
                   .EnableTokenEndpointPassthrough()
                   .EnableUserinfoEndpointPassthrough();
        })
        .AddValidation(options =>
        {
            options.UseLocalServer();
            options.UseAspNetCore();
        });
}
```

#### External Identity Provider Integration

**Pattern**: Use external IdP for authentication, extract claims for authorization.

**Microsoft Entra ID** (Azure AD):
```json
{
  "Authentication": {
    "Provider": "Entra",
    "Entra": {
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "your-tenant-id",
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret",  // Use Key Vault in production
      "Audience": "api://fhir-server",
      "ClaimMappings": {
        "sub": "oid",  // Map Entra user ID to subject claim
        "roles": "roles",
        "tenant_id": "extension_TenantId"
      }
    }
  }
}
```

**Okta**:
```json
{
  "Authentication": {
    "Provider": "Okta",
    "Okta": {
      "Domain": "dev-12345.okta.com",
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret",
      "Audience": "api://fhir",
      "ClaimMappings": {
        "sub": "uid",
        "roles": "groups",
        "tenant_id": "tenantId"
      }
    }
  }
}
```

**Generic OIDC Provider**:
```json
{
  "Authentication": {
    "Provider": "OIDC",
    "OIDC": {
      "Authority": "https://your-idp.com",
      "ClientId": "fhir-server",
      "ClientSecret": "secret",
      "RequireHttpsMetadata": true,
      "ResponseType": "code",
      "Scope": "openid profile email fhirUser",
      "ClaimMappings": {
        "sub": "sub",
        "roles": "role",
        "tenant_id": "tenant"
      }
    }
  }
}
```

**Startup for External IdP**:
```csharp
// Program.cs
var authConfig = builder.Configuration.GetSection("Authentication");
var provider = authConfig.GetValue<string>("Provider");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        switch (provider)
        {
            case "Entra":
                var entraConfig = authConfig.GetSection("Entra");
                options.Authority = $"{entraConfig["Instance"]}{entraConfig["TenantId"]}/v2.0";
                options.Audience = entraConfig["Audience"];
                break;

            case "Okta":
                var oktaConfig = authConfig.GetSection("Okta");
                options.Authority = $"https://{oktaConfig["Domain"]}";
                options.Audience = oktaConfig["Audience"];
                break;

            case "OIDC":
                var oidcConfig = authConfig.GetSection("OIDC");
                options.Authority = oidcConfig["Authority"];
                options.Audience = oidcConfig["ClientId"];
                options.RequireHttpsMetadata = oidcConfig.GetValue<bool>("RequireHttpsMetadata");
                break;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero  // No tolerance for expired tokens
        };

        // Map external IdP claims to FHIR authorization context
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                // Map claims based on ClaimMappings configuration
                var claimMappings = authConfig.GetSection($"{provider}:ClaimMappings");
                // ... map claims to FhirAuthorizationContext
            }
        };
    });
```

### Authorization Handlers Configuration

appsettings.json:
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

### Hybrid Approach: OpenIddict with External IdP Federation

**Best of both worlds**: Use OpenIddict locally, federate to external IdP in production.

```json
{
  "Authentication": {
    "Provider": "OpenIddict",
    "OpenIddict": {
      "Enabled": true,
      "Issuer": "https://localhost:5001",
      "Federation": {
        "Enabled": true,  // Enable external IdP federation
        "Providers": [
          {
            "Name": "Entra",
            "DisplayName": "Sign in with Microsoft",
            "Type": "OIDC",
            "Authority": "https://login.microsoftonline.com/common/v2.0",
            "ClientId": "your-client-id",
            "ClientSecret": "your-secret",
            "Scopes": ["openid", "profile", "email"]
          },
          {
            "Name": "Okta",
            "DisplayName": "Sign in with Okta",
            "Type": "OIDC",
            "Authority": "https://dev-12345.okta.com",
            "ClientId": "your-client-id",
            "ClientSecret": "your-secret"
          }
        ]
      }
    }
  }
}
```

This allows:
- Local dev: Use OpenIddict test users
- Staging: Federate to test Entra/Okta
- Production: Full external IdP integration

---

## Testing

### Local Token Generation for .http Files

**Pattern**: Use OpenIddict to generate test tokens locally (similar to [Microsoft FHIR Server approach](https://github.com/microsoft/fhir-server/blob/main/docs/rest/SearchExamples.http))

**Benefits**:
- No manual token creation needed
- Test with different users/roles/scopes
- Works with VS Code REST Client extension
- Automated token refresh in test suites

#### OpenIddict Token Endpoint Configuration

When OpenIddict is enabled for development, expose token endpoint at `/connect/token`:

```csharp
// Program.cs - Development environment only
if (builder.Environment.IsDevelopment())
{
    // ... OpenIddict configuration from earlier ...

    // Add token endpoint controller/handler
    app.MapPost("/connect/token", async (HttpContext context,
        OpenIddictApplicationManager<OpenIddictApplication> applicationManager,
        OpenIddictAuthorizationManager<OpenIddictAuthorization> authorizationManager,
        IOpenIddictScopeManager scopeManager,
        SignInManager<ApplicationUser> signInManager) =>
    {
        var request = context.GetOpenIddictServerRequest();

        // Handle client_credentials flow (for service accounts)
        if (request.IsClientCredentialsGrantType())
        {
            var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            identity.AddClaim(Claims.Subject, request.ClientId);
            identity.AddClaim(Claims.Name, request.ClientId);

            // Add roles from client configuration
            var client = await applicationManager.FindByClientIdAsync(request.ClientId);
            var roles = await applicationManager.GetRolesAsync(client);
            foreach (var role in roles)
            {
                identity.AddClaim(Claims.Role, role);
            }

            // Add scopes
            identity.SetScopes(request.GetScopes());
            identity.SetResources(await scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // Handle password flow (for test users in development)
        if (request.IsPasswordGrantType())
        {
            var user = await userManager.FindByNameAsync(request.Username);
            if (user == null)
            {
                return Results.Json(new
                {
                    error = "invalid_grant",
                    error_description = "Invalid username or password"
                }, statusCode: 400);
            }

            var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, false);
            if (!result.Succeeded)
            {
                return Results.Json(new
                {
                    error = "invalid_grant",
                    error_description = "Invalid username or password"
                }, statusCode: 400);
            }

            var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            identity.AddClaim(Claims.Subject, user.Id);
            identity.AddClaim(Claims.Name, user.UserName);
            identity.AddClaim("tenant_id", user.TenantId.ToString());

            // Add user roles
            var roles = await userManager.GetRolesAsync(user);
            foreach (var role in roles)
            {
                identity.AddClaim(Claims.Role, role);
            }

            identity.SetScopes(request.GetScopes());

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return Results.Json(new { error = "unsupported_grant_type" }, statusCode: 400);
    });
}
```

#### Example .http File for Local Testing

Create `docs/rest/FhirApiExamples.http`:

```http
### Variables
@hostname = localhost:5001
@tenantId = 1

### 1. Get Admin Token (client_credentials flow)
# @name adminToken
POST https://{{hostname}}/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=fhir-admin-client
&client_secret=dev-secret
&scope=system/*.read system/*.write

### 2. Get Clinician Token (password flow)
# @name clinicianToken
POST https://{{hostname}}/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password
&username=clinician@example.com
&password=Clinician123!
&scope=user/Patient.read user/Observation.read user/Observation.write

### 3. Get Patient-Scoped Token (SMART on FHIR)
# @name patientToken
POST https://{{hostname}}/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password
&username=patient@example.com
&password=Patient123!
&scope=patient/Observation.read patient/MedicationRequest.read
&patient_id=patient-123

###############################################################################
# FHIR Operations Using Tokens
###############################################################################

### 4. Search Patients (Admin - Full Access)
GET https://{{hostname}}/tenant/{{tenantId}}/Patient?name=Smith
Authorization: Bearer {{adminToken.response.body.access_token}}
Accept: application/fhir+json

### 5. Search Patients (Clinician - User Scope)
GET https://{{hostname}}/tenant/{{tenantId}}/Patient?name=Smith
Authorization: Bearer {{clinicianToken.response.body.access_token}}
Accept: application/fhir+json

### 6. Search Observations (Patient - Filtered to Patient Compartment)
GET https://{{hostname}}/tenant/{{tenantId}}/Observation?code=8867-4
Authorization: Bearer {{patientToken.response.body.access_token}}
Accept: application/fhir+json

### 7. Create Observation (Clinician - Has Write Permission)
POST https://{{hostname}}/tenant/{{tenantId}}/Observation
Authorization: Bearer {{clinicianToken.response.body.access_token}}
Content-Type: application/fhir+json

{
  "resourceType": "Observation",
  "status": "final",
  "code": {
    "coding": [{
      "system": "http://loinc.org",
      "code": "8867-4",
      "display": "Heart rate"
    }]
  },
  "subject": { "reference": "Patient/patient-123" },
  "valueQuantity": {
    "value": 80,
    "unit": "beats/minute",
    "system": "http://unitsofmeasure.org",
    "code": "/min"
  }
}

### 8. Update Observation (Patient - Should Fail, No Write Scope)
PUT https://{{hostname}}/tenant/{{tenantId}}/Observation/obs-123
Authorization: Bearer {{patientToken.response.body.access_token}}
Content-Type: application/fhir+json

{
  "resourceType": "Observation",
  "id": "obs-123",
  "status": "amended",
  "code": {
    "coding": [{
      "system": "http://loinc.org",
      "code": "8867-4",
      "display": "Heart rate"
    }]
  }
}

### 9. Delete AuditEvent (Should Fail - CapabilityStatement Doesn't Allow)
DELETE https://{{hostname}}/tenant/{{tenantId}}/AuditEvent/audit-123
Authorization: Bearer {{adminToken.response.body.access_token}}

### 10. Transaction Bundle with Mixed Permissions
POST https://{{hostname}}/tenant/{{tenantId}}/
Authorization: Bearer {{clinicianToken.response.body.access_token}}
Content-Type: application/fhir+json

{
  "resourceType": "Bundle",
  "type": "transaction",
  "entry": [
    {
      "request": { "method": "POST", "url": "Patient" },
      "resource": {
        "resourceType": "Patient",
        "name": [{ "family": "Test", "given": ["Bundle"] }]
      }
    },
    {
      "request": { "method": "POST", "url": "Observation" },
      "resource": {
        "resourceType": "Observation",
        "status": "final",
        "code": { "coding": [{ "system": "http://loinc.org", "code": "8867-4" }] }
      }
    }
  ]
}
```

#### Token Testing Scenarios

**Scenario 1: Role-Based Access**
```http
### Admin Token (All Resources)
# @name adminToken
POST https://{{hostname}}/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=admin-client
&client_secret=admin-secret
&scope=system/*.read system/*.write

### ReadOnly Token (Search Only)
# @name readOnlyToken
POST https://{{hostname}}/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=readonly-client
&client_secret=readonly-secret
&scope=system/*.read

### Test: Admin Can Delete
DELETE https://{{hostname}}/tenant/1/Patient/test-123
Authorization: Bearer {{adminToken.response.body.access_token}}
# Expected: 204 No Content

### Test: ReadOnly Cannot Delete (403 Forbidden)
DELETE https://{{hostname}}/tenant/1/Patient/test-123
Authorization: Bearer {{readOnlyToken.response.body.access_token}}
# Expected: 403 Forbidden (RBAC denies)
```

**Scenario 2: SMART Patient Scoping**
```http
### Patient Token for patient-123
# @name patient123Token
POST https://{{hostname}}/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password
&username=patient123@example.com
&password=Patient123!
&scope=patient/Observation.read patient/MedicationRequest.read

### Search Own Observations (Filtered)
GET https://{{hostname}}/tenant/1/Observation
Authorization: Bearer {{patient123Token.response.body.access_token}}
# Expected: Only observations for patient-123 (compartment filtering)

### Search Another Patient's Observations (Should Fail)
GET https://{{hostname}}/tenant/1/Observation?patient=patient-456
Authorization: Bearer {{patient123Token.response.body.access_token}}
# Expected: 403 Forbidden (patient compartment violation)
```

**Scenario 3: Capability Statement Enforcement**
```http
### Admin Token
# @name adminToken
POST https://{{hostname}}/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=admin-client
&client_secret=admin-secret
&scope=system/*.read system/*.write

### Test: Update AuditEvent (Should Fail - Capability)
PUT https://{{hostname}}/tenant/1/AuditEvent/audit-123
Authorization: Bearer {{adminToken.response.body.access_token}}
Content-Type: application/fhir+json

{
  "resourceType": "AuditEvent",
  "id": "audit-123"
}
# Expected: 403 Forbidden (CapabilityStatement doesn't allow AuditEvent update)
```

#### VS Code REST Client Setup

Install the [REST Client extension](https://marketplace.visualstudio.com/items?itemName=humao.rest-client) and configure:

**.vscode/settings.json**:
```json
{
  "rest-client.environmentVariables": {
    "local": {
      "hostname": "localhost:5001",
      "tenantId": "1"
    },
    "staging": {
      "hostname": "staging.fhir.example.com",
      "tenantId": "1"
    }
  }
}
```

**Usage**:
1. Open `docs/rest/FhirApiExamples.http`
2. Click "Send Request" above any `###` section
3. Token responses are cached automatically by `@name` directive
4. Subsequent requests use `{{tokenName.response.body.access_token}}`

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

## Deployment Scenarios & Client Workflows

### Scenario 1: Local Development (OpenIddict)

**Environment**: Developer workstation running `dotnet run`

**Identity Provider**: OpenIddict (in-process, no external dependencies)

**Configuration** (appsettings.Development.json):
```json
{
  "Authentication": {
    "Provider": "OpenIddict",
    "OpenIddict": {
      "Enabled": true,
      "Issuer": "https://localhost:5001",
      "Clients": [
        {
          "ClientId": "postman-client",
          "ClientSecret": "dev-secret",
          "DisplayName": "Postman Testing Client",
          "RedirectUris": ["https://oauth.pstmn.io/v1/callback"],
          "Scopes": ["system/*.read", "system/*.write"]
        },
        {
          "ClientId": "web-app",
          "ClientSecret": "dev-secret",
          "DisplayName": "Web App Client",
          "RedirectUris": ["https://localhost:3000/callback"],
          "Scopes": ["user/*.read", "user/*.write", "openid", "profile"]
        }
      ],
      "TestUsers": [
        {
          "Username": "admin@dev.local",
          "Password": "Admin123!",
          "Roles": ["Admin"],
          "TenantId": "1"
        },
        {
          "Username": "clinician@dev.local",
          "Password": "Clinician123!",
          "Roles": ["Clinician"],
          "TenantId": "1"
        }
      ]
    }
  }
}
```

**Client Workflow**:

**Option A: Using .http files** (VS Code REST Client)
```http
### Get token for local testing
# @name devToken
POST https://localhost:5001/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password
&username=clinician@dev.local
&password=Clinician123!
&scope=user/Patient.read user/Observation.read

### Use token to search patients
GET https://localhost:5001/tenant/1/Patient?name=Smith
Authorization: Bearer {{devToken.response.body.access_token}}
```

**Option B: Using Postman**
1. Create new request in Postman
2. Authorization tab → Type: OAuth 2.0
3. Configure:
   - Grant Type: Authorization Code (PKCE) or Password
   - Auth URL: `https://localhost:5001/connect/authorize`
   - Access Token URL: `https://localhost:5001/connect/token`
   - Client ID: `postman-client`
   - Client Secret: `dev-secret`
   - Scope: `user/Patient.read user/Observation.read`
4. Click "Get New Access Token"
5. Use token for all requests

**Option C: Custom Application**
```csharp
// Client app connecting to local FHIR server
var client = new HttpClient();

// Get token
var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["grant_type"] = "password",
    ["username"] = "clinician@dev.local",
    ["password"] = "Clinician123!",
    ["scope"] = "user/Patient.read user/Observation.read"
});

var tokenResponse = await client.PostAsync("https://localhost:5001/connect/token", tokenRequest);
var tokenData = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();

// Use token for FHIR requests
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);

var patients = await client.GetFromJsonAsync<Bundle>("https://localhost:5001/tenant/1/Patient?name=Smith");
```

**Pros**:
- ✅ No external dependencies
- ✅ Fast iteration (no network latency)
- ✅ Test multiple users/roles easily
- ✅ Full control over claims

**Cons**:
- ❌ Not production-ready (dev secrets, in-memory storage)
- ❌ Doesn't test external IdP integration

---

### Scenario 2a: Azure App Service with OpenIddict

**Environment**: Azure App Service (Linux or Windows)

**Identity Provider**: OpenIddict with persistent storage (Azure SQL Database)

**Why OpenIddict in App Service?**
- ✅ Control over token lifetime and claims
- ✅ Support multiple external apps (mobile, web, desktop)
- ✅ Can federate to external IdPs later
- ✅ SMART on FHIR compliant scopes
- ❌ You manage user accounts (more responsibility)
- ❌ Additional database for OpenIddict storage

**Configuration** (appsettings.Production.json):
```json
{
  "Authentication": {
    "Provider": "OpenIddict",
    "OpenIddict": {
      "Enabled": true,
      "Issuer": "https://fhir-api.azurewebsites.net",
      "Database": {
        "ConnectionString": "@Microsoft.KeyVault(SecretUri=https://fhir-kv.vault.azure.net/secrets/OpenIddictDbConnection)",
        "Provider": "SqlServer"
      },
      "Certificates": {
        "Signing": {
          "Source": "KeyVault",
          "KeyVaultUri": "https://fhir-kv.vault.azure.net/certificates/fhir-signing-cert"
        },
        "Encryption": {
          "Source": "KeyVault",
          "KeyVaultUri": "https://fhir-kv.vault.azure.net/certificates/fhir-encryption-cert"
        }
      },
      "Clients": [
        {
          "ClientId": "mobile-app",
          "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://fhir-kv.vault.azure.net/secrets/MobileAppClientSecret)",
          "DisplayName": "Mobile FHIR App",
          "RedirectUris": ["myapp://callback"],
          "Scopes": ["patient/*.read", "patient/*.write", "openid", "profile", "fhirUser"]
        },
        {
          "ClientId": "web-portal",
          "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://fhir-kv.vault.azure.net/secrets/WebPortalClientSecret)",
          "DisplayName": "Patient Portal",
          "RedirectUris": ["https://portal.example.com/callback"],
          "Scopes": ["user/*.read", "user/*.write", "openid", "profile"]
        }
      ]
    }
  },
  "Authorization": {
    "RoleStore": {
      "Provider": "AzureSql",  // Store roles/permissions in FHIR database
      "ConnectionString": "@Microsoft.KeyVault(SecretUri=https://fhir-kv.vault.azure.net/secrets/FhirDbConnection)"
    }
  }
}
```

**Startup Configuration**:
```csharp
// Program.cs - Production with OpenIddict
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<IdentityDbContext>()
               .ReplaceDefaultEntities<Guid>();  // Use GUID instead of string for IDs
    })
    .AddServer(options =>
    {
        options.SetIssuer(new Uri(builder.Configuration["Authentication:OpenIddict:Issuer"]));

        // Load certificates from Azure Key Vault
        var signingCert = LoadCertificateFromKeyVault(
            builder.Configuration["Authentication:OpenIddict:Certificates:Signing:KeyVaultUri"]);
        var encryptionCert = LoadCertificateFromKeyVault(
            builder.Configuration["Authentication:OpenIddict:Certificates:Encryption:KeyVaultUri"]);

        options.AddSigningCertificate(signingCert);
        options.AddEncryptionCertificate(encryptionCert);

        // Enable production flows
        options.AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange()
               .AllowRefreshTokenFlow()
               .AllowClientCredentialsFlow();

        // ... rest of configuration
    });
```

**Client Workflow** (Mobile App):
```typescript
// React Native / TypeScript mobile app
import { AuthSession } from 'expo-auth-session';

const discovery = {
  authorizationEndpoint: 'https://fhir-api.azurewebsites.net/connect/authorize',
  tokenEndpoint: 'https://fhir-api.azurewebsites.net/connect/token',
};

// Initiate OAuth flow
const [request, response, promptAsync] = AuthSession.useAuthRequest(
  {
    clientId: 'mobile-app',
    scopes: ['patient/*.read', 'patient/*.write', 'openid', 'fhirUser'],
    redirectUri: 'myapp://callback',
    usePKCE: true,  // Required for security
  },
  discovery
);

// User taps "Sign In"
await promptAsync();

// Exchange code for token
const tokenResponse = await AuthSession.exchangeCodeAsync(
  {
    clientId: 'mobile-app',
    code: response.params.code,
    redirectUri: 'myapp://callback',
    extraParams: {
      code_verifier: request.codeVerifier,
    },
  },
  discovery
);

// Use token for FHIR requests
const fhirClient = new FHIRClient({
  baseUrl: 'https://fhir-api.azurewebsites.net',
  auth: {
    token: tokenResponse.accessToken,
  },
});

const observations = await fhirClient.search('Observation', {
  patient: 'patient-123',
  code: '8867-4',
});
```

---

### Scenario 2b: Azure App Service with Entra ID

**Environment**: Azure App Service (Linux or Windows)

**Identity Provider**: Microsoft Entra ID (Azure AD)

**Why Entra ID in App Service?**
- ✅ No user management (Microsoft handles it)
- ✅ Enterprise SSO (users already in Azure AD)
- ✅ Multi-factor authentication built-in
- ✅ Conditional access policies
- ✅ No additional database needed
- ❌ Less control over token claims
- ❌ SMART scopes require custom claims (workaround possible)

**Configuration** (appsettings.Production.json):
```json
{
  "Authentication": {
    "Provider": "Entra",
    "Entra": {
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "12345678-1234-1234-1234-123456789012",
      "ClientId": "87654321-4321-4321-4321-210987654321",
      "Audience": "api://fhir-server",
      "ValidIssuers": [
        "https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/v2.0",
        "https://sts.windows.net/12345678-1234-1234-1234-123456789012/"
      ],
      "ClaimMappings": {
        "sub": "oid",  // User object ID
        "roles": "roles",  // App roles from Entra
        "tenant_id": "extension_TenantId",  // Custom claim
        "fhirUser": "fhirUser"  // Custom claim for SMART
      }
    }
  },
  "Authorization": {
    "RoleStore": {
      "Provider": "EntraAppRoles",  // Use Entra ID app roles
      "Mappings": {
        "FhirAdmin": [
          { "ResourceType": "*", "Interaction": "*" }
        ],
        "FhirClinician": [
          { "ResourceType": "Patient", "Interaction": "*" },
          { "ResourceType": "Observation", "Interaction": "*" }
        ],
        "FhirReadOnly": [
          { "ResourceType": "*", "Interaction": "read" }
        ]
      }
    }
  }
}
```

**Entra ID App Registration Setup**:
1. Go to Azure Portal → App Registrations → New registration
2. Name: "FHIR Server API"
3. Supported account types: "Accounts in this organizational directory only"
4. Redirect URI: (skip for API)
5. After creation:
   - **Expose an API** → Add scope: `api://fhir-server/user_impersonation`
   - **App roles** → Add roles: `FhirAdmin`, `FhirClinician`, `FhirReadOnly`
   - **Manifest** → Add custom claims:
     ```json
     {
       "optionalClaims": {
         "accessToken": [
           { "name": "fhirUser", "source": "user", "essential": false }
         ]
       }
     }
     ```
   - **Enterprise applications** → Assign users to roles

**Client Workflow** (Web Portal):
```typescript
// React web app using MSAL.js
import { PublicClientApplication } from '@azure/msal-browser';

const msalConfig = {
  auth: {
    clientId: 'web-portal-client-id',
    authority: 'https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012',
    redirectUri: 'https://portal.example.com/callback',
  },
};

const msalInstance = new PublicClientApplication(msalConfig);

// Sign in user
const loginResponse = await msalInstance.loginPopup({
  scopes: ['api://fhir-server/user_impersonation'],
});

// Get token for FHIR API
const tokenResponse = await msalInstance.acquireTokenSilent({
  scopes: ['api://fhir-server/user_impersonation'],
  account: loginResponse.account,
});

// Call FHIR API
const response = await fetch('https://fhir-api.azurewebsites.net/tenant/1/Patient?name=Smith', {
  headers: {
    'Authorization': `Bearer ${tokenResponse.accessToken}`,
  },
});
```

**Recommendation for App Service**:
- **Use Entra ID if**: Your users are already in Azure AD (enterprise customers)
- **Use OpenIddict if**: You have public users or need full control over scopes/claims

---

### Scenario 3: Azure PaaS with Entra ID + Azure RBAC Integration

**Environment**: Azure Container Apps or Azure Kubernetes Service (AKS)

**Identity Provider**: Microsoft Entra ID with Managed Identity

**Why This Approach?**
- ✅ **Azure RBAC integration**: Use Azure role assignments for FHIR permissions
- ✅ **Managed Identity**: No secrets to manage for app itself
- ✅ **Entra Workload Identity**: Kubernetes pods use Azure AD
- ✅ **Scalable**: Auto-scale with container orchestration
- ✅ **Enterprise-ready**: Full audit trail via Azure Monitor

**Architecture**:
```
User (Entra ID)
  ↓ OAuth 2.0 token
FHIR API (Container Apps / AKS)
  ↓ Managed Identity
Azure SQL (FHIR data)
Azure Storage (StructureDefinitions)
Azure Key Vault (secrets)
```

**Configuration** (appsettings.Production.json):
```json
{
  "Authentication": {
    "Provider": "Entra",
    "Entra": {
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "12345678-1234-1234-1234-123456789012",
      "ClientId": "fhir-api-managed-identity-client-id",
      "Audience": "api://fhir-server",
      "ValidIssuers": [
        "https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/v2.0"
      ]
    }
  },
  "Authorization": {
    "Provider": "AzureRbac",  // Use Azure RBAC for permissions
    "AzureRbac": {
      "Enabled": true,
      "Scope": "/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.HealthcareApis/fhirServices/{fhirServiceName}",
      "RoleMappings": {
        // Map Azure RBAC roles to FHIR permissions
        "FHIR Data Contributor": [
          { "ResourceType": "*", "Interaction": "*" }
        ],
        "FHIR Data Reader": [
          { "ResourceType": "*", "Interaction": "read" }
        ],
        "FHIR Data Writer": [
          { "ResourceType": "Patient", "Interaction": "*" },
          { "ResourceType": "Observation", "Interaction": "*" }
        ]
      }
    }
  },
  "AzureServices": {
    "SqlDatabase": {
      "ConnectionString": "Server=tcp:fhir-sql.database.windows.net,1433;Database=FhirDb;Authentication=Active Directory Managed Identity;",
      "ManagedIdentityClientId": "fhir-api-managed-identity-client-id"
    },
    "Storage": {
      "AccountName": "fhirstorage",
      "UseManagedIdentity": true
    },
    "KeyVault": {
      "VaultUri": "https://fhir-kv.vault.azure.net/",
      "UseManagedIdentity": true
    }
  }
}
```

**Azure RBAC Authorization Handler**:
```csharp
public class AzureRbacAuthorizationHandler : IAuthorizationHandler
{
    private readonly AzureRbacClient _rbacClient;
    private readonly IConfiguration _configuration;

    public int Priority => 30;  // Run after authentication, before SMART scopes

    public async ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken ct)
    {
        // Skip if using SMART scopes
        if (context.SmartContext != null)
        {
            return new AuthorizationResult { Allowed = true };
        }

        // Get user's Azure RBAC role assignments
        var userId = context.UserId;
        var scope = _configuration["Authorization:AzureRbac:Scope"];

        var roleAssignments = await _rbacClient.GetRoleAssignmentsAsync(userId, scope, ct);

        // Map Azure roles to FHIR permissions
        var mappings = _configuration.GetSection("Authorization:AzureRbac:RoleMappings")
            .Get<Dictionary<string, FhirPermission[]>>();

        var permissions = roleAssignments
            .SelectMany(role => mappings.GetValueOrDefault(role.RoleDefinitionName, []))
            .ToList();

        var required = new FhirPermission(
            ResourceType: context.ResourceType ?? "*",
            Interaction: context.Interaction.ToString().ToLowerInvariant()
        );

        if (!permissions.Any(p => p.Matches(required)))
        {
            return new AuthorizationResult
            {
                Allowed = false,
                DenialReason = $"Azure RBAC: User does not have required role for {required}"
            };
        }

        return new AuthorizationResult { Allowed = true };
    }
}
```

**Deployment** (Azure Container Apps):
```bicep
resource fhirApi 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'fhir-api'
  location: location
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      secrets: [
        {
          name: 'sql-connection-string'
          keyVaultUrl: 'https://fhir-kv.vault.azure.net/secrets/FhirDbConnection'
          identity: managedIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'fhir-api'
          image: 'fhiracr.azurecr.io/fhir-api:latest'
          env: [
            {
              name: 'Authentication__Provider'
              value: 'Entra'
            }
            {
              name: 'Authorization__Provider'
              value: 'AzureRbac'
            }
            {
              name: 'AzureServices__SqlDatabase__ConnectionString'
              secretRef: 'sql-connection-string'
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 10
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '100'
              }
            }
          }
        ]
      }
    }
  }
}

// Grant FHIR API managed identity access to resources
resource sqlRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(sqlServer.id, managedIdentity.id, 'SqlDbContributor')
  scope: sqlServer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '9b7fa17d-e63e-47b0-bb0a-15c516ac86ec')  // SQL DB Contributor
    principalId: managedIdentity.properties.principalId
  }
}
```

**Client Workflow** (Same as Scenario 2b):
- Web/mobile app uses MSAL to get Entra ID token
- Token includes Azure RBAC role assignments as claims
- FHIR API validates token + checks Azure RBAC permissions

**Assign FHIR Permissions**:
```bash
# Assign user to FHIR Data Contributor role
az role assignment create \
  --assignee user@example.com \
  --role "FHIR Data Contributor" \
  --scope "/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.HealthcareApis/fhirServices/fhir-api"

# Assign group to FHIR Data Reader role
az role assignment create \
  --assignee-object-id {groupObjectId} \
  --assignee-principal-type Group \
  --role "FHIR Data Reader" \
  --scope "/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.HealthcareApis/fhirServices/fhir-api"
```

**Benefits**:
- ✅ Centralized role management via Azure Portal
- ✅ No separate role database needed
- ✅ Audit trail via Azure Activity Log
- ✅ Works with Entra ID Privileged Identity Management (PIM)
- ✅ Conditional access policies apply automatically

---

### Comparison: Which Scenario to Choose?

| Scenario | Best For | Identity Provider | Role Management | Complexity | Cost |
|----------|----------|------------------|-----------------|------------|------|
| **Local Dev** | Development/testing | OpenIddict (in-process) | Config file | Low | Free |
| **App Service + OpenIddict** | Public apps, full control | OpenIddict (persistent) | SQL Database | Medium | $$ (DB + certs) |
| **App Service + Entra** | Enterprise, existing Azure AD | Entra ID | Entra app roles | Medium | $ |
| **PaaS + Entra + Azure RBAC** | Enterprise, scalable, audit | Entra ID | Azure RBAC | High | $$$ (containers) |

**Recommendation**:
- **Development**: Always use Scenario 1 (OpenIddict local)
- **Production - Public App**: Use Scenario 2a (OpenIddict persistent)
- **Production - Enterprise**: Use Scenario 2b or 3 (Entra ID)
- **Production - High Scale**: Use Scenario 3 (Azure PaaS + RBAC)

---

## SMART on FHIR Discovery: CapabilityStatement Integration

**FHIR Requirement**: Servers SHALL advertise their OAuth 2.0 endpoints in the CapabilityStatement ([SMART App Launch Framework](http://hl7.org/fhir/smart-app-launch/conformance.html))

### CapabilityStatement.rest.security Extension

The server must include OAuth 2.0 endpoint URIs in the `CapabilityStatement.rest.security` element:

```json
{
  "resourceType": "CapabilityStatement",
  "rest": [{
    "mode": "server",
    "security": {
      "cors": true,
      "service": [{
        "coding": [{
          "system": "http://terminology.hl7.org/CodeSystem/restful-security-service",
          "code": "SMART-on-FHIR",
          "display": "SMART on FHIR"
        }]
      }],
      "extension": [{
        "url": "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris",
        "extension": [
          {
            "url": "authorize",
            "valueUri": "https://fhir-api.azurewebsites.net/connect/authorize"
          },
          {
            "url": "token",
            "valueUri": "https://fhir-api.azurewebsites.net/connect/token"
          },
          {
            "url": "introspect",
            "valueUri": "https://fhir-api.azurewebsites.net/connect/introspect"
          },
          {
            "url": "revoke",
            "valueUri": "https://fhir-api.azurewebsites.net/connect/revoke"
          },
          {
            "url": "register",
            "valueUri": "https://fhir-api.azurewebsites.net/connect/register"
          }
        ]
      }],
      "description": "This server supports SMART on FHIR authorization with the following scopes: patient/*.read, patient/*.write, user/*.read, user/*.write, system/*.read, system/*.write"
    }
  }]
}
```

### Dynamic CapabilityStatement Generation

**Implementation**: Populate OAuth URIs based on identity provider configuration

```csharp
public class SmartSecurityCapabilitySegment : ICapabilityStatementSegment
{
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public async Task<ITypedElement?> BuildSegmentAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var provider = _configuration["Authentication:Provider"];
        var baseUrl = GetBaseUrl();

        // Get OAuth endpoints based on provider
        var (authorizeUrl, tokenUrl, introspectUrl, revokeUrl) = provider switch
        {
            "OpenIddict" => (
                $"{baseUrl}/connect/authorize",
                $"{baseUrl}/connect/token",
                $"{baseUrl}/connect/introspect",
                $"{baseUrl}/connect/revoke"
            ),
            "Entra" => (
                $"https://login.microsoftonline.com/{_configuration["Authentication:Entra:TenantId"]}/oauth2/v2.0/authorize",
                $"https://login.microsoftonline.com/{_configuration["Authentication:Entra:TenantId"]}/oauth2/v2.0/token",
                null,  // Entra doesn't support introspect
                $"https://login.microsoftonline.com/{_configuration["Authentication:Entra:TenantId"]}/oauth2/v2.0/logout"
            ),
            _ => throw new NotSupportedException($"Provider {provider} not supported")
        };

        // Build security element
        var security = new CapabilityStatementSecurityJsonNode
        {
            Cors = true,
            Description = "This server supports SMART on FHIR authorization with patient/*.read, patient/*.write, user/*.read, user/*.write, system/*.read, system/*.write scopes"
        };

        // Add SMART-on-FHIR service coding
        security.Service.Add(new CodeableConceptJsonNode
        {
            Coding =
            [
                new CodingJsonNode
                {
                    System = "http://terminology.hl7.org/CodeSystem/restful-security-service",
                    Code = "SMART-on-FHIR",
                    Display = "SMART on FHIR"
                }
            ]
        });

        // Add OAuth URI extension
        var oauthExtension = new ExtensionJsonNode
        {
            Url = "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris"
        };

        oauthExtension.Extension.Add(new ExtensionJsonNode
        {
            Url = "authorize",
            Value = new UriJsonNode(authorizeUrl)
        });

        oauthExtension.Extension.Add(new ExtensionJsonNode
        {
            Url = "token",
            Value = new UriJsonNode(tokenUrl)
        });

        if (introspectUrl != null)
        {
            oauthExtension.Extension.Add(new ExtensionJsonNode
            {
                Url = "introspect",
                Value = new UriJsonNode(introspectUrl)
            });
        }

        if (revokeUrl != null)
        {
            oauthExtension.Extension.Add(new ExtensionJsonNode
            {
                Url = "revoke",
                Value = new UriJsonNode(revokeUrl)
            });
        }

        security.Extension.Add(oauthExtension);

        return security;
    }

    private string GetBaseUrl()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null)
        {
            return _configuration["Authentication:OpenIddict:Issuer"] ?? "https://localhost:5001";
        }

        return $"{request.Scheme}://{request.Host}";
    }
}

// Register in CapabilityStatementService
services.AddSingleton<ICapabilityStatementSegment, SmartSecurityCapabilitySegment>();
```

### SMART Configuration Endpoint (.well-known/smart-configuration)

**Optional but Recommended**: Provide a dedicated SMART discovery endpoint

```csharp
// Add to FhirEndpoints.cs or create SmartEndpoints.cs
public static IEndpointRouteBuilder MapSmartDiscoveryEndpoints(this IEndpointRouteBuilder endpoints)
{
    endpoints.MapGet("/.well-known/smart-configuration", HandleSmartConfiguration)
        .WithName("SmartConfiguration")
        .Produces<SmartConfiguration>(StatusCodes.Status200OK, "application/json");

    return endpoints;
}

private static async Task<IResult> HandleSmartConfiguration(
    HttpContext context,
    [FromServices] IConfiguration configuration)
{
    var provider = configuration["Authentication:Provider"];
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

    var (authorizeUrl, tokenUrl, introspectUrl, revokeUrl, registrationUrl) = provider switch
    {
        "OpenIddict" => (
            $"{baseUrl}/connect/authorize",
            $"{baseUrl}/connect/token",
            $"{baseUrl}/connect/introspect",
            $"{baseUrl}/connect/revoke",
            $"{baseUrl}/connect/register"
        ),
        "Entra" => (
            $"https://login.microsoftonline.com/{configuration["Authentication:Entra:TenantId"]}/oauth2/v2.0/authorize",
            $"https://login.microsoftonline.com/{configuration["Authentication:Entra:TenantId"]}/oauth2/v2.0/token",
            null,
            $"https://login.microsoftonline.com/{configuration["Authentication:Entra:TenantId"]}/oauth2/v2.0/logout",
            null
        ),
        _ => throw new NotSupportedException($"Provider {provider} not supported")
    };

    var smartConfig = new
    {
        authorization_endpoint = authorizeUrl,
        token_endpoint = tokenUrl,
        token_endpoint_auth_methods_supported = new[] { "client_secret_basic", "client_secret_post", "private_key_jwt" },
        grant_types_supported = new[] { "authorization_code", "client_credentials", "refresh_token" },
        registration_endpoint = registrationUrl,
        scopes_supported = new[]
        {
            "openid", "profile", "email", "fhirUser",
            "patient/*.read", "patient/*.write",
            "user/*.read", "user/*.write",
            "system/*.read", "system/*.write"
        },
        response_types_supported = new[] { "code" },
        management_endpoint = $"{baseUrl}/manage",
        introspection_endpoint = introspectUrl,
        revocation_endpoint = revokeUrl,
        capabilities = new[]
        {
            "launch-ehr",               // EHR launch support
            "launch-standalone",        // Standalone launch
            "client-public",            // Public clients (PKCE)
            "client-confidential-symmetric",  // Confidential clients
            "sso-openid-connect",      // SSO support
            "context-ehr-patient",     // Patient context
            "context-ehr-encounter",   // Encounter context
            "context-standalone-patient",  // Standalone patient selection
            "permission-offline",      // Refresh tokens
            "permission-patient",      // Patient scopes
            "permission-user",         // User scopes
            "permission-v2"            // SMART v2 scopes
        }
    };

    return Results.Json(smartConfig);
}
```

### Client Discovery Flow

**How SMART clients discover OAuth endpoints**:

1. **Fetch CapabilityStatement**:
   ```http
   GET /metadata HTTP/1.1
   Host: fhir-api.azurewebsites.net
   Accept: application/fhir+json
   ```

2. **Extract OAuth URIs** from `rest.security.extension`:
   ```typescript
   const capabilityStatement = await fetch('https://fhir-api.azurewebsites.net/metadata')
     .then(r => r.json());

   const security = capabilityStatement.rest[0].security;
   const oauthExtension = security.extension.find(e =>
     e.url === 'http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris'
   );

   const authorizeUrl = oauthExtension.extension.find(e => e.url === 'authorize').valueUri;
   const tokenUrl = oauthExtension.extension.find(e => e.url === 'token').valueUri;

   // Use these for OAuth flow
   const authUrl = `${authorizeUrl}?client_id=my-app&scope=patient/*.read&redirect_uri=...`;
   ```

3. **Alternative: Use .well-known/smart-configuration**:
   ```typescript
   const smartConfig = await fetch('https://fhir-api.azurewebsites.net/.well-known/smart-configuration')
     .then(r => r.json());

   // Directly use endpoints
   const authUrl = `${smartConfig.authorization_endpoint}?client_id=my-app&...`;
   ```

### Example: Full SMART Launch Sequence

**Scenario**: Mobile app launches standalone, selects patient

```typescript
// 1. Discover OAuth endpoints
const capabilityStatement = await fhirClient.metadata();
const oauthUris = extractOAuthUris(capabilityStatement);

// 2. Initiate authorization code flow with PKCE
const codeVerifier = generateCodeVerifier();
const codeChallenge = await generateCodeChallenge(codeVerifier);

const authParams = new URLSearchParams({
  client_id: 'mobile-app',
  response_type: 'code',
  redirect_uri: 'myapp://callback',
  scope: 'launch/patient patient/*.read patient/*.write openid fhirUser',
  state: generateState(),
  aud: 'https://fhir-api.azurewebsites.net',
  code_challenge: codeChallenge,
  code_challenge_method: 'S256'
});

// 3. Open browser for user login
const authUrl = `${oauthUris.authorize}?${authParams}`;
await openBrowser(authUrl);

// 4. User logs in, selects patient, redirected back to app
const callbackUrl = await waitForCallback();
const { code, state } = parseCallback(callbackUrl);

// 5. Exchange code for token
const tokenResponse = await fetch(oauthUris.token, {
  method: 'POST',
  headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
  body: new URLSearchParams({
    grant_type: 'authorization_code',
    code: code,
    redirect_uri: 'myapp://callback',
    client_id: 'mobile-app',
    code_verifier: codeVerifier
  })
});

const { access_token, patient, fhirUser } = await tokenResponse.json();

// 6. Use token + patient context for FHIR requests
const observations = await fhirClient.search('Observation', {
  patient: patient,  // Patient ID from token
  code: '8867-4'
}, {
  headers: {
    'Authorization': `Bearer ${access_token}`
  }
});
```

### Testing SMART Discovery

```http
### 1. Get CapabilityStatement
GET https://{{hostname}}/metadata
Accept: application/fhir+json

### Extract security.extension with oauth-uris

### 2. Get .well-known/smart-configuration (alternative)
GET https://{{hostname}}/.well-known/smart-configuration
Accept: application/json

### Expected response:
# {
#   "authorization_endpoint": "https://{{hostname}}/connect/authorize",
#   "token_endpoint": "https://{{hostname}}/connect/token",
#   "scopes_supported": ["patient/*.read", "user/*.read", ...],
#   "capabilities": ["launch-standalone", "permission-patient", ...]
# }
```

### Configuration Per Deployment Scenario

**Scenario 1: Local Dev (OpenIddict)**
- CapabilityStatement advertises: `https://localhost:5001/connect/*`
- .well-known/smart-configuration: Enabled

**Scenario 2a: App Service + OpenIddict**
- CapabilityStatement advertises: `https://fhir-api.azurewebsites.net/connect/*`
- .well-known/smart-configuration: Enabled

**Scenario 2b/3: Entra ID**
- CapabilityStatement advertises: `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/*`
- .well-known/smart-configuration: Proxies to Entra endpoints
- **Note**: Entra doesn't natively support SMART scopes, so you may need custom claims mapping

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

| Aspect | Legacy | V2 (Endpoint Filter) |
|--------|--------|-----|
| **Capability enforcement** | ❌ Not enforced | ✅ Automatically enforced |
| **Authorization layers** | Single check | Pipeline with 5 handlers |
| **Patient filtering** | Manual in queries | Automatic via filter |
| **Performance** | ~2-5ms per request | <1.5ms (cached lookups) |
| **Extensibility** | Hard-coded logic | Pluggable handlers |
| **Multi-tenant** | Not supported | Built-in with isolation |
| **Bundle entry authz** | ❌ Bypass possible | ✅ Same as direct API calls |
| **Auditing** | Separate middleware | Composed endpoint filter |
| **Granularity** | Global middleware | Per-endpoint (composable) |

### ✅ Key Improvements

1. **Capability Contract Enforcement**: Server MUST respect its CapabilityStatement
2. **Layered Security**: Multiple defense layers (auth, RBAC, scopes, capability)
3. **Automatic Data Filtering**: Patient-scoped requests automatically filtered
4. **Performance**: O(1) capability checks via pre-built cache
5. **Extensible**: Easy to add new authorization handlers
6. **Multi-Tenant**: Tenant isolation enforced at authorization layer
7. **Bundle Security**: Bundle entries go through same authz as direct API calls (endpoint filters)
8. **Integrated Auditing**: Auditing via composed endpoint filter (after authz, applies to bundles)
9. **Type-Safe**: Endpoint filters have access to route parameters and endpoint metadata

---

## Consequences

### Positive

1. **FHIR Conformance**: Server behavior matches advertised capabilities
2. **Security in Depth**: Multiple authorization layers (auth → RBAC → SMART → capability)
3. **SMART Compliant**: Full patient/*.read compartment filtering
4. **Performance**: <1.5ms authorization overhead (O(1) capability lookups)
5. **Auditability**: All authorization decisions logged with full context
6. **Bundle Security**: Authorization applies to bundle entries (no bypass via internal routing)
7. **Composable**: Stack filters (authz → audit → validation) per endpoint group
8. **Type-Safe**: Endpoint filters access route parameters and metadata directly

### Negative

1. **Complexity**: More code than simple allow/deny (5 handler pipeline)
2. **Cache Invalidation**: Must rebuild capability cache when statement changes
3. **Configuration**: Requires proper role/permission setup per tenant
4. **Filter Overhead**: Each filter adds ~0.3-0.5ms per request (acceptable for security)

### Mitigation

1. **Incremental Implementation**: Start simple, add handlers as needed (Phase 1.2 → Phase 10)
2. **Cache Management**: Reuse capability cache invalidation from capability statement investigation
3. **Default Deny**: Fail closed for security (if no authz configured, deny all)
4. **Performance Optimization**: Use O(1) lookups (capability cache, frozen collections for roles)
5. **Testing**: Unit test handlers individually, integration test filter composition

### Tradeoffs Accepted

| Tradeoff | Why Acceptable |
|----------|----------------|
| More complex than simple middleware | Security correctness (bundle entries) > simplicity |
| Filter runs per-entry in bundles | Acceptable overhead for transaction/batch integrity |
| Requires handler configuration | Multi-tenant + SMART requires flexibility anyway |
| Cache invalidation complexity | Already solved for CapabilityStatement (reuse pattern) |

---

## References

### Internal Documentation
- Investigation: `smart-on-fhir-v2-implementation.md` (SMART scopes)
- Investigation: `dynamic-capability-statement-generation.md` (Capability caching)
- Investigation: `distributed-messaging-architecture.md` (Event bus for cache invalidation)
- ADR-2503: Phase 1.2 - Search Implementation (basic auth)
- ADR-2506: Phase 3 - Validation (capability enforcement)
- ADR-2513: Phase 10 - SMART on FHIR (full SMART auth)

### External Standards
- SMART App Launch Framework: http://hl7.org/fhir/smart-app-launch/
- FHIR Security: http://hl7.org/fhir/security.html
- OAuth 2.0 RFC 6749: https://datatracker.ietf.org/doc/html/rfc6749
- OpenID Connect Core: https://openid.net/specs/openid-connect-core-1_0.html

### Identity Provider Documentation
- OpenIddict: https://documentation.openiddict.com/
- Microsoft Entra ID: https://learn.microsoft.com/en-us/entra/identity-platform/
- Okta Developer: https://developer.okta.com/docs/
- Auth0 Docs: https://auth0.com/docs

### Reference Implementations
- Microsoft FHIR Server auth config: https://github.com/microsoft/fhir-server/blob/main/src/Microsoft.Health.Fhir.Shared.Web/appsettings.json
- HAPI FHIR Authorization: https://hapifhir.io/hapi-fhir/docs/security/authorization_interceptor.html

---

## Next Steps

1. **Phase 1.2**: Implement basic authorization middleware + authentication handler
2. **Phase 3**: Add capability enforcement handler with interaction cache
3. **Phase 6**: Add tenant isolation handler
4. **Phase 10**: Implement full SMART authorization with compartment filtering
