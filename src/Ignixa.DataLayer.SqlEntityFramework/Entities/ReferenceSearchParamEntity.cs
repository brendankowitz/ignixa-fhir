// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.ReferenceSearchParam table.
/// Stores indexed reference search parameter values (e.g., Observation.subject, Encounter.patient).
/// </summary>
[Table("ReferenceSearchParam", Schema = "dbo")]
public class ReferenceSearchParamEntity
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
    /// Base URI of the reference (e.g., "http://example.com/fhir").
    /// Null for local references.
    /// </summary>
    [Column("BaseUri")]
    [MaxLength(128)]
    public string? BaseUri { get; set; }

    /// <summary>
    /// Referenced resource type ID (FK to ResourceType.ResourceTypeId).
    /// Null if type is unknown.
    /// </summary>
    [Column("ReferenceResourceTypeId")]
    public short? ReferenceResourceTypeId { get; set; }

    /// <summary>
    /// Referenced resource ID (e.g., "patient-123").
    /// </summary>
    [Required]
    [Column("ReferenceResourceId")]
    [MaxLength(64)]
    public string ReferenceResourceId { get; set; } = string.Empty;

    /// <summary>
    /// Referenced resource version number.
    /// Null if version not specified.
    /// </summary>
    [Column("ReferenceResourceVersion")]
    public int? ReferenceResourceVersion { get; set; }

    // Navigation properties

    /// <summary>
    /// Navigation to Resource entity.
    /// </summary>
    [ForeignKey($"{nameof(ResourceTypeId)},{nameof(ResourceSurrogateId)}")]
    public ResourceEntity? Resource { get; set; }
}
