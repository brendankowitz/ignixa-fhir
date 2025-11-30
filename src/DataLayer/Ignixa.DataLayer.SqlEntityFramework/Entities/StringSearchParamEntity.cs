// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.StringSearchParam table.
/// Stores indexed string search parameter values (e.g., Patient.name, Observation.text).
/// </summary>
[Table("StringSearchParam", Schema = "dbo")]
public class StringSearchParamEntity
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
    /// Search parameter ID (e.g., 42 for Patient.name).
    /// </summary>
    [Required]
    [Column("SearchParamId")]
    public short SearchParamId { get; set; }

    /// <summary>
    /// Indexed text value (up to 256 characters).
    /// Case-insensitive, accent-insensitive.
    /// </summary>
    [Required]
    [Column("Text")]
    [MaxLength(256)]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Overflow text for values longer than 256 characters.
    /// </summary>
    [Column("TextOverflow")]
    public string? TextOverflow { get; set; }

    /// <summary>
    /// Indicates if this is the minimum value for range searches.
    /// </summary>
    [Required]
    [Column("IsMin")]
    public bool IsMin { get; set; }

    /// <summary>
    /// Indicates if this is the maximum value for range searches.
    /// </summary>
    [Required]
    [Column("IsMax")]
    public bool IsMax { get; set; }

    // Navigation properties

    /// <summary>
    /// Navigation to Resource entity.
    /// </summary>
    [ForeignKey($"{nameof(ResourceTypeId)},{nameof(ResourceSurrogateId)}")]
    public ResourceEntity? Resource { get; set; }
}
