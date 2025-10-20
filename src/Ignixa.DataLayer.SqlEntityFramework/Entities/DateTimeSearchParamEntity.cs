// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.DateTimeSearchParam table.
/// Stores indexed date/time search parameter values (e.g., Patient.birthDate, Observation.effective).
/// </summary>
[Table("DateTimeSearchParam", Schema = "dbo")]
public class DateTimeSearchParamEntity
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
    /// Start of date/time range.
    /// </summary>
    [Required]
    [Column("StartDateTime")]
    public DateTime StartDateTime { get; set; }

    /// <summary>
    /// End of date/time range.
    /// </summary>
    [Required]
    [Column("EndDateTime")]
    public DateTime EndDateTime { get; set; }

    /// <summary>
    /// Indicates if the date range is longer than a day.
    /// Used for optimization.
    /// </summary>
    [Required]
    [Column("IsLongerThanADay")]
    public bool IsLongerThanADay { get; set; }

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
