// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;

namespace Ignixa.Domain.Exceptions;

/// <summary>
/// Thrown when a custom audit header value exceeds the maximum allowed length.
/// </summary>
public sealed class AuditHeaderTooLargeException(string headerName, int maximumLength, int actualLength)
    : AuditHeaderException(string.Format(
        CultureInfo.InvariantCulture,
        "The maximum length of a custom audit header value is {0}. The supplied custom audit header '{1}' has length of {2}.",
        maximumLength,
        headerName,
        actualLength))
{
    public string HeaderName { get; } = headerName;

    public int MaximumLength { get; } = maximumLength;

    public int ActualLength { get; } = actualLength;
}
