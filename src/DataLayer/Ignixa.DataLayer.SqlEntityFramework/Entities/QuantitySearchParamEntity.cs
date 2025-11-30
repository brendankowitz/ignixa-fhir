// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.QuantitySearchParam table.
/// Stores indexed quantity search parameter values with units (e.g., Observation.value[Quantity]).
/// </summary>
[Table("QuantitySearchParam", Schema = "dbo")]
public class QuantitySearchParamEntity
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
    /// System ID (FK to System.SystemId, e.g., http://unitsofmeasure.org).
    /// Null if no system specified.
    /// </summary>
    [Column("SystemId")]
    public int? SystemId { get; set; }

    /// <summary>
    /// Quantity code ID (FK to QuantityCode.QuantityCodeId, e.g., "mg", "kg").
    /// Null if no unit code specified.
    /// </summary>
    [Column("QuantityCodeId")]
    public int? QuantityCodeId { get; set; }

    /// <summary>
    /// Single quantity value (for exact match searches).
    /// Null if value is a range.
    /// </summary>
    [Column("SingleValue")]
    [Precision(36, 18)]
    public decimal? SingleValue { get; set; }

    /// <summary>
    /// Lower bound of quantity range.
    /// </summary>
    [Required]
    [Column("LowValue")]
    [Precision(36, 18)]
    public decimal LowValue { get; set; }

    /// <summary>
    /// Upper bound of quantity range.
    /// </summary>
    [Required]
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
