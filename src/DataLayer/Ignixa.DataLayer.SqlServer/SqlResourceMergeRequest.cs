namespace Ignixa.DataLayer.SqlServer;

public sealed record SqlResourceMergeRequest(
    bool RaiseExceptionOnConflict,
    bool IsResourceChangeCaptureEnabled,
    long? TransactionId,
    bool SingleTransaction,
    SqlResourceIndexBatch Batch);
