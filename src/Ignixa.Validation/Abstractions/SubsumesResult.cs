// <copyright file="SubsumesResult.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Result of a CodeSystem $subsumes operation.
/// Indicates the subsumption relationship between two codes.
/// </summary>
/// <param name="Outcome">Subsumption outcome: "equivalent", "subsumes", "subsumed-by", or "not-subsumed".</param>
public record SubsumesResult(string Outcome);
