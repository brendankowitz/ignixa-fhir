namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

/// <summary>
/// Immutable snapshot of a table's rows read from ONE specific database, captured via
/// <see cref="DifferentialTestHarness.SnapshotLegacyAsync"/> or
/// <see cref="DifferentialTestHarness.SnapshotNewAsync"/>. Row order is NOT meaningful -- SQL Server
/// gives no ordering guarantee without ORDER BY, and TVP-based bulk inserts don't guarantee insertion
/// order either. <see cref="DifferentialTestHarness.AssertEquivalent"/> sorts rows by a normalized
/// column-value key before comparing so an unsorted result-set ordering never produces a false failure.
/// </summary>
public sealed record RowStateSnapshot(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);
