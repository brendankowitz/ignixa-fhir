// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace Ignixa.Anonymizer.Models;

public class ProcessContext
{
    /// <summary>
    /// Tracks visited nodes by Location string (e.g., "Patient.name[0].use").
    /// IElement instances are not stable across calls — the same logical node may
    /// produce different IElement wrapper objects from Children() or Select().
    /// Location strings are stable and unique per node in the tree.
    /// </summary>
    public HashSet<string> VisitedNodes { get; set; } = [];

    /// <summary>
    /// The id of the enclosing resource being processed.
    /// Used by DateShiftProcessor as a per-resource key prefix (since IElement has no Parent).
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;
}
