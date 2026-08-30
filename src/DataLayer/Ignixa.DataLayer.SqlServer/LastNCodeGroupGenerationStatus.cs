namespace Ignixa.DataLayer.SqlServer;

public sealed record LastNCodeGroupGenerationStatus(
    Guid? AttemptId,
    long Generation,
    string State,
    long? SnapshotHighWaterSurrogateId);
