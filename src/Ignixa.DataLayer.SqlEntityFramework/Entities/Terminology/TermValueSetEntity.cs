// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities.Terminology;

/// <summary>
/// Entity mapping to dbo.TermValueSet table.
/// Stores metadata for ValueSet resources to track expansion state.
/// </summary>
[Table("TermValueSet", Schema = "dbo")]
public class TermValueSetEntity
{
    [Key]
    [Column("TermValueSetId")]
    public long TermValueSetId { get; set; }

    [Required]
    [Column("PackageResourceId")]
    public long PackageResourceId { get; set; }

    [Required]
    [Column("Canonical")]
    [MaxLength(512)]
    public string Canonical { get; set; } = string.Empty;

    [Column("Version")]
    [MaxLength(100)]
    public string? Version { get; set; }

    [Required]
    [Column("Name")]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column("Immutable")]
    public bool Immutable { get; set; }

    [Required]
    [Column("IsExpanded")]
    public bool IsExpanded { get; set; }

    [Column("LastExpansionDate")]
    public DateTimeOffset? LastExpansionDate { get; set; }

    [Column("ExpansionCodeCount")]
    public int? ExpansionCodeCount { get; set; }

    /// <summary>
    /// True if the expansion is incomplete due to external CodeSystems not being imported.
    /// Per FHIR spec, this should be returned as expansion.parameter.incomplete = true.
    /// </summary>
    [Required]
    [Column("IsPartialExpansion")]
    public bool IsPartialExpansion { get; set; }

    /// <summary>
    /// Human-readable reason for partial expansion (e.g., "External systems: http://snomed.info/sct, http://loinc.org").
    /// </summary>
    [Column("PartialExpansionReason")]
    [MaxLength(1024)]
    public string? PartialExpansionReason { get; set; }

    [Required]
    [Column("ImportedDate")]
    public DateTimeOffset ImportedDate { get; set; }

    // Navigation properties
    public PackageResourceEntity PackageResource { get; set; } = null!;
}
