// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity mapping to dbo.Transactions table.
/// Tracks batches of FHIR operations for transaction support and visibility control.
/// </summary>
[Table("Transactions", Schema = "dbo")]
public class TransactionEntity
{
    /// <summary>
    /// First surrogate ID in the range allocated for this transaction.
    /// Primary key.
    /// </summary>
    [Key]
    [Column("SurrogateIdRangeFirstValue")]
    public long SurrogateIdRangeFirstValue { get; set; }

    /// <summary>
    /// Last surrogate ID in the range allocated for this transaction.
    /// </summary>
    [Required]
    [Column("SurrogateIdRangeLastValue")]
    public long SurrogateIdRangeLastValue { get; set; }

    /// <summary>
    /// Optional transaction definition (e.g., bundle JSON).
    /// </summary>
    [Column("Definition")]
    [MaxLength(2000)]
    public string? Definition { get; set; }

    /// <summary>
    /// Indicates if the transaction has completed processing.
    /// </summary>
    [Required]
    [Column("IsCompleted")]
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Indicates if the transaction completed successfully.
    /// </summary>
    [Required]
    [Column("IsSuccess")]
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Indicates if the transaction results are visible to queries.
    /// </summary>
    [Required]
    [Column("IsVisible")]
    public bool IsVisible { get; set; }

    /// <summary>
    /// Indicates if history records have been moved.
    /// </summary>
    [Required]
    [Column("IsHistoryMoved")]
    public bool IsHistoryMoved { get; set; }

    /// <summary>
    /// UTC timestamp when the transaction was created.
    /// </summary>
    [Required]
    [Column("CreateDate")]
    public DateTime CreateDate { get; set; }

    /// <summary>
    /// UTC timestamp when the transaction ended.
    /// </summary>
    [Column("EndDate")]
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// UTC timestamp when the transaction became visible.
    /// </summary>
    [Column("VisibleDate")]
    public DateTime? VisibleDate { get; set; }

    /// <summary>
    /// UTC timestamp when history was moved.
    /// </summary>
    [Column("HistoryMovedDate")]
    public DateTime? HistoryMovedDate { get; set; }

    /// <summary>
    /// UTC timestamp of last heartbeat (for long-running transactions).
    /// </summary>
    [Required]
    [Column("HeartbeatDate")]
    public DateTime HeartbeatDate { get; set; }

    /// <summary>
    /// Failure reason if transaction failed.
    /// </summary>
    [Column("FailureReason")]
    public string? FailureReason { get; set; }

    /// <summary>
    /// Indicates if the transaction is controlled by the client (vs server).
    /// </summary>
    [Required]
    [Column("IsControlledByClient")]
    public bool IsControlledByClient { get; set; } = true;

    /// <summary>
    /// UTC timestamp when invisible history was removed.
    /// </summary>
    [Column("InvisibleHistoryRemovedDate")]
    public DateTime? InvisibleHistoryRemovedDate { get; set; }

    // Navigation properties

    /// <summary>
    /// Resources created in this transaction.
    /// </summary>
    public ICollection<ResourceEntity> CreatedResources { get; } = new List<ResourceEntity>();

    /// <summary>
    /// Resources marked as history in this transaction.
    /// </summary>
    public ICollection<ResourceEntity> HistorizedResources { get; } = new List<ResourceEntity>();
}
