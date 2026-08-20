namespace Ignixa.DataLayer.SqlServer;

/// <summary>What <see cref="ResourceSurrogateIdIndexOnlineMigration"/> will do for a tenant before its schema deploy.</summary>
internal enum ResourceSurrogateIdIndexMigrationAction
{
    /// <summary>Nothing to do: the tenant is already at the target version, the index already has the INCLUDE, or dbo.Resource does not exist yet.</summary>
    None,

    /// <summary>Convert the existing index in place with <c>DROP_EXISTING = ON, ONLINE = ON</c> before the deploy.</summary>
    ConvertOnline,

    /// <summary>The index is missing from a populated dbo.Resource: create it with <c>ONLINE = ON</c> before the deploy.</summary>
    CreateOnline,

    /// <summary>
    /// The engine cannot build the index online, so the conversion is left to the DacFx deploy, which
    /// drops the index and rebuilds it offline. Always logged as a Warning before the deploy runs.
    /// </summary>
    DeferConversionToOfflineDeploy,

    /// <summary>
    /// The index is missing and the engine cannot build it online, so the DacFx deploy creates it offline.
    /// Always logged as a Warning before the deploy runs.
    /// </summary>
    DeferCreationToOfflineDeploy,
}
