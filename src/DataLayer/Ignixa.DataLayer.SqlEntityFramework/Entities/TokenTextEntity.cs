// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.TokenText table.
/// Stores display text from token search parameter values (e.g., CodeableConcept.text, Coding.display).
/// Used for :text modifier searches on token parameters.
/// </summary>
[Table("TokenText", Schema = "dbo")]
public class TokenTextEntity
{
    /// <summary>
    /// Resource type identifier (FK to ResourceType.ResourceTypeId).
    /// </summary>
    [Required]
    [Column("ResourceTypeId")]
    public short ResourceTypeId { get; set; }

    /// <summary>
    /// Resource surrogate ID (FK to Resource.ResourceSurrogateId).
    /// </summary>
    [Required]
    [Column("ResourceSurrogateId")]
    public long ResourceSurrogateId { get; set; }

    /// <summary>
    /// Search parameter ID (e.g., 15 for Observation.code).
    /// </summary>
    [Required]
    [Column("SearchParamId")]
    public short SearchParamId { get; set; }

    /// <summary>
    /// Display text from the token value (e.g., Coding.display, CodeableConcept.text).
    /// Up to 400 characters.
    /// </summary>
    [Required]
    [Column("Text")]
    [MaxLength(400)]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether this is a historical record (vs current).
    /// </summary>
    [Column("IsHistory")]
    public bool IsHistory { get; set; }

    // Navigation properties

    /// <summary>
    /// Navigation to Resource entity.
    /// </summary>
    [ForeignKey($"{nameof(ResourceTypeId)},{nameof(ResourceSurrogateId)}")]
    public ResourceEntity? Resource { get; set; }
}
