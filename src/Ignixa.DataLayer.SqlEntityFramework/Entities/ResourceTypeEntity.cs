// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.ResourceType table.
/// Stores FHIR resource type names (Patient, Observation, etc.) and their numeric IDs.
/// </summary>
[Table("ResourceType", Schema = "dbo")]
public class ResourceTypeEntity
{
    /// <summary>
    /// Numeric resource type identifier (auto-generated).
    /// Primary key.
    /// </summary>
    [Key]
    [Column("ResourceTypeId")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public short ResourceTypeId { get; set; }

    /// <summary>
    /// FHIR resource type name (e.g., "Patient", "Observation", "Condition").
    /// Case-sensitive. Primary key (clustered).
    /// </summary>
    [Required]
    [Column("Name")]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    // Navigation properties

    /// <summary>
    /// Collection of all resources of this type.
    /// </summary>
    public ICollection<ResourceEntity> Resources { get; } = new List<ResourceEntity>();
}
