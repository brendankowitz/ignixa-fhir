namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Result of <see cref="SchemaDeployer.DeployAndStampAsync"/>. <see cref="StampException"/> is
/// non-null if and only if <see cref="Outcome"/> is
/// <see cref="SchemaDeployOutcome.AppliedButVersionStampFailed"/>.
/// </summary>
public sealed record SchemaDeployResult(SchemaDeployOutcome Outcome, Exception? StampException = null);
