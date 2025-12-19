// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.Configuration;

/// <summary>
/// Configuration options for experimental features.
/// </summary>
public class ExperimentalOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Experimental";

    /// <summary>
    /// Master switch to enable experimental features.
    /// When false, no experimental features are loaded regardless of individual settings.
    /// Default: true (enabled by default in Docker image).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Individual feature configurations.
    /// </summary>
    public ExperimentalFeaturesOptions Features { get; set; } = new();
}

/// <summary>
/// Container for individual experimental feature options.
/// </summary>
public class ExperimentalFeaturesOptions
{
    /// <summary>
    /// MCP (Model Context Protocol) server configuration.
    /// </summary>
    public McpExperimentalOptions Mcp { get; set; } = new();

    /// <summary>
    /// $transform operation configuration.
    /// </summary>
    public TransformExperimentalOptions Transform { get; set; } = new();

    /// <summary>
    /// Terminology operations configuration ($expand, $translate, $subsumes).
    /// </summary>
    public TerminologyExperimentalOptions Terminology { get; set; } = new();

    /// <summary>
    /// Future: $summary operation configuration.
    /// </summary>
    public SummaryExperimentalOptions Summary { get; set; } = new();
}

/// <summary>
/// Configuration options for MCP (Model Context Protocol) server.
/// </summary>
public class McpExperimentalOptions
{
    /// <summary>
    /// Whether MCP server is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Transport protocol for MCP server (e.g., "http").
    /// </summary>
    public string Transport { get; set; } = "http";
}

/// <summary>
/// Configuration options for $transform operation.
/// </summary>
public class TransformExperimentalOptions
{
    /// <summary>
    /// Whether $transform operation is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Timeout in seconds for transformation operations.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Configuration options for terminology operations.
/// </summary>
public class TerminologyExperimentalOptions
{
    /// <summary>
    /// Whether terminology operations are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to automatically import terminology packages on startup.
    /// </summary>
    public bool EnableAutoImport { get; set; }
}

/// <summary>
/// Configuration options for $summary operation (future feature).
/// </summary>
public class SummaryExperimentalOptions
{
    /// <summary>
    /// Whether $summary operation is enabled.
    /// Default: false (not yet implemented).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Maximum number of resources to include in summary.
    /// </summary>
    public int MaxResources { get; set; } = 1000;

    /// <summary>
    /// Allowed resource types for summary.
    /// </summary>
    public ICollection<string> AllowedResourceTypes { get; } = [];
}
