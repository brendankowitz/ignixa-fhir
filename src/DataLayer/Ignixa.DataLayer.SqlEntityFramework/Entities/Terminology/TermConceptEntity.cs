// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities.Terminology;

/// <summary>
/// Entity mapping to dbo.TermConcept table.
/// Stores individual concepts/codes from CodeSystem resources for fast queries.
/// </summary>
[Table("TermConcept", Schema = "dbo")]
public class TermConceptEntity
{
    [Key]
    [Column("TermConceptId")]
    public long TermConceptId { get; set; }

    [Required]
    [Column("TermCodeSystemId")]
    public long TermCodeSystemId { get; set; }

    [Required]
    [Column("Code")]
    [MaxLength(256)]
    public string Code { get; set; } = string.Empty;

    [Column("Display")]
    [MaxLength(500)]
    public string? Display { get; set; }

    [Column("Definition")]
    [MaxLength(4000)]  // SQL Server max for indexed columns
    public string? Definition { get; set; }

    [Column("ParentConceptId")]
    public long? ParentConceptId { get; set; }

    [Required]
    [Column("Level")]
    public int Level { get; set; }

    [Required]
    [Column("IsActive")]
    public bool IsActive { get; set; } = true;

    [Column("PropertiesJson")]
    public string? PropertiesJson { get; set; }

    // Navigation properties
    public TermCodeSystemEntity CodeSystem { get; set; } = null!;
    public TermConceptEntity? ParentConcept { get; set; }
}
