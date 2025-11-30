// <copyright file="ReferenceFieldMetadata.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#nullable enable

namespace Ignixa.Abstractions;

/// <summary>
/// Metadata about a reference field in a FHIR resource.
/// </summary>
public sealed record ReferenceFieldMetadata(
    string ElementPath,
    int Min,
    string Max,
    IReadOnlyList<string> TargetResourceTypes,
    bool InSummary)
{
    /// <summary>
    /// Gets a value indicating whether this field can contain multiple references (max != "1").
    /// </summary>
    public bool IsCollection => Max != "1";

    /// <summary>
    /// Gets a value indicating whether this field is required (min >= 1).
    /// </summary>
    public bool IsRequired => Min > 0;
}
