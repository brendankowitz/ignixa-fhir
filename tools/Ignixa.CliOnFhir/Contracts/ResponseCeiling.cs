// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// The last line of defence of the response ceilings: every tool fits its own answer, and an answer that still would be larger
/// than its ceiling is replaced, as it is written, by an error envelope with <see cref="Code"/>, never sent oversized.
/// </summary>
public static class ResponseCeiling
{
    /// <summary>The error code of an answer withheld because it is larger than its ceiling.</summary>
    public const string Code = "response-over-ceiling";

    /// <summary>The issue that replaces an answer of <paramref name="bytes"/> bytes from <paramref name="tool"/> over <paramref name="ceiling"/>.</summary>
    public static IssueItem Issue(string tool, int bytes, int ceiling) => new(
        "error",
        Code,
        $"{tool} answered {bytes} bytes, over the {ceiling}-byte response ceiling, so the answer was withheld rather than sent oversized. "
            + "Narrow the call: shorter arguments, elements, a smaller count, or summary=count.",
        Location: null);
}
