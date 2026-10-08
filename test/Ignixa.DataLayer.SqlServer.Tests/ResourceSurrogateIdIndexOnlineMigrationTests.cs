using Ignixa.DataLayer.SqlServer.Tests.Fixtures;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class ResourceSurrogateIdIndexOnlineMigrationTests
{
    // Any attempt to connect fails fast; a test passing with this connection string proves no SQL ran.
    private const string UnreachableConnectionString = "Server=tcp:unreachable.invalid,1;Connect Timeout=1;Pooling=false";

    private const int SqlServer2019 = 15;
    private const int SqlServer2022 = 16;

    [Theory]
    [InlineData(3)] // Enterprise, Developer, Evaluation
    [InlineData(5)] // Azure SQL Database
    [InlineData(8)] // Azure SQL Managed Instance
    [InlineData(12)] // SQL database in Microsoft Fabric
    public void GivenAnIndexLackingTheIncludeOnAnOnlineCapableEdition_WhenDecided_ThenConvertsOnline(int engineEdition)
    {
        var plan = ResourceSurrogateIdIndexOnlineMigration.Decide(ResourceSurrogateIdIndexState.LacksInclude, engineEdition, SqlServer2022);

        plan.Action.ShouldBe(ResourceSurrogateIdIndexMigrationAction.ConvertOnline);
    }

    [Theory]
    [InlineData(2)] // Standard, Web
    [InlineData(4)] // Express
    [InlineData(9)] // Azure SQL Edge -- not known to support online index operations
    [InlineData(42)] // an edition this build has never heard of
    public void GivenAnIndexLackingTheIncludeOnAnEditionWithoutOnlineIndexOperations_WhenDecided_ThenDefersToTheOfflineDeployNamingTheEdition(int engineEdition)
    {
        var plan = ResourceSurrogateIdIndexOnlineMigration.Decide(ResourceSurrogateIdIndexState.LacksInclude, engineEdition, SqlServer2022);

        plan.Action.ShouldBe(ResourceSurrogateIdIndexMigrationAction.DeferConversionToOfflineDeploy);
        plan.Reason.ShouldContain($"engine edition {engineEdition}");
        plan.OfflineDeployConsequence.ShouldNotBeNull().ShouldContain("DROP INDEX");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(12)]
    public void GivenAMissingIndexOnAnOnlineCapableEdition_WhenDecided_ThenCreatesItOnline(int engineEdition)
    {
        var plan = ResourceSurrogateIdIndexOnlineMigration.Decide(ResourceSurrogateIdIndexState.Missing, engineEdition, SqlServer2022);

        plan.Action.ShouldBe(ResourceSurrogateIdIndexMigrationAction.CreateOnline);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(9)]
    public void GivenAMissingIndexOnAnEditionWithoutOnlineIndexOperations_WhenDecided_ThenDefersTheCreationToTheOfflineDeploy(int engineEdition)
    {
        var plan = ResourceSurrogateIdIndexOnlineMigration.Decide(ResourceSurrogateIdIndexState.Missing, engineEdition, SqlServer2022);

        plan.Action.ShouldBe(ResourceSurrogateIdIndexMigrationAction.DeferCreationToOfflineDeploy);
        plan.Reason.ShouldContain($"engine edition {engineEdition}");
        plan.OfflineDeployConsequence.ShouldNotBeNull().ShouldContain("create it OFFLINE");
    }

    [Theory]
    [InlineData(nameof(ResourceSurrogateIdIndexState.IncludesResourceId), 2)]
    [InlineData(nameof(ResourceSurrogateIdIndexState.IncludesResourceId), 3)]
    [InlineData(nameof(ResourceSurrogateIdIndexState.NoResourceTable), 2)]
    [InlineData(nameof(ResourceSurrogateIdIndexState.NoResourceTable), 3)]
    public void GivenAnIndexThatNeedsNoWork_WhenDecided_ThenDoesNothingOnAnyEdition(string indexState, int engineEdition)
    {
        var plan = ResourceSurrogateIdIndexOnlineMigration.Decide(
            Enum.Parse<ResourceSurrogateIdIndexState>(indexState), engineEdition, SqlServer2022);

        plan.Action.ShouldBe(ResourceSurrogateIdIndexMigrationAction.None);
        plan.OfflineDeployConsequence.ShouldBeNull();
    }

    [Theory]
    [InlineData(3, SqlServer2022)] // box SQL Server 2022
    [InlineData(3, 17)] // box SQL Server 2025
    [InlineData(5, 12)] // Azure SQL Database reports ProductMajorVersion 12 regardless of features
    [InlineData(8, 12)]
    [InlineData(12, 12)]
    public void GivenAnEngineAcceptingWaitAtLowPriority_WhenDecided_ThenTheStatementWaitsAtLowPriorityWithoutAbandoningTheBuild(
        int engineEdition, int productMajorVersion)
    {
        foreach (var indexState in new[] { ResourceSurrogateIdIndexState.LacksInclude, ResourceSurrogateIdIndexState.Missing })
        {
            var plan = ResourceSurrogateIdIndexOnlineMigration.Decide(indexState, engineEdition, productMajorVersion);

            plan.WaitAtLowPriority.ShouldBeTrue();
            ResourceSurrogateIdIndexOnlineMigration.OnlineStatement(plan).ShouldContain(
                $"ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = {ResourceSurrogateIdIndexOnlineMigration.LowPriorityMaxDurationMinutes} MINUTES, ABORT_AFTER_WAIT = NONE))");
        }
    }

    [Fact]
    public void GivenABoxSqlServerOlderThan2022_WhenDecided_ThenTheStatementUsesPlainOnlineAndTheReasonSaysWhy()
    {
        var plan = ResourceSurrogateIdIndexOnlineMigration.Decide(ResourceSurrogateIdIndexState.LacksInclude, 3, SqlServer2019);

        plan.Action.ShouldBe(ResourceSurrogateIdIndexMigrationAction.ConvertOnline);
        plan.WaitAtLowPriority.ShouldBeFalse();
        plan.Reason.ShouldContain("WAIT_AT_LOW_PRIORITY needs SQL Server 2022");
        var statement = ResourceSurrogateIdIndexOnlineMigration.OnlineStatement(plan);
        statement.ShouldContain("WITH (DROP_EXISTING = ON, ONLINE = ON)");
        statement.ShouldNotContain("WAIT_AT_LOW_PRIORITY");
    }

    [Fact]
    public void GivenAConversionAndACreation_WhenTheirStatementsAreBuilt_ThenOnlyTheConversionReplacesAnExistingIndex()
    {
        var convert = ResourceSurrogateIdIndexOnlineMigration.OnlineStatement(
            ResourceSurrogateIdIndexOnlineMigration.Decide(ResourceSurrogateIdIndexState.LacksInclude, 3, SqlServer2022));
        var create = ResourceSurrogateIdIndexOnlineMigration.OnlineStatement(
            ResourceSurrogateIdIndexOnlineMigration.Decide(ResourceSurrogateIdIndexState.Missing, 3, SqlServer2022));

        convert.ShouldContain("DROP_EXISTING = ON");
        create.ShouldNotContain("DROP_EXISTING");
        create.ShouldContain("INCLUDE (ResourceId)");
    }

    [Theory]
    [InlineData(ResourceSurrogateIdIndexOnlineMigration.TargetSchemaVersion)]
    [InlineData(ResourceSurrogateIdIndexOnlineMigration.TargetSchemaVersion + 1)]
    public async Task GivenATenantAtOrAboveTheTargetVersion_WhenPlanned_ThenDoesNothingWithoutConnecting(int currentVersion)
    {
        var plan = await ResourceSurrogateIdIndexOnlineMigration.PlanAsync(
            UnreachableConnectionString, currentVersion, CancellationToken.None);

        plan.Action.ShouldBe(ResourceSurrogateIdIndexMigrationAction.None);
    }

    [Fact]
    public async Task GivenATenantBelowTheTargetVersion_WhenPlanned_ThenInspectsTheDatabase()
    {
        // The counterpart of the version gate above: below the target version the plan has to read the
        // index's shape, so against an unreachable server it fails rather than guessing.
        await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() => ResourceSurrogateIdIndexOnlineMigration.PlanAsync(
            UnreachableConnectionString, ResourceSurrogateIdIndexOnlineMigration.TargetSchemaVersion - 1, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAnOfflineConversionFallbackPlan_WhenApplied_ThenLogsAWarningNamingTenantIndexAndReasonAndRunsNoSql()
    {
        var logger = new RecordingLogger<SchemaDeployer>();
        var plan = ResourceSurrogateIdIndexOnlineMigration.Decide(ResourceSurrogateIdIndexState.LacksInclude, engineEdition: 2, SqlServer2022);

        await ResourceSurrogateIdIndexOnlineMigration.ApplyAsync(UnreachableConnectionString, plan, tenantId: 17, logger, CancellationToken.None);

        var warning = logger.Messages(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain("Tenant 17");
        warning.ShouldContain(ResourceSurrogateIdIndexOnlineMigration.IndexName);
        warning.ShouldContain("engine edition 2");
        warning.ShouldContain("OFFLINE");
    }

    [Fact]
    public async Task GivenAnOfflineCreationFallbackPlan_WhenApplied_ThenLogsAWarningThatTheIndexIsMissingAndRunsNoSql()
    {
        var logger = new RecordingLogger<SchemaDeployer>();
        var plan = ResourceSurrogateIdIndexOnlineMigration.Decide(ResourceSurrogateIdIndexState.Missing, engineEdition: 4, SqlServer2022);

        await ResourceSurrogateIdIndexOnlineMigration.ApplyAsync(UnreachableConnectionString, plan, tenantId: 17, logger, CancellationToken.None);

        var warning = logger.Messages(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain("Tenant 17");
        warning.ShouldContain($"{ResourceSurrogateIdIndexOnlineMigration.IndexName} is missing from dbo.Resource");
        warning.ShouldContain("engine edition 4");
        warning.ShouldContain("create it OFFLINE");
    }

    [Fact]
    public async Task GivenANoOpPlan_WhenApplied_ThenLogsNoWarningAndRunsNoSql()
    {
        var logger = new RecordingLogger<SchemaDeployer>();
        var plan = ResourceSurrogateIdIndexOnlineMigration.Decide(ResourceSurrogateIdIndexState.IncludesResourceId, engineEdition: 2, SqlServer2022);

        await ResourceSurrogateIdIndexOnlineMigration.ApplyAsync(UnreachableConnectionString, plan, tenantId: 17, logger, CancellationToken.None);

        logger.Messages(LogLevel.Warning).ShouldBeEmpty();
    }
}
