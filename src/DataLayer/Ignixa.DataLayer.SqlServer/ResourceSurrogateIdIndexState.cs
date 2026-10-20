namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// The shape of dbo.Resource's <c>IX_Resource_ResourceTypeId_ResourceSurrgateId</c> as
/// <see cref="ResourceSurrogateIdIndexOnlineMigration.PlanAsync"/> found it.
/// </summary>
internal enum ResourceSurrogateIdIndexState
{
    /// <summary>dbo.Resource does not exist; the deploy creates the table and the index together, on an empty table.</summary>
    NoResourceTable,

    /// <summary>dbo.Resource exists without the index; the deploy would create it on a populated table.</summary>
    Missing,

    /// <summary>The index exists in its version-3 shape, without <c>INCLUDE (ResourceId)</c>.</summary>
    LacksInclude,

    /// <summary>The index already has <c>INCLUDE (ResourceId)</c>.</summary>
    IncludesResourceId,
}
