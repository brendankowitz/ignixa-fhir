// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.NumberSearchParam table.
/// Stores indexed numeric search parameter values (e.g., Observation.value[x], RiskAssessment.probability).
/// </summary>
[Table("NumberSearchParam", Schema = "dbo")]
public class NumberSearchParamEntity
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
    /// Single numeric value (for exact match searches).
    /// Null if value is a range.
    /// </summary>
    [Column("SingleValue")]
    [Precision(36, 18)]
    public decimal? SingleValue { get; set; }

    /// <summary>
    /// Lower bound of numeric range.
    /// Note: The table schema requires NOT NULL, but rows are never queried directly via EF.
    /// The TVP accepts nullable values and the stored procedure converts nulls to defaults.
    /// </summary>
    [Column("LowValue")]
    [Precision(36, 18)]
    public decimal LowValue { get; set; }

    /// <summary>
    /// Upper bound of numeric range.
    /// Note: The table schema requires NOT NULL, but rows are never queried directly via EF.
    /// The TVP accepts nullable values and the stored procedure converts nulls to defaults.
    /// </summary>
    [Column("HighValue")]
    [Precision(36, 18)]
    public decimal HighValue { get; set; }

    // Navigation properties

    /// <summary>
    /// Navigation to Resource entity.
    /// </summary>
    [ForeignKey($"{nameof(ResourceTypeId)},{nameof(ResourceSurrogateId)}")]
    public ResourceEntity? Resource { get; set; }
}
