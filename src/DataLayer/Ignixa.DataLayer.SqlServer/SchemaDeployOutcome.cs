namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// The result of <see cref="SchemaDeployer.DeployAndStampAsync"/>. A schema deploy and its
/// version stamp are two separate database calls; this distinguishes "both succeeded" from
/// "the schema change committed but the version record did not" so a caller can decide how to
/// react, rather than the stamp failure looking identical to an aborted deploy.
/// </summary>
public enum SchemaDeployOutcome
{
    Applied,
    AppliedButVersionStampFailed,
}
