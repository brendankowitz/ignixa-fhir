// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;

namespace Ignixa.Domain.Exceptions;

/// <summary>
/// Thrown when a request supplies more custom audit headers than allowed.
/// </summary>
public sealed class AuditHeaderCountExceededException(int maximumCount, int actualCount)
    : AuditHeaderException(string.Format(
        CultureInfo.InvariantCulture,
        "The maximum number of custom audit headers allowed is {0}. The number of custom audit headers supplied is {1}.",
        maximumCount,
        actualCount))
{
    public int MaximumCount { get; } = maximumCount;

    public int ActualCount { get; } = actualCount;
}
