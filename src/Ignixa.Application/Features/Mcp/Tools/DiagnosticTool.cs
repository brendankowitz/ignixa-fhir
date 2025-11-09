// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace Ignixa.Application.Features.Mcp.Tools;

/// <summary>
/// Diagnostic tool for testing MCP server integration and tenant routing.
/// Phase 1: Spike to validate MapGroup tenant parameter accessibility.
/// </summary>
[McpServerToolType]
public class DiagnosticTool
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DiagnosticTool(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Diagnostic tool to check tenant context resolution.
    /// Returns information about the current HTTP context and route parameters.
    /// </summary>
    [McpServerTool]
    [Description("Diagnostic tool to test tenant context resolution in MCP server")]
    public Task<DiagnosticResult> DiagnoseTenantContextAsync(CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        var result = new DiagnosticResult
        {
            HasHttpContext = httpContext != null,
            RouteParameters = httpContext?.Request.RouteValues
                .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "null")
                ?? new Dictionary<string, string>(),
            TenantContextItem = httpContext?.Items.ContainsKey("TenantContext") == true
                ? httpContext.Items["TenantContext"]?.ToString()
                : null,
            RequestPath = httpContext?.Request.Path.Value ?? "unknown"
        };

        return Task.FromResult(result);
    }
}
