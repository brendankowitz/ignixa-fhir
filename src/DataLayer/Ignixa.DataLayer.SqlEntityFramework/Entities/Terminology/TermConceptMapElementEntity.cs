// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities.Terminology;

/// <summary>
/// Entity mapping to dbo.TermConceptMapElement table.
/// Stores individual mapping elements (source → target) for $translate operations.
/// </summary>
[Table("TermConceptMapElement", Schema = "dbo")]
public class TermConceptMapElementEntity
{
    [Key]
    [Column("TermConceptMapElementId")]
    public long TermConceptMapElementId { get; set; }

    [Required]
    [Column("TermConceptMapId")]
    public long TermConceptMapId { get; set; }

    [Required]
    [Column("SourceSystemId")]
    public int SourceSystemId { get; set; }

    [Required]
    [Column("SourceCode")]
    [MaxLength(256)]
    public string SourceCode { get; set; } = string.Empty;

    [Column("SourceDisplay")]
    [MaxLength(500)]
    public string? SourceDisplay { get; set; }

    [Column("TargetSystemId")]
    public int? TargetSystemId { get; set; }

    [Column("TargetCode")]
    [MaxLength(256)]
    public string? TargetCode { get; set; }

    [Column("TargetDisplay")]
    [MaxLength(500)]
    public string? TargetDisplay { get; set; }

    [Required]
    [Column("Equivalence")]
    [MaxLength(50)]
    public string Equivalence { get; set; } = string.Empty;

    [Column("Comment")]
    public string? Comment { get; set; }

    [Required]
    [Column("GroupIndex")]
    public int GroupIndex { get; set; }

    // Navigation properties
    public TermConceptMapEntity ConceptMap { get; set; } = null!;
    public SystemEntity SourceSystem { get; set; } = null!;
    public SystemEntity? TargetSystem { get; set; }
}
