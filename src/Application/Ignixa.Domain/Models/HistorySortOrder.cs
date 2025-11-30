// <copyright file="HistorySortOrder.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Domain.Models;

/// <summary>
/// Specifies the sort order for history query results.
/// </summary>
public enum HistorySortOrder
{
    /// <summary>
    /// Sort by lastModified in ascending order (oldest first).
    /// </summary>
    Ascending,

    /// <summary>
    /// Sort by lastModified in descending order (newest first).
    /// FHIR specification default for history operations.
    /// </summary>
    Descending,
}
