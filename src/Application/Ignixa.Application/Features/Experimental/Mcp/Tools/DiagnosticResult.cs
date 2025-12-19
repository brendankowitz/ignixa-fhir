// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.Mcp.Tools;

/// <summary>
/// Result model for diagnostic tool.
/// </summary>
public class DiagnosticResult
{
    public bool HasHttpContext { get; init; }
    public Dictionary<string, string> RouteParameters { get; init; } = new();
    public string? TenantContextItem { get; init; }
    public string RequestPath { get; init; } = string.Empty;
}
