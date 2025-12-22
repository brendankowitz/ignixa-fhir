// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.ResourceTtl table.
/// Tracks TTL (Time-To-Live) expiration timestamps for FHIR resources.
/// Resources with entries in this table will be hard-deleted by background cleanup after ExpiresAt.
/// </summary>
[Table("ResourceTtl", Schema = "dbo")]
public class ResourceTtlEntity
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
    /// Part of composite primary key.
    /// </summary>
    [Required]
    [Column("ResourceId")]
    [MaxLength(64)]
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when this resource should be hard-deleted.
    /// Set via X-TTL HTTP header during resource creation/update.
    /// </summary>
    [Required]
    [Column("ExpiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Transaction ID when this TTL entry was created or last updated.
    /// References Transaction.SurrogateIdRangeFirstValue (no FK constraint).
    /// </summary>
    [Column("TransactionId")]
    public long? TransactionId { get; set; }

    // Navigation properties

    /// <summary>
    /// Navigation to Transaction entity.
    /// </summary>
    [ForeignKey(nameof(TransactionId))]
    public TransactionEntity? Transaction { get; set; }
}
