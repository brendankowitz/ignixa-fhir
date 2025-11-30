// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.Resource table.
/// Stores FHIR resource data with versioning, soft delete, and compressed JSON storage.
/// </summary>
[Table("Resource", Schema = "dbo")]
public class ResourceEntity
{
    /// <summary>
    /// Resource type identifier (FK to ResourceType.ResourceTypeId).
    /// Part of composite primary key.
    /// </summary>
    [Required]
    [Column("ResourceTypeId")]
    public short ResourceTypeId { get; set; }

    /// <summary>
    /// Logical FHIR resource ID (e.g., "patient-123").
    /// </summary>
    [Required]
    [Column("ResourceId")]
    [MaxLength(64)]
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// Version number (incremented on each update).
    /// </summary>
    [Required]
    [Column("Version")]
    public int Version { get; set; }

    /// <summary>
    /// Indicates if this is a historical version (not the current version).
    /// </summary>
    [Required]
    [Column("IsHistory")]
    public bool IsHistory { get; set; }

    /// <summary>
    /// Surrogate ID for internal use (unique across all resource types).
    /// Part of composite primary key.
    /// </summary>
    [Required]
    [Column("ResourceSurrogateId")]
    public long ResourceSurrogateId { get; set; }

    /// <summary>
    /// Indicates if this resource has been soft-deleted.
    /// </summary>
    [Required]
    [Column("IsDeleted")]
    public bool IsDeleted { get; set; }

    /// <summary>
    /// HTTP method used to create this version (GET, PUT, POST, DELETE).
    /// </summary>
    [Column("RequestMethod")]
    [MaxLength(10)]
    public string? RequestMethod { get; set; }

    /// <summary>
    /// Compressed FHIR JSON resource (varbinary).
    /// Uses Gzip compression for storage efficiency.
    /// </summary>
    [Required]
    [Column("RawResource")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required by EF Core for varbinary(max) column mapping")]
    public byte[] RawResource { get; set; } = [];

    /// <summary>
    /// Indicates if meta fields (lastUpdated, versionId) are set in RawResource.
    /// </summary>
    [Required]
    [Column("IsRawResourceMetaSet")]
    public bool IsRawResourceMetaSet { get; set; }

    /// <summary>
    /// Hash of search parameters used to index this resource.
    /// Used to determine if reindexing is needed.
    /// </summary>
    [Column("SearchParamHash")]
    [MaxLength(64)]
    public string? SearchParamHash { get; set; }

    /// <summary>
    /// Transaction ID when this version was created.
    /// </summary>
    [Column("TransactionId")]
    public long? TransactionId { get; set; }

    /// <summary>
    /// Transaction ID when this version became history.
    /// </summary>
    [Column("HistoryTransactionId")]
    public long? HistoryTransactionId { get; set; }

    // Navigation properties

    /// <summary>
    /// Navigation to ResourceType entity.
    /// </summary>
    [ForeignKey(nameof(ResourceTypeId))]
    public ResourceTypeEntity? ResourceType { get; set; }

    /// <summary>
    /// Navigation to Transaction entity (creation transaction).
    /// </summary>
    [ForeignKey(nameof(TransactionId))]
    public TransactionEntity? Transaction { get; set; }

    /// <summary>
    /// Navigation to Transaction entity (history transaction).
    /// </summary>
    [ForeignKey(nameof(HistoryTransactionId))]
    public TransactionEntity? HistoryTransaction { get; set; }
}
