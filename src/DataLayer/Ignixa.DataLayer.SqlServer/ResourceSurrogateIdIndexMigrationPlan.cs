namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// The decision <see cref="ResourceSurrogateIdIndexOnlineMigration.PlanAsync"/> reached for one tenant,
/// and why. Planning is separate from applying so the schema-upgrade CLI can show it before its
/// confirmation prompt without changing anything. Built only by
/// <see cref="ResourceSurrogateIdIndexOnlineMigration.Decide"/>, which sets
/// <paramref name="WaitAtLowPriority"/> only for the online actions.
/// </summary>
/// <param name="Action">What will be done before the deploy.</param>
/// <param name="WaitAtLowPriority">
/// Whether the online build's lock waits use <c>WAIT_AT_LOW_PRIORITY</c>, which the engine must support.
/// </param>
/// <param name="Reason">Why, stated in the log and the CLI output.</param>
internal sealed record ResourceSurrogateIdIndexMigrationPlan(
    ResourceSurrogateIdIndexMigrationAction Action,
    bool WaitAtLowPriority,
    string Reason)
{
    /// <summary>
    /// What the offline deploy will cost dbo.Resource, for the actions that leave the index to it; stated
    /// wherever the fallback is reported (log and CLI output). <see langword="null"/> for every other action.
    /// </summary>
    public string? OfflineDeployConsequence => Action switch
    {
        ResourceSurrogateIdIndexMigrationAction.DeferConversionToOfflineDeploy =>
            "The schema deploy will rebuild it OFFLINE (DROP INDEX, then CREATE INDEX): dbo.Resource is without the " +
            "index, and its uniqueness check, until the rebuild finishes, and writes to dbo.Resource are blocked while it builds.",
        ResourceSurrogateIdIndexMigrationAction.DeferCreationToOfflineDeploy =>
            "The schema deploy will create it OFFLINE: writes to dbo.Resource are blocked while it builds.",
        _ => null,
    };
}
