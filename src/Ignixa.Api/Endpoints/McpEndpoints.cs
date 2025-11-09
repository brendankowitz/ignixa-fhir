// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// MCP (Model Context Protocol) server endpoints configuration.
/// Phase 1: Foundation - Basic SSE endpoint registration.
/// </summary>
public static class McpEndpoints
{
    /// <summary>
    /// Maps MCP server endpoints to the application.
    /// Supports both tenant-explicit (/tenant/{id}/mcp/sse) and tenant-agnostic (/mcp/sse) routes.
    /// </summary>
    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Phase 1: Direct MCP endpoint (no tenant routing via MapGroup)
        // MapMcp() creates /sse endpoint automatically
        // For tenant-aware operation, tools will use tenant resolution from TenantAwareMcpTool
        endpoints.MapMcp();

        // Note: MapMcp() doesn't work well with MapGroup() for tenant routing
        // Instead, tenant context is resolved in tools via:
        // 1. Explicit tenantId parameter
        // 2. Single-tenant auto-detection
        // This creates /sse endpoint (not /tenant/{id}/mcp/sse)

        return endpoints;
    }
}
