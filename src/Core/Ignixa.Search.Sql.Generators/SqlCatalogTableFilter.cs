using System;

namespace Ignixa.Search.Sql.Generators;

/// <summary>
/// The set of tables <see cref="SqlCatalogGenerator"/> reads from the decomposed DDL: the search-index
/// tables the compiler lowers against, plus the tables the data layer hand-writes SQL against
/// (terminology, packages, background jobs, the event store) so those identifiers come from the real
/// DDL -- a renamed column becomes a build failure rather than a runtime error 207.
/// </summary>
/// <remarks>
/// Deliberately a named set rather than every table. The parser handles the DDL vocabulary these tables
/// use; the wider schema also contains computed columns (EventLog's PERSISTED PartitionId), which it
/// does not model. Widening further means teaching it those constructs first -- worth doing when
/// something needs them, not speculatively.
/// <para>
/// Split out of <see cref="SqlCatalogGenerator"/> (rather than a public method on it) so callers that
/// only need the filter -- notably the schema-parity guard test, which checks these tables' DDL against
/// the DB provisioning script -- don't have to load the generator's <c>Microsoft.CodeAnalysis</c>
/// dependency, an analyzer-only reference not available at ordinary runtime.
/// </para>
/// </remarks>
public static class SqlCatalogTableFilter
{
    public static bool IsCatalogTable(string tableName) =>
        tableName.EndsWith("SearchParam", StringComparison.Ordinal)
            || tableName == "ResourceType"
            || tableName == "Resource"
            || tableName == "TokenText"
            || tableName.StartsWith("Term", StringComparison.Ordinal)
            || tableName == "SourceEvents"
            || tableName == "BackgroundJobs"
            || tableName == "PackageResource"
            || tableName == "System";
}
