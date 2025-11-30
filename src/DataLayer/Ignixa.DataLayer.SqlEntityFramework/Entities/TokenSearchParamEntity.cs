// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.TokenSearchParam table.
/// Stores indexed token search parameter values (e.g., Observation.code, Patient.identifier).
/// Tokens are system|code pairs.
/// </summary>
[Table("TokenSearchParam", Schema = "dbo")]
public class TokenSearchParamEntity
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
    /// System ID (FK to System.SystemId, e.g., http://loinc.org).
    /// Null if no system specified.
    /// </summary>
    [Column("SystemId")]
    public int? SystemId { get; set; }

    /// <summary>
    /// Token code value (up to 256 characters).
    /// Case-sensitive.
    /// </summary>
    [Required]
    [Column("Code")]
    [MaxLength(256)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Overflow code for values longer than 256 characters.
    /// </summary>
    [Column("CodeOverflow")]
    public string? CodeOverflow { get; set; }

    // Navigation properties

    /// <summary>
    /// Navigation to Resource entity.
    /// </summary>
    [ForeignKey($"{nameof(ResourceTypeId)},{nameof(ResourceSurrogateId)}")]
    public ResourceEntity? Resource { get; set; }
}
