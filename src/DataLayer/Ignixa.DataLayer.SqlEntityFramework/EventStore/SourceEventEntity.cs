// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.DataLayer.SqlEntityFramework.EventStore;

public class SourceEventEntity
{
    public long EventId { get; set; }
    public string StreamId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string EventData { get; set; } = null!;
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Transaction ID at the time this event was created.
    /// Used for deterministic reindex cutoff - resources with TransactionId &lt;= this value
    /// existed before this event and may need reindexing for new SearchParameters.
    /// </summary>
    public long TransactionId { get; set; }
}
