// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.UriSearchParam table.
/// Stores indexed URI search parameter values (e.g., ValueSet.url, StructureDefinition.url).
/// </summary>
[Table("UriSearchParam", Schema = "dbo")]
public class UriSearchParamEntity
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
    /// Search parameter ID.
    /// </summary>
    [Required]
    [Column("SearchParamId")]
    public short SearchParamId { get; set; }

    /// <summary>
    /// URI value (up to 256 characters).
    /// Case-sensitive.
    /// </summary>
    [Required]
    [Column("Uri")]
    [MaxLength(256)]
    public string Uri { get; set; } = string.Empty;

    // Navigation properties

    /// <summary>
    /// Navigation to Resource entity.
    /// </summary>
    [ForeignKey($"{nameof(ResourceTypeId)},{nameof(ResourceSurrogateId)}")]
    public ResourceEntity? Resource { get; set; }
}
