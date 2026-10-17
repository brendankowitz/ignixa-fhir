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
/// because agent clients call tools on their own but rarely read resources without being told to. It offers the topics of
/// the toolset's tools only.
/// </summary>
[McpServerToolType]
public sealed class HelpTools(McpToolset toolset = McpToolset.All)
{
    private const string QueryDescription = "Detail for the other tools. topic: search, columns, cohorts, hierarchies, paging, references, issues, patient_summary, terminology, drug_classes or folder.";

    [McpServerTool(Name = "help", ReadOnly = true, Idempotent = true)]
    [QueryToolset(Description = QueryDescription)]
    [Description("Detail for the other tools. topic: search, columns, fhirpath, cohorts, patient_search, hierarchies, paging, references, issues, plan, patient_summary, patient_context, terminology, drug_classes, validate or folder.")]
    public CallToolResult Help(string topic)
    {
        IReadOnlyDictionary<string, string> topics = HelpTopics.For(toolset);
        return topics.TryGetValue(topic.Trim(), out string? text)
            ? new CallToolResult { Content = [new TextContentBlock { Text = text }] }
            : new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Unknown topic. Topics: {string.Join(", ", topics.Keys.Order(StringComparer.Ordinal))}." }],
                IsError = true,
            };
    }
}
