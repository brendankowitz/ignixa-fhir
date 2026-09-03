// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Guards a convention that has no compiler or schema-deployment enforcement of its own: every table this
/// repo expects to take high-volume inserts or deletes shared across many callers -- every search-parameter
/// table, <c>dbo.Resource</c>, <c>dbo.CompartmentAssignment</c>, and the six terminology tables a CodeSystem,
/// ValueSet or ConceptMap import writes -- declares <c>SET (LOCK_ESCALATION = AUTO)</c> in its
/// <c>Tables/*.sql</c> file.
/// <para>
/// <b>The effect differs by table, and this guard does not check for that difference.</b> <c>AUTO</c>
/// escalates a bulk operation's row locks to the partition it touches instead of the whole table -- but
/// only on a partitioned table. Every search-parameter table here is partitioned on
/// <c>PartitionScheme_ResourceTypeId</c>, so the setting is load-bearing on those. None of the six
/// terminology tables is partitioned, so on those <c>AUTO</c> escalates to TABLE level exactly like SQL
/// Server's un-set default -- the setting is currently inert there, kept for convention consistency and so
/// it is already correct if one of those tables is ever partitioned. See each <c>Tables/TermCodeSystem.sql</c>-
/// style comment for the same note where it is set.
/// </para>
/// <para>
/// Nothing else checks this. DacFx deploys whatever <c>Tables/*.sql</c> says whether or not the option is
/// set, and the schema still compiles and passes every functional test either way -- the omission only shows
/// up as unexplained blocking under concurrent load, which is exactly the kind of defect worth failing loud
/// on here instead.
/// </para>
/// </summary>
public class LockEscalationConventionGuardTests
{
    /// <summary>
    /// The closed set of tables this convention applies to. Deliberately explicit rather than "every table"
    /// -- small reference/catalog tables (<c>dbo.ResourceType</c>, <c>dbo.System</c>, <c>dbo.SearchParam</c>,
    /// ...) and control-plane tables (<c>dbo.JobQueue</c>, <c>dbo.SchemaVersion</c>, ...) never take the
    /// volume that makes escalation a concern, and the existing search-parameter tables already agree on
    /// exactly this list. A table added here without updating its <c>.sql</c> file fails below; a table
    /// added to <c>Tables/*.sql</c> that plausibly belongs in this list is a case for a human, not this test.
    /// </summary>
    private static readonly string[] TablesRequiringLockEscalation =
    [
        "CompartmentAssignment",
        "DateTimeSearchParam",
        "NumberSearchParam",
        "QuantitySearchParam",
        "ReferenceSearchParam",
        "ReferenceTokenCompositeSearchParam",
        "Resource",
        "StringSearchParam",
        "TermCodeSystem",
        "TermConcept",
        "TermConceptMap",
        "TermConceptMapElement",
        "TermValueSet",
        "TermValueSetExpansion",
        "TokenDateTimeCompositeSearchParam",
        "TokenNumberNumberCompositeSearchParam",
        "TokenQuantityCompositeSearchParam",
        "TokenSearchParam",
        "TokenStringCompositeSearchParam",
        "TokenText",
        "TokenTokenCompositeSearchParam",
        "UriSearchParam",
    ];

    [Fact]
    public void GivenTheHighVolumeSharedTables_WhenTheirDdlIsRead_ThenEachSetsLockEscalationToAuto()
    {
        var repoRoot = RepoRoot.Find();
        var tablesDir = Path.Combine(repoRoot, "src", "DataLayer", "Ignixa.DataLayer.SqlServer.Database", "Tables");

        var missing = new List<string>();

        foreach (var table in TablesRequiringLockEscalation)
        {
            var path = Path.Combine(tablesDir, $"{table}.sql");

            File.Exists(path).ShouldBeTrue($"{path} does not exist -- update {nameof(TablesRequiringLockEscalation)} if it was renamed or removed.");

            var ddl = File.ReadAllText(path);

            if (!ddl.Contains($"ALTER TABLE dbo.{table} SET (LOCK_ESCALATION = AUTO)", StringComparison.Ordinal))
            {
                missing.Add(table);
            }
        }

        missing.ShouldBeEmpty(
            "The following tables are missing 'ALTER TABLE dbo.<Table> SET (LOCK_ESCALATION = AUTO);', " +
            $"which every other high-volume shared table in this project declares: {string.Join(", ", missing)}");
    }
}
