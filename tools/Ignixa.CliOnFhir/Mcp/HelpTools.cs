// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>
/// The <c>help</c> tool: the detail the short tool descriptions leave out, on demand. A tool rather than an MCP resource
/// because agent clients call tools on their own but rarely read resources without being told to.
/// </summary>
[McpServerToolType]
public sealed class HelpTools
{
    [McpServerTool(Name = "help", ReadOnly = true, Idempotent = true)]
    [Description("Explains the other tools in depth. topic: search, columns, fhirpath, cohorts, patient_search, hierarchies, paging, references, issues, plan, patient_context, terminology, drug_classes, validate or folder.")]
    public static CallToolResult Help(string topic) =>
        HelpTopics.All.TryGetValue(topic.Trim(), out string? text)
            ? new CallToolResult { Content = [new TextContentBlock { Text = text }] }
            : new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Unknown topic. Topics: {string.Join(", ", HelpTopics.All.Keys.Order(StringComparer.Ordinal))}." }],
                IsError = true,
            };
}
