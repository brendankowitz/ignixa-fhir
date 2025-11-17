// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities.Terminology;

/// <summary>
/// Entity mapping to dbo.TermConceptMap table.
/// Stores metadata for ConceptMap resources to support $translate operations.
/// </summary>
[Table("TermConceptMap", Schema = "dbo")]
public class TermConceptMapEntity
{
    [Key]
    [Column("TermConceptMapId")]
    public long TermConceptMapId { get; set; }

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

    [Column("SourceCanonical")]
    [MaxLength(512)]
    public string? SourceCanonical { get; set; }

    [Column("TargetCanonical")]
    [MaxLength(512)]
    public string? TargetCanonical { get; set; }

    [Required]
    [Column("ImportedDate")]
    public DateTimeOffset ImportedDate { get; set; }

    // Navigation properties
    public PackageResourceEntity PackageResource { get; set; } = null!;
}
