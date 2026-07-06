// <copyright file="ConformanceExpectation.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Validation.Tests.Conformance;

/// <summary>
/// The expected validation verdict for a case, derived from a reference validator's recorded outcome.
/// </summary>
/// <param name="ExpectedErrorCount">Error/fatal issue count from the reference outcome.</param>
/// <param name="Source">Which oracle produced this expectation (e.g. "java").</param>
public sealed record ConformanceExpectation(int ExpectedErrorCount, string Source)
{
    /// <summary>True when the reference validator reported zero error/fatal issues (derived, not stored).</summary>
    public bool ExpectedValid => ExpectedErrorCount == 0;
}
