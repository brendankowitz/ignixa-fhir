namespace Ignixa.DataLayer.SqlServer;

public sealed record LastNCodeGroupGenerationStatus(
    long Generation,
    string State,
    long? SnapshotHighWaterSurrogateId);
