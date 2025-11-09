// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.PackageResource table.
/// Stores FHIR conformance resources extracted from FHIR NPM packages (IGs).
/// </summary>
[Table("PackageResource", Schema = "dbo")]
public class PackageResourceEntity
{
    /// <summary>
    /// Surrogate primary key for package resources.
    /// </summary>
    [Key]
    [Column("PackageResourceId")]
    public long PackageResourceId { get; set; }

    /// <summary>
    /// NPM package identifier (e.g., "hl7.fhir.us.core").
    /// </summary>
    [Required]
    [Column("PackageId")]
    [MaxLength(256)]
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// NPM package version (e.g., "5.0.1", "6.1.0").
    /// Uses semantic versioning (MAJOR.MINOR.PATCH).
    /// </summary>
    [Required]
    [Column("PackageVersion")]
    [MaxLength(100)]
    public string PackageVersion { get; set; } = string.Empty;

    /// <summary>
    /// FHIR resource type (e.g., "StructureDefinition", "ValueSet", "CodeSystem").
    /// </summary>
    [Required]
    [Column("ResourceType")]
    [MaxLength(64)]
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Canonical URL of the conformance resource.
    /// Used for resolution and lookups.
    /// </summary>
    [Required]
    [Column("Canonical")]
    [MaxLength(512)]
    public string Canonical { get; set; } = string.Empty;

    /// <summary>
    /// Business version of the conformance resource (from resource.version).
    /// May differ from PackageVersion.
    /// </summary>
    [Column("Version")]
    [MaxLength(100)]
    public string? Version { get; set; }

    /// <summary>
    /// Logical FHIR resource ID (from resource.id).
    /// </summary>
    [Required]
    [Column("ResourceId")]
    [MaxLength(64)]
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// Full FHIR resource as JSON string (uncompressed for package resources).
    /// Package resources are immutable and don't need compression.
    /// </summary>
    [Required]
    [Column("ResourceJson", TypeName = "nvarchar(max)")]
    public string ResourceJson { get; set; } = string.Empty;

    /// <summary>
    /// FHIR version (e.g., "4.0.1", "4.3.0", "5.0.0").
    /// </summary>
    [Required]
    [Column("FhirVersion")]
    [MaxLength(10)]
    public string FhirVersion { get; set; } = string.Empty;

    /// <summary>
    /// When this package resource was loaded into the database.
    /// </summary>
    [Required]
    [Column("LoadedDate")]
    public DateTimeOffset LoadedDate { get; set; }

    /// <summary>
    /// Indicates if this package resource is active and should be used for resolution.
    /// Allows soft-deletion of packages without data loss.
    /// </summary>
    [Required]
    [Column("IsActive")]
    public bool IsActive { get; set; } = true;
}
