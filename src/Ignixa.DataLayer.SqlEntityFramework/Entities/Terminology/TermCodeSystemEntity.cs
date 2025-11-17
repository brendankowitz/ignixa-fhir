// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities.Terminology;

/// <summary>
/// Entity mapping to dbo.TermCodeSystem table.
/// Stores metadata for CodeSystem resources to enable fast terminology operations.
/// </summary>
[Table("TermCodeSystem", Schema = "dbo")]
public class TermCodeSystemEntity
{
    [Key]
    [Column("TermCodeSystemId")]
    public long TermCodeSystemId { get; set; }

    [Required]
    [Column("PackageResourceId")]
    public long PackageResourceId { get; set; }

    [Required]
    [Column("SystemId")]
    public int SystemId { get; set; }

    [Column("Version")]
    [MaxLength(100)]
    public string? Version { get; set; }

    [Required]
    [Column("ConceptCount")]
    public int ConceptCount { get; set; }

    [Required]
    [Column("Content")]
    [MaxLength(50)]
    public string Content { get; set; } = string.Empty;

    [Required]
    [Column("IsHierarchical")]
    public bool IsHierarchical { get; set; }

    [Required]
    [Column("CaseSensitive")]
    public bool CaseSensitive { get; set; }

    [Required]
    [Column("Compositional")]
    public bool Compositional { get; set; }

    [Required]
    [Column("ImportedDate")]
    public DateTimeOffset ImportedDate { get; set; }

    // Navigation properties
    public PackageResourceEntity PackageResource { get; set; } = null!;
    public SystemEntity System { get; set; } = null!;
}
