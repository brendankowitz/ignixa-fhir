// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Ignixa.Api.Endpoints.Experimental;

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
        // Phase 1: MCP endpoint with explicit path to avoid conflicts with bundle processing
        // MapMcp("/mcp") creates /mcp endpoint for SSE-based Model Context Protocol
        // MCP streamable HTTP is accessed at POST /mcp (SSE transport)
        // For tenant-aware operation, tools will use tenant resolution from TenantAwareMcpTool
        endpoints.MapMcp("/mcp");

        // Note: MapMcp() without a path defaults to "/" which conflicts with bundle processing
        // Using explicit "/mcp" path prevents the "AmbiguousMatchException" error where both
        // "MCP Streamable HTTP | HTTP: POST /" and "HTTP: POST /" matched the same route.

        return endpoints;
    }
}
