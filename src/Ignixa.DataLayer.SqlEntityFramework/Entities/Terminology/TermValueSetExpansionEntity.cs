// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities.Terminology;

/// <summary>
/// Entity mapping to dbo.TermValueSetExpansion table.
/// Stores pre-computed expansion of ValueSets for fast $expand and $validate-code operations.
/// </summary>
[Table("TermValueSetExpansion", Schema = "dbo")]
public class TermValueSetExpansionEntity
{
    [Key]
    [Column("TermValueSetExpansionId")]
    public long TermValueSetExpansionId { get; set; }

    [Required]
    [Column("TermValueSetId")]
    public long TermValueSetId { get; set; }

    [Required]
    [Column("SystemId")]
    public int SystemId { get; set; }

    [Required]
    [Column("Code")]
    [MaxLength(256)]
    public string Code { get; set; } = string.Empty;

    [Column("Display")]
    [MaxLength(500)]
    public string? Display { get; set; }

    [Column("SystemVersion")]
    [MaxLength(100)]
    public string? SystemVersion { get; set; }

    [Required]
    [Column("IsActive")]
    public bool IsActive { get; set; } = true;

    [Required]
    [Column("Ordinal")]
    public int Ordinal { get; set; }

    // Navigation properties
    public TermValueSetEntity ValueSet { get; set; } = null!;
    public SystemEntity System { get; set; } = null!;
}
