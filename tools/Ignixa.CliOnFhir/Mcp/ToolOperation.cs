// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>One operation of a tool that takes an <c>operation</c> argument: the arguments it needs and those it also takes.</summary>
/// <param name="Name">The <c>operation</c> value, for example <c>lookup</c>.</param>
/// <param name="Required">Arguments that must have a value.</param>
/// <param name="Optional">Arguments it also takes; any other argument with a value fails the call.</param>
internal sealed record ToolOperation(string Name, IReadOnlyList<string> Required, IReadOnlyList<string> Optional)
{
    /// <summary>The arguments as an agent reads them, for example <c>system, code [version]</c>.</summary>
    public string Signature =>
        Optional.Count == 0 ? string.Join(", ", Required)
        : Required.Count == 0 ? $"[{string.Join(", ", Optional)}]"
        : $"{string.Join(", ", Required)} [{string.Join(", ", Optional)}]";

    /// <summary>Whether <paramref name="argument"/> is one this operation takes.</summary>
    public bool Takes(string argument) => Required.Contains(argument, StringComparer.Ordinal) || Optional.Contains(argument, StringComparer.Ordinal);
}
