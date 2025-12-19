# Investigation: Model Context Protocol (MCP) Integration Architecture

**Feature**: mcp-integration
**Status**: Proposed
**Created**: 2025-11-08

---

## Executive Summary

This ADR proposes integrating a **Model Context Protocol (MCP) server** into the Ignixa FHIR Server to enable AI agents and LLM applications to interact with FHIR data through a standardized, tool-based interface.

### What is MCP?

**Model Context Protocol (MCP)** is an open protocol that standardizes how applications provide context to Large Language Models (LLMs). It enables secure integration between LLMs and various data sources and tools through three primary concepts:

1. **Tools**: Functions that LLMs can invoke (e.g., search patients, download packages)
2. **Resources**: Data sources exposed to clients (e.g., FHIR resources, terminology ValueSets)
3. **Prompts**: Reusable message templates for common workflows

### Why MCP for FHIR?

**Use Cases**:

- **Clinical AI Assistants**: LLM-powered chatbots querying patient data via natural language
- **Administrative Automation**: AI agents managing package downloads, job monitoring, data quality checks
- **Research & Analytics**: AI-assisted cohort identification, data exploration, terminology lookups
- **Developer Tools**: Claude Code/GitHub Copilot integration for FHIR API development

**Business Value**:

- Lower barrier to entry for AI applications consuming FHIR data
- Standardized tool interface (vs. custom REST API integration for each LLM)
- Tenant-aware AI operations with built-in security
- Admin operations accessible to AI agents (package management, job monitoring)

### Proposed Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Ignixa FHIR Server (ASP.NET Core)           │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │              Existing FHIR API (REST)                      │ │
│  │  /tenant/{id}/Patient, /metadata, /$export, /$import      │ │
│  └────────────────────────────────────────────────────────────┘ │
│                              ↓ reuses                            │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │         MCP Server Layer (Sub-Application at /mcp)         │ │
│  │                                                              │ │
│  │  Transport: SSE (Server-Sent Events) over HTTPS            │ │
│  │  Endpoint: /mcp/sse (tenant-aware or tenant-explicit)      │ │
│  │                                                              │ │
│  │  ┌──────────────────────────────────────────────────────┐  │ │
│  │  │ MCP Tools (LLM-invokable functions)                  │  │ │
│  │  ├──────────────────────────────────────────────────────┤  │ │
│  │  │ FHIR Operations:                                     │  │ │
│  │  │ - search_resources(resourceType, params, tenantId)  │  │ │
│  │  │ - get_resource(resourceType, id, tenantId)          │  │ │
│  │  │ - validate_resource(resource, profile, tenantId)    │  │ │
│  │  │                                                       │  │ │
│  │  │ Terminology Operations:                              │  │ │
│  │  │ - expand_valueset(url, tenantId)                    │  │ │
│  │  │ - validate_code(system, code, valueSetUrl)          │  │ │
│  │  │ - lookup_code(system, code)                         │  │ │
│  │  │                                                       │  │ │
│  │  │ Package Management (Admin):                          │  │ │
│  │  │ - list_packages(tenantId)                           │  │ │
│  │  │ - download_package(packageId, version, tenantId)    │  │ │
│  │  │ - get_package_status(packageId, tenantId)           │  │ │
│  │  │                                                       │  │ │
│  │  │ Background Jobs (Admin):                             │  │ │
│  │  │ - list_export_jobs(tenantId, status)                │  │ │
│  │  │ - get_job_status(jobId, tenantId)                   │  │ │
│  │  │ - cancel_job(jobId, tenantId)                       │  │ │
│  │  │                                                       │  │ │
│  │  │ Metadata Operations:                                 │  │ │
│  │  │ - get_capability_statement(tenantId)                │  │ │
│  │  │ - list_search_parameters(resourceType, tenantId)    │  │ │
│  │  └──────────────────────────────────────────────────────┘  │ │
│  │                                                              │ │
│  │  ┌──────────────────────────────────────────────────────┐  │ │
│  │  │ MCP Resources (Data Streams)                         │  │ │
│  │  ├──────────────────────────────────────────────────────┤  │ │
│  │  │ - fhir://tenant/{id}/Patient/{id}                   │  │ │
│  │  │ - fhir://tenant/{id}/packages/{packageId}           │  │ │
│  │  │ - fhir://tenant/{id}/jobs/{jobId}                   │  │ │
│  │  │ - fhir://terminology/valueset/{canonical}           │  │ │
│  │  └──────────────────────────────────────────────────────┘  │ │
│  │                                                              │ │
│  │  ┌──────────────────────────────────────────────────────┐  │ │
│  │  │ MCP Prompts (Workflow Templates)                     │  │ │
│  │  ├──────────────────────────────────────────────────────┤  │ │
│  │  │ - find_patient_by_identifier(system, value)         │  │ │
│  │  │ - analyze_validation_errors(resource)               │  │ │
│  │  │ - explain_search_parameters(resourceType)           │  │ │
│  │  └──────────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────────┘ │
│                              ↓ uses                              │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │         Shared Infrastructure (Medino, DI, Caching)        │ │
│  │  - IMediator (reuse existing handlers)                     │ │
│  │  - ITenantConfigurationStore (tenant resolution)           │ │
│  │  - IFhirRepositoryFactory (data access)                    │ │
│  │  - ISearchServiceFactory (search operations)               │ │
│  │  - IBackgroundJobRepository (job management)               │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘

                              ↑ connects via SSE

┌─────────────────────────────────────────────────────────────────┐
│                       AI Clients (MCP Protocol)                 │
│                                                                  │
│  - Claude Desktop / Claude Code                                 │
│  - GitHub Copilot Extensions                                    │
│  - Custom LLM Applications                                      │
│  - AI-powered Clinical Decision Support Tools                  │
└─────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Sub-path hosting at `/mcp`** | Isolates MCP transport from FHIR REST API, allows independent versioning |
| **SSE transport (not STDIO)** | STDIO requires separate process; SSE integrates cleanly with ASP.NET Core |
| **Tenant-aware tools** | All MCP tools accept `tenantId` parameter for multi-tenant security |
| **Reuse Medino handlers** | No duplication; MCP tools invoke existing `SearchResourcesHandler`, etc. |
| **Admin-focused initial scope** | Package management and job monitoring provide immediate value |
| **Read-only FHIR operations (Phase 1)** | Safer for AI agents; write operations in Phase 2+ |

---

## Context

### Current State Analysis

**Ignixa FHIR Server v2 (as of Phase 23)**:

- ✅ Multi-tenant architecture (Isolated mode)
- ✅ RESTful FHIR API (R4/R4B/R5/STU3)
- ✅ Background jobs via DurableTask ($export, $import)
- ✅ Medino-based query/command handlers
- ✅ Search with 100+ search parameters per resource type
- ✅ Validation framework (Tier 1-3)
- ✅ Terminology services (basic, in-memory)
- ❌ **No AI/LLM integration layer**
- ❌ **No standardized tool interface for external agents**

**ADR-2532 Package Management (Proposed)**:

- ✅ Design complete for NPM package loading (US Core, mCODE, etc.)
- ✅ Admin API spec for downloading packages, checking status
- ❌ Implementation pending (Phase 1: 3-4 weeks)

**Gap**: While the FHIR REST API is comprehensive, **AI agents struggle to consume it** because:

1. No standardized tool schema (each LLM needs custom integration)
2. Complex query syntax (search parameters, chaining, modifiers)
3. Authentication/authorization requires custom implementation per client
4. Admin operations (package management, job monitoring) not exposed in CapabilityStatement

### Why MCP?

**Model Context Protocol** solves these problems by:

- Providing a **standard tool schema** (JSON Schema for inputs/outputs)
- Supporting **server-driven discovery** (LLMs query available tools at runtime)
- Enabling **streaming responses** (SSE transport for large datasets)
- Offering **tenant-aware security** (tools enforce multi-tenancy)

### MCP .NET SDK Status

**Official SDK** (`ModelContextProtocol` on NuGet):

- ✅ Preview release (0.4.0-preview.3 as of Jan 2025)
- ✅ ASP.NET Core integration via `ModelContextProtocol.AspNetCore`
- ✅ SSE transport support (`MapMcpHttpSse()`)
- ✅ Tool registration via attributes or delegates
- ✅ JSON Schema generation for tool inputs
- ⚠️ **Preview status**: Breaking changes possible
- ⚠️ **Limited documentation**: Community examples exist, official docs sparse

**Integration Constraints**:

1. **Separate endpoint required**: MCP server needs dedicated endpoint (can't share with FHIR REST routes)
2. **Transport choice**: SSE recommended over STDIO for ASP.NET Core apps
3. **Dependency injection**: MCP server can access existing DI container (✅ reuse Medino, repositories)
4. **No conflict with existing middleware**: MCP endpoints can coexist with FHIR endpoints

---

## Decision

### Phased Implementation

Implement MCP integration in **3 phases** over 6-8 weeks:

| Phase | Duration | Scope | Deliverables |
|-------|----------|-------|--------------|
| **Phase 1: Foundation** | 2-3 weeks | MCP server setup, basic FHIR tools | `/mcp/sse` endpoint, `search_resources`, `get_resource` tools |
| **Phase 2: Admin Operations** | 2-3 weeks | Package management, job monitoring tools | `list_packages`, `download_package`, `list_export_jobs` tools |
| **Phase 3: Advanced** | 2 weeks | Terminology, validation, prompts | `expand_valueset`, validation tools, workflow prompts |

### Architecture

#### 1. MCP Server Hosting (Sub-Application Pattern)

**Route Structure**:

```
/mcp/sse         → MCP SSE endpoint (tenant-agnostic, requires auth)
/tenant/{id}/mcp/sse → MCP SSE endpoint (tenant-explicit)
```

**Integration Point** (`Program.cs`):

```csharp
// After existing FHIR endpoint mappings
app.MapMetadataEndpoints();

// NEW: MCP Server Integration (Phase 1)
app.MapMcpServer(); // Extension method in Ignixa.Api/Endpoints/McpEndpoints.cs
```

**Implementation** (`McpEndpoints.cs`):

```csharp
public static class McpEndpoints
{
    public static IEndpointRouteBuilder MapMcpServer(this IEndpointRouteBuilder endpoints)
    {
        // Register MCP services (if not already registered in Program.cs)
        // This is typically done in ConfigureContainer, but can be done here for isolation

        // Tenant-explicit endpoint (always supported)
        endpoints.MapGroup("/tenant/{tenantId:int}/mcp")
            .MapMcpHttpSse("/sse", ConfigureMcpServer);

        // Tenant-agnostic endpoint (single-tenant mode only, handled by middleware)
        endpoints.MapGroup("/mcp")
            .MapMcpHttpSse("/sse", ConfigureMcpServer);

        return endpoints;
    }

    private static void ConfigureMcpServer(IMcpServerBuilder builder)
    {
        builder
            .WithName("Ignixa FHIR Server")
            .WithVersion("2.0.0")
            .WithToolsFromAssembly(typeof(McpEndpoints).Assembly, "Ignixa.Api.Mcp.Tools")
            .WithResourcesFromAssembly(typeof(McpEndpoints).Assembly, "Ignixa.Api.Mcp.Resources")
            .WithPromptsFromAssembly(typeof(McpEndpoints).Assembly, "Ignixa.Api.Mcp.Prompts");
    }
}
```

#### 2. Tenant Resolution Strategy

**Challenge**: MCP clients connect via SSE to `/mcp/sse` or `/tenant/{id}/mcp/sse`. How do tools know which tenant to operate on?

**Solution**: Three-tier tenant resolution:

1. **Explicit route parameter** (`/tenant/{id}/mcp/sse`):
   - TenantId extracted from route
   - Stored in `HttpContext.Items["TenantContext"]` by `TenantResolutionMiddleware`
   - Tools inherit tenant context from connection

2. **Tool parameter** (all tools accept `tenantId` parameter):
   - LLM passes `tenantId` explicitly in tool invocation
   - Overrides connection-level tenant (for multi-tenant admin users)

3. **Default tenant** (single-tenant mode only):
   - If only 1 active tenant exists, use it as default
   - Middleware sets default tenant in `HttpContext.Items["DefaultTenant"]`

**Example Tool Signature**:

```csharp
[McpTool("search_resources")]
public async Task<SearchResultsDto> SearchResourcesAsync(
    string resourceType,
    Dictionary<string, string> searchParams,
    int? tenantId = null, // Optional if connecting via /tenant/{id}/mcp/sse
    CancellationToken ct = default)
{
    // 1. Resolve tenant: explicit parameter > connection context > default
    var resolvedTenantId = tenantId
        ?? _httpContextAccessor.HttpContext?.Items["TenantContext"] as int?
        ?? _httpContextAccessor.HttpContext?.Items["DefaultTenant"] as int?
        ?? throw new InvalidOperationException("Tenant not specified");

    // 2. Invoke existing Medino handler
    var query = new SearchResourcesQuery(
        TenantId: resolvedTenantId,
        ResourceType: resourceType,
        SearchParameters: searchParams);

    var result = await _mediator.SendAsync(query, ct);

    // 3. Map to MCP-friendly DTO
    return MapToDto(result);
}
```

#### 3. MCP Tools (LLM-Invokable Functions)

**Tool Categories**:

##### Category 1: FHIR Read Operations (Phase 1)

| Tool Name | Description | Inputs | Outputs |
|-----------|-------------|--------|---------|
| `search_resources` | Search FHIR resources by type and parameters | `resourceType`, `searchParams` (dict), `tenantId?` | `SearchResultsDto` (entries, total, links) |
| `get_resource` | Get single FHIR resource by ID | `resourceType`, `id`, `tenantId?` | `ResourceDto` (JSON) |
| `get_resource_history` | Get version history for a resource | `resourceType`, `id`, `tenantId?` | `HistoryResultDto` |

**JSON Schema Example** (generated by MCP SDK):

```json
{
  "name": "search_resources",
  "description": "Search FHIR resources by type and search parameters. Returns a Bundle of matching resources.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "resourceType": {
        "type": "string",
        "description": "FHIR resource type (e.g., Patient, Observation)",
        "enum": ["Patient", "Observation", "Condition", "..."]
      },
      "searchParams": {
        "type": "object",
        "description": "Search parameters as key-value pairs (e.g., {'name': 'John', 'birthdate': 'gt2000-01-01'})",
        "additionalProperties": { "type": "string" }
      },
      "tenantId": {
        "type": "integer",
        "description": "Tenant ID (optional if connected via /tenant/{id}/mcp/sse)"
      }
    },
    "required": ["resourceType", "searchParams"]
  }
}
```

##### Category 2: Admin Operations - Package Management (Phase 2)

**Based on ADR-2532 Package Management Spec**:

| Tool Name | Description | Inputs | Outputs |
|-----------|-------------|--------|---------|
| `list_packages` | List installed FHIR packages (IGs) for a tenant | `tenantId?` | `PackageListDto[]` (id, version, status, resources) |
| `download_package` | Download and install a FHIR NPM package | `packageId`, `version`, `tenantId?` | `PackageDownloadResultDto` (jobId, status) |
| `get_package_status` | Check installation status of a package | `packageId`, `tenantId?` | `PackageStatusDto` (status, progress, errors) |
| `get_package_resources` | List resources in a package (StructureDefinition, ValueSet, etc.) | `packageId`, `resourceType?`, `tenantId?` | `PackageResourceDto[]` |

**Example Implementation**:

```csharp
[McpTool("download_package")]
[McpToolDescription("Download and install a FHIR NPM package (e.g., US Core 5.0.1). Returns a background job ID.")]
public async Task<PackageDownloadResultDto> DownloadPackageAsync(
    [McpToolParameter("packageId", "NPM package ID (e.g., 'hl7.fhir.us.core')")]
    string packageId,

    [McpToolParameter("version", "Package version (e.g., '5.0.1')")]
    string version,

    [McpToolParameter("tenantId", "Tenant ID (optional if connected via /tenant/{id}/mcp/sse)")]
    int? tenantId = null,

    CancellationToken ct = default)
{
    var resolvedTenantId = ResolveTenantId(tenantId);

    // Invoke LoadPackageCommand (from ADR-2532 design)
    var command = new LoadPackageCommand(
        PackageId: packageId,
        Version: version,
        TenantId: resolvedTenantId);

    var result = await _mediator.SendAsync(command, ct);

    return new PackageDownloadResultDto
    {
        PackageId = packageId,
        Version = version,
        JobId = result.JobId,
        Status = result.Status,
        Message = result.Message
    };
}
```

##### Category 3: Admin Operations - Background Jobs (Phase 2)

| Tool Name | Description | Inputs | Outputs |
|-----------|-------------|--------|---------|
| `list_export_jobs` | List bulk export jobs for a tenant | `tenantId?`, `status?` (active/completed/failed) | `ExportJobDto[]` |
| `list_import_jobs` | List bulk import jobs for a tenant | `tenantId?`, `status?` | `ImportJobDto[]` |
| `get_job_status` | Get detailed status of a background job | `jobId`, `tenantId?` | `JobStatusDto` (status, progress, errors, outputUrls) |
| `cancel_job` | Cancel a running background job | `jobId`, `tenantId?` | `JobCancelResultDto` |
| `download_job_output` | Get download URLs for completed job outputs | `jobId`, `tenantId?` | `JobOutputDto` (urls[], expiresAt) |

**Use Case**: AI agent monitoring long-running exports

```
LLM: "Check if the patient export I started 10 minutes ago is complete"
  → Invokes: list_export_jobs(tenantId=1, status="active")
  → Returns: [{ jobId: "abc123", status: "Running", progress: "65%" }]

LLM: "Download the results when it's done"
  → (polls get_job_status every 30s)
  → When status="Completed":
    → Invokes: download_job_output(jobId="abc123")
    → Returns: { urls: ["https://.../patients_1.ndjson"], expiresAt: "..." }
```

##### Category 4: Terminology Operations (Phase 3)

| Tool Name | Description | Inputs | Outputs |
|-----------|-------------|--------|---------|
| `expand_valueset` | Expand a ValueSet to get list of valid codes | `valueSetUrl`, `filter?`, `count?`, `tenantId?` | `ValueSetExpansionDto` |
| `validate_code` | Check if a code is valid in a ValueSet | `system`, `code`, `valueSetUrl`, `tenantId?` | `CodeValidationDto` (valid, display, message) |
| `lookup_code` | Get details about a code | `system`, `code`, `tenantId?` | `CodeLookupDto` (display, definition, properties) |

##### Category 5: Validation Operations (Phase 3)

| Tool Name | Description | Inputs | Outputs |
|-----------|-------------|--------|---------|
| `validate_resource` | Validate a FHIR resource against a profile | `resource` (JSON), `profile?`, `tenantId?` | `ValidationResultDto` (valid, issues[]) |
| `explain_validation_errors` | Get human-readable explanations for validation errors | `resource`, `profile?`, `tenantId?` | `ValidationExplanationDto` |

#### 4. MCP Resources (Data Streams)

**Resources** expose data URIs that clients can subscribe to. Unlike tools (which are invoked on-demand), resources provide **streaming access** to data.

**FHIR Resource URIs**:

```
fhir://tenant/1/Patient/12345        → Specific patient resource
fhir://tenant/1/Observation?patient=12345 → Search results as stream
fhir://tenant/1/packages/hl7.fhir.us.core → Package metadata
fhir://tenant/1/jobs/export-abc123   → Job status (updates in real-time)
```

**Implementation** (Phase 3):

```csharp
[McpResource("fhir://tenant/{tenantId}/Patient/{id}")]
public async Task<ResourceDto> GetPatientResourceAsync(int tenantId, string id)
{
    var query = new GetResourceQuery(TenantId: tenantId, ResourceType: "Patient", Id: id);
    var result = await _mediator.SendAsync(query);
    return MapToDto(result);
}
```

#### 5. MCP Prompts (Workflow Templates)

**Prompts** are reusable message templates that guide LLMs through common workflows.

**Example Prompts**:

1. **Find Patient by Identifier**:

```yaml
name: find_patient_by_identifier
description: Search for a patient using an identifier (MRN, SSN, etc.)
arguments:
  - name: system
    description: Identifier system (e.g., http://hospital.org/mrn)
  - name: value
    description: Identifier value (e.g., 12345)
  - name: tenantId
    description: Tenant ID
template: |
  Use the search_resources tool to find a patient with identifier system={{system}} and value={{value}}.
  If multiple patients match, ask the user to clarify.
  If no patients match, inform the user and ask if they want to create a new patient.
```

2. **Analyze Validation Errors**:

```yaml
name: analyze_validation_errors
description: Explain validation errors in plain language
arguments:
  - name: resource
    description: FHIR resource JSON
template: |
  Call validate_resource with the provided resource.
  For each error in the response:
  1. Explain what the error means in plain language
  2. Show the problematic field path
  3. Suggest how to fix it
  Present the results as a numbered list.
```

---

## Multi-Tenancy & Security

### Tenant Isolation

**Requirements**:

1. MCP tools MUST enforce tenant boundaries (no cross-tenant access)
2. All tools accept `tenantId` parameter (explicit or from connection context)
3. Tenant validation happens before invoking Medino handlers

**Implementation**:

```csharp
public abstract class TenantAwareMcpTool
{
    protected readonly IHttpContextAccessor _httpContextAccessor;
    protected readonly ITenantConfigurationStore _tenantStore;

    protected async Task<int> ResolveTenantIdAsync(int? explicitTenantId, CancellationToken ct)
    {
        // Priority 1: Explicit parameter
        if (explicitTenantId.HasValue)
        {
            await ValidateTenantAccessAsync(explicitTenantId.Value, ct);
            return explicitTenantId.Value;
        }

        // Priority 2: Connection-level tenant (from route /tenant/{id}/mcp/sse)
        if (_httpContextAccessor.HttpContext?.Items["TenantContext"] is int routeTenantId)
        {
            return routeTenantId;
        }

        // Priority 3: Default tenant (single-tenant mode only)
        if (_httpContextAccessor.HttpContext?.Items["DefaultTenant"] is int defaultTenantId)
        {
            return defaultTenantId;
        }

        throw new UnauthorizedAccessException("Tenant not specified and cannot be inferred");
    }

    protected async Task ValidateTenantAccessAsync(int tenantId, CancellationToken ct)
    {
        var tenant = await _tenantStore.GetTenantConfigurationAsync(tenantId, ct);
        if (tenant == null || !tenant.IsActive)
        {
            throw new UnauthorizedAccessException($"Tenant {tenantId} not found or inactive");
        }

        // TODO: Check user claims/permissions (Phase 2)
        // if (!_httpContextAccessor.HttpContext.User.HasClaim("tenant", tenantId.ToString()))
        //     throw new UnauthorizedAccessException("User not authorized for tenant");
    }
}
```

### Authentication & Authorization

**Phase 1** (Development):

- No authentication (localhost only)
- Tenant isolation enforced by route structure

**Phase 2** (Production):

- OAuth 2.0 / OpenID Connect (same as FHIR REST API)
- API keys with tenant scope
- Claims-based authorization (`tenant:{id}` claim required)

**Phase 3** (Enterprise):

- Per-tool authorization (`can:download_package`, `can:cancel_job`)
- Audit logging (all MCP tool invocations logged to `AuditLogger`)

---

## Implementation Phases

### Phase 1: Foundation (2-3 weeks)

**Week 1: MCP Server Setup**

- [ ] Add `ModelContextProtocol.AspNetCore` NuGet package
- [ ] Create `McpEndpoints.cs` with `/mcp/sse` endpoint mapping
- [ ] Register MCP services in `Program.cs` DI container
- [ ] Create `TenantAwareMcpTool` base class
- [ ] Test MCP connection with Claude Desktop

**Week 2: Basic FHIR Tools**

- [ ] Implement `search_resources` tool (uses `SearchResourcesHandler`)
- [ ] Implement `get_resource` tool (uses `GetResourceHandler`)
- [ ] Implement `get_resource_history` tool (uses `GetResourceHistoryHandler`)
- [ ] Create DTOs for MCP responses (`SearchResultsDto`, `ResourceDto`)
- [ ] Write integration tests (MCP client → tool → Medino handler)

**Week 3: Documentation & Testing**

- [ ] Document MCP tool usage in `docs/mcp-integration.md`
- [ ] Create example MCP client configurations (Claude Desktop, Postman)
- [ ] Test multi-tenant isolation (tenant A cannot access tenant B data)
- [ ] Performance testing (tool invocation latency <500ms)

**Deliverables**:

- ✅ Working MCP server at `/mcp/sse`
- ✅ 3 basic FHIR read tools
- ✅ Tenant isolation enforced
- ✅ Integration tests passing

### Phase 2: Admin Operations (2-3 weeks)

**Week 4: Package Management Tools**

- [ ] Implement `list_packages` tool
- [ ] Implement `download_package` tool (triggers background job)
- [ ] Implement `get_package_status` tool
- [ ] Implement `get_package_resources` tool
- [ ] Create `PackageManagementService` (wraps ADR-2532 handlers)

**Week 5: Background Job Tools**

- [ ] Implement `list_export_jobs` tool
- [ ] Implement `list_import_jobs` tool
- [ ] Implement `get_job_status` tool (polls `IBackgroundJobRepository`)
- [ ] Implement `cancel_job` tool
- [ ] Implement `download_job_output` tool (returns blob URLs)

**Week 6: Production Hardening**

- [ ] Add authentication (OAuth 2.0 integration)
- [ ] Add authorization (claims-based, per-tool permissions)
- [ ] Add audit logging (all tool invocations logged)
- [ ] Add rate limiting (prevent abuse)
- [ ] Load testing (100 concurrent MCP connections)

**Deliverables**:

- ✅ 9 admin operation tools (package + job management)
- ✅ Authentication & authorization working
- ✅ Audit logging enabled
- ✅ Production-ready

### Phase 3: Advanced Features (2 weeks)

**Week 7: Terminology & Validation Tools**

- [ ] Implement `expand_valueset` tool (uses ADR-2532 terminology service)
- [ ] Implement `validate_code` tool
- [ ] Implement `lookup_code` tool
- [ ] Implement `validate_resource` tool
- [ ] Implement `explain_validation_errors` tool (LLM-friendly explanations)

**Week 8: Resources & Prompts**

- [ ] Implement MCP resources (`fhir://tenant/{id}/...` URIs)
- [ ] Implement 3-5 workflow prompts (find patient, analyze errors, etc.)
- [ ] Create AI agent examples (Claude Code integration, GitHub Copilot)
- [ ] Document best practices for LLM tool usage

**Deliverables**:

- ✅ 5 terminology/validation tools
- ✅ MCP resources for streaming data access
- ✅ Workflow prompts for common scenarios
- ✅ Example AI agent integrations

---

## Success Criteria

### Phase 1

- ✅ MCP server responds to `tools/list` request with 3 FHIR tools
- ✅ Claude Desktop can connect and invoke `search_resources` successfully
- ✅ Multi-tenant isolation prevents cross-tenant access
- ✅ Tool invocation latency <500ms (p95)

### Phase 2

- ✅ Package download triggered via `download_package` tool completes successfully
- ✅ Export job status monitored via `get_job_status` tool updates in real-time
- ✅ Authentication prevents unauthorized access
- ✅ Audit log contains all tool invocations with tenant/user context

### Phase 3

- ✅ ValueSet expansion returns 100+ codes via `expand_valueset`
- ✅ Validation errors explained in plain language via `explain_validation_errors`
- ✅ MCP resources stream data without buffering entire dataset
- ✅ Workflow prompts reduce LLM token usage by 30% vs. raw tool calls

---

## File Structure

```
src/
├── Ignixa.Api/
│   ├── Program.cs                          (add app.MapMcpServer())
│   ├── Endpoints/
│   │   └── McpEndpoints.cs                 (MCP server configuration)
│   └── Mcp/
│       ├── Tools/
│       │   ├── TenantAwareMcpTool.cs       (base class)
│       │   ├── FhirOperations/
│       │   │   ├── SearchResourcesTool.cs
│       │   │   ├── GetResourceTool.cs
│       │   │   └── GetResourceHistoryTool.cs
│       │   ├── PackageManagement/
│       │   │   ├── ListPackagesTool.cs
│       │   │   ├── DownloadPackageTool.cs
│       │   │   ├── GetPackageStatusTool.cs
│       │   │   └── GetPackageResourcesTool.cs
│       │   ├── BackgroundJobs/
│       │   │   ├── ListExportJobsTool.cs
│       │   │   ├── GetJobStatusTool.cs
│       │   │   └── CancelJobTool.cs
│       │   └── Terminology/
│       │       ├── ExpandValueSetTool.cs
│       │       ├── ValidateCodeTool.cs
│       │       └── LookupCodeTool.cs
│       ├── Resources/
│       │   ├── FhirResourceProvider.cs     (fhir:// URIs)
│       │   ├── PackageResourceProvider.cs
│       │   └── JobResourceProvider.cs
│       ├── Prompts/
│       │   ├── FindPatientPrompt.cs
│       │   ├── AnalyzeValidationErrorsPrompt.cs
│       │   └── ExplainSearchParametersPrompt.cs
│       └── Dtos/
│           ├── SearchResultsDto.cs
│           ├── ResourceDto.cs
│           ├── PackageListDto.cs
│           ├── JobStatusDto.cs
│           └── ValidationResultDto.cs
│
├── Ignixa.Application/
│   └── Features/
│       └── Packages/                       (from ADR-2532)
│           ├── LoadPackageCommand.cs
│           ├── LoadPackageHandler.cs
│           ├── ListPackagesQuery.cs
│           └── ListPackagesHandler.cs
│
└── test/
    └── Ignixa.Api.Tests/
        └── Mcp/
            ├── FhirOperationsToolsTests.cs
            ├── PackageManagementToolsTests.cs
            ├── BackgroundJobToolsTests.cs
            └── TenantIsolationTests.cs
```

---

## Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **MCP SDK breaking changes** | High (requires rework) | MEDIUM | Pin to specific preview version, monitor GitHub releases |
| **Performance issues (SSE overhead)** | Medium (latency spikes) | LOW | Use streaming responses, implement caching, load test early |
| **Tenant isolation bugs** | HIGH (data leakage) | LOW | Comprehensive integration tests, security review, audit logging |
| **LLM token costs** | Medium (expensive queries) | MEDIUM | Implement result pagination, caching, prompt optimization |
| **Authentication complexity** | Medium (delayed production) | MEDIUM | Start with API keys (Phase 1), defer OAuth to Phase 2 |
| **Scope creep (write operations)** | Medium (delays delivery) | HIGH | Strict Phase 1 scope (read-only), defer writes to Phase 2+ |

---

## Alternatives Considered

### Alternative 1: Custom REST API for AI Agents

**Pros**: Full control, no dependency on MCP SDK

**Cons**: Every LLM client needs custom integration, no standardization

**Decision**: Rejected - MCP provides standard tool schema and discovery

### Alternative 2: STDIO Transport (Separate Process)

**Pros**: True isolation, easier to sandbox

**Cons**: Complex deployment, can't reuse ASP.NET Core DI, inter-process communication overhead

**Decision**: Rejected - SSE transport integrates cleanly with existing ASP.NET Core app

### Alternative 3: GraphQL API

**Pros**: Flexible query language, good for AI agents

**Cons**: Doesn't solve tool discovery problem, not standard for LLMs

**Decision**: Rejected - MCP is purpose-built for LLM integration

### Alternative 4: Embed MCP Server in Client Applications

**Pros**: No server changes needed

**Cons**: Duplicates business logic, no central administration, no multi-tenancy

**Decision**: Rejected - Server-side MCP provides centralized control and security

---

## Open Questions

1. **Billing & Rate Limiting**: Should MCP tool invocations count against tenant API quotas?

   **Recommendation**: Yes, track via audit log and enforce same rate limits as REST API

2. **Versioning**: How to handle MCP API versioning (vs. FHIR API versioning)?

   **Recommendation**: Use semantic versioning for MCP tools, advertise in server info

3. **Streaming Large Results**: How to handle 10,000+ search results via MCP?

   **Recommendation**: Implement pagination in tools, return continuation tokens

4. **Write Operations**: Should Phase 2+ include `create_resource`, `update_resource` tools?

   **Recommendation**: Yes, but require explicit user confirmation (LLM cannot auto-approve writes)

5. **Integration with Vector Search** (future ADR-2541):

   **Recommendation**: Add `semantic_search` tool when vector embeddings implemented

---

## Conclusion

Integrating a **Model Context Protocol (MCP) server** into the Ignixa FHIR Server provides:

✅ **Standardized AI Integration**: LLMs connect via standard protocol (no custom integrations)

✅ **Admin Operations Exposed**: Package management and job monitoring accessible to AI agents

✅ **Multi-Tenant Security**: Tenant isolation enforced at tool level

✅ **Reuses Existing Infrastructure**: MCP tools invoke existing Medino handlers (no duplication)

✅ **Phased Delivery**: 3 phases over 6-8 weeks, each delivering value incrementally

**Recommendation**: **Proceed with Phase 1 immediately** (2-3 weeks). The foundation (MCP server setup, basic FHIR tools) enables AI-powered exploration of FHIR data with minimal risk. Phase 2 (admin operations) aligns with ADR-2532 package management implementation, and Phase 3 (advanced features) can be prioritized based on user feedback.

---

## Next Steps

1. **Approve this ADR** and allocate Phase 1 to next sprint
2. **Install `ModelContextProtocol.AspNetCore`** NuGet package (preview)
3. **Create spike**: Test MCP SSE endpoint with Claude Desktop (1-2 days)
4. **Begin Phase 1 implementation** (2-3 weeks)
5. **Gather user feedback** from AI agent developers
6. **Plan Phase 2** based on ADR-2532 package management timeline

---

**Document Status**: PROPOSED

**Last Updated**: 2025-11-08

**Next Review**: After Phase 1 spike completion

**Owner**: Ignixa Development Team
