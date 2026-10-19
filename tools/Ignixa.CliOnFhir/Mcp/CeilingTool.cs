// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>
/// A tool whose answer is held to the session's response ceiling (<see cref="FolderSession.MaxResponseBytes"/>, the MCP default
/// <see cref="EnvelopeJson.McpMaxBytes"/> without a session): one larger is replaced by a <see cref="ResponseCeiling.Code"/> error
/// envelope and logged. Every tool fits its own answer first; this only catches what a fit missed.
/// </summary>
internal sealed class CeilingTool(McpServerTool inner, Func<RequestContext<CallToolRequestParams>, FolderSession?> sessionOf) : DelegatingMcpServerTool(inner)
{
    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CallToolResult result = await base.InvokeAsync(request, cancellationToken);
        FolderSession? session = sessionOf(request);
        int ceiling = session?.MaxResponseBytes ?? EnvelopeJson.McpMaxBytes;
        int bytes = result.Content.OfType<TextContentBlock>().Sum(block => Encoding.UTF8.GetByteCount(block.Text));
        if (bytes <= ceiling)
        {
            return result;
        }

        ILogger logger = request.Services?.GetService<ILoggerFactory>()?.CreateLogger<CeilingTool>() ?? NullLogger<CeilingTool>.Instance;
        logger.LogError("{Tool} answered {Bytes} bytes, over the {Ceiling}-byte response ceiling; the answer was replaced by {Code}.", ProtocolTool.Name, bytes, ceiling, ResponseCeiling.Code);
        IssueItem issue = ResponseCeiling.Issue(ProtocolTool.Name, bytes, ceiling);
        ToolEnvelope<object?> envelope = session is null
            ? new ToolEnvelope<object?>(null, [], [], [], [issue], new FreshnessInfo(IndexBuiltAt: null, StaleFiles: null))
            : EnvelopeAssembler.Failure<object>(session, issue, cancellationToken);
        return ToolResults.Lean(envelope);
    }
}
