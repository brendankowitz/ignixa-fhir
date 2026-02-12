// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Configuration;

namespace Ignixa.Anonymizer.Pipeline;

/// <summary>
/// Represents a matched rule with the elements it applies to.
/// </summary>
public sealed record MatchedRule
{
    /// <summary>
    /// The FHIRPath rule configuration.
    /// </summary>
    public required FhirPathRule Rule { get; init; }

    /// <summary>
    /// Elements matched by the FHIRPath expression.
    /// </summary>
    public required IReadOnlyList<IElement> MatchedElements { get; init; }
}
