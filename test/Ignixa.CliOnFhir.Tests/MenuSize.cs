// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit.Abstractions;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// What a tool menu costs a client on every model request: the serialized <c>tools/list</c> entries plus the server
/// instructions, in characters (tokens are approximated as characters / 4). Measured with LF line breaks, so the result does
/// not depend on how the source files were checked out: a CRLF checkout puts \r\n in every multi-line description.
/// </summary>
internal static class MenuSize
{
    public static int Measure(IEnumerable<McpClientTool> tools, string instructions, ITestOutputHelper output)
    {
        var report = new StringBuilder();
        string lfInstructions = instructions.ReplaceLineEndings("\n");
        int total = lfInstructions.Length;
        report.AppendLine($"{"tool",-28} {"chars",7} {"~tokens",8}");
        foreach (Tool tool in tools.Select(tool => tool.ProtocolTool).OrderBy(tool => tool.Name, StringComparer.Ordinal))
        {
            string json = JsonSerializer.Serialize(tool, McpJsonUtilities.DefaultOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
            total += json.Length;
            report.AppendLine($"{tool.Name,-28} {json.Length,7} {json.Length / 4,8}");
        }

        report.AppendLine($"{"(instructions)",-28} {lfInstructions.Length,7} {lfInstructions.Length / 4,8}");
        report.AppendLine($"{"TOTAL",-28} {total,7} {total / 4,8}");
        output.WriteLine(report.ToString());
        return total;
    }
}
