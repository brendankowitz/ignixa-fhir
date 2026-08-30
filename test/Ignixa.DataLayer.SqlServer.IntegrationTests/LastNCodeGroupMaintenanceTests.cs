using System.Data;
using Microsoft.Data.SqlClient;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class LastNCodeGroupMaintenanceTests
{
    private const short ResourceTypeId = 104;
    private const short SearchParamId = 210;

    [SkippableFact]
    public async Task GivenTransitiveBridges_WhenContributionsAreAdded_ThenAllIdentitiesUseTheMinimumComponent()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["a", "b"]);
        await SeedObservationAsync(database, 2, ["b", "c"]);

        // Act
        await MaintainAsync(database, "Add", [1, 2]);

        // Assert
        IReadOnlyList<long> labels = await ReadComponentLabelsAsync(database, ["a", "b", "c"]);
        labels.Distinct().Count().ShouldBe(1);
        labels[0].ShouldBe(await ReadMinimumIdentityIdAsync(database, ["a", "b", "c"]));
    }

    [SkippableFact]
    public async Task GivenTwoObservationsSupportingOneEdge_WhenOneIsRemoved_ThenSupportRemainsOne()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["a", "b"]);
        await SeedObservationAsync(database, 2, ["a", "b"]);
        await MaintainAsync(database, "Add", [1, 2]);

        // Act
        await MaintainAsync(database, "Remove", [1]);

        // Assert
        (await ReadSupportCountAsync(database, "a", "b")).ShouldBe(1);
    }

    [SkippableFact]
    public async Task GivenTwoObservationsSupportingOneEdge_WhenBothAreRemovedTogether_ThenTheEdgeIsDeleted()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["a", "b"]);
        await SeedObservationAsync(database, 2, ["a", "b"]);
        await MaintainAsync(database, "Add", [1, 2]);

        // Act
        await MaintainAsync(database, "Remove", [1, 2]);

        // Assert
        (await ReadCountAsync(database, "dbo.LastNCodeEdge")).ShouldBe(0);
    }

    [SkippableFact]
    public async Task GivenTheLastBridgeIsRemoved_WhenRepairRuns_ThenTheComponentSplits()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["a", "b"]);
        await SeedObservationAsync(database, 2, ["b", "c"]);
        await MaintainAsync(database, "Add", [1, 2]);

        // Act
        await MaintainAsync(database, "Remove", [2]);

        // Assert
        (await ReadComponentLabelAsync(database, "a")).ShouldBe(await ReadComponentLabelAsync(database, "b"));
        (await ReadComponentLabelAsync(database, "c")).ShouldNotBe(await ReadComponentLabelAsync(database, "a"));
    }

    [SkippableFact]
    public async Task GivenAnExistingContribution_WhenAddRunsAgain_ThenSupportAndRowsAreUnchanged()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["a", "b"]);
        await MaintainAsync(database, "Add", [1]);

        // Act
        await MaintainAsync(database, "Add", [1]);

        // Assert
        (await ReadSupportCountAsync(database, "a", "b")).ShouldBe(1);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeMembership")).ShouldBe(2);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(1);
    }

    [SkippableFact]
    public async Task GivenTwoExistingComponents_WhenABridgeIsAdded_ThenPriorGroupsUseTheMergedMinimum()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["a", "b"]);
        await SeedObservationAsync(database, 2, ["c", "d"]);
        await MaintainAsync(database, "Add", [1, 2]);
        await SeedObservationAsync(database, 3, ["b", "c"]);

        // Act
        await MaintainAsync(database, "Add", [3]);

        // Assert
        long minimumIdentityId = await ReadMinimumIdentityIdAsync(database, ["a", "b", "c", "d"]);
        (await ReadComponentLabelsAsync(database, ["a", "b", "c", "d"]))
            .ShouldAllBe(label => label == minimumIdentityId);
        (await ReadGroupCodeIdAsync(database, 1)).ShouldBe(minimumIdentityId);
        (await ReadGroupCodeIdAsync(database, 2)).ShouldBe(minimumIdentityId);
        (await ReadGroupCodeIdAsync(database, 3)).ShouldBe(minimumIdentityId);
    }

    [SkippableFact]
    public async Task GivenABridgeBetweenTwoSupportedComponents_WhenItIsRemoved_ThenLabelsAndPriorGroupsUseEachMinimum()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["a", "b"]);
        await SeedObservationAsync(database, 2, ["b", "c"]);
        await SeedObservationAsync(database, 3, ["c", "d"]);
        await MaintainAsync(database, "Add", [1, 2, 3]);

        // Act
        await MaintainAsync(database, "Remove", [2]);

        // Assert
        long leftMinimum = await ReadMinimumIdentityIdAsync(database, ["a", "b"]);
        long rightMinimum = await ReadMinimumIdentityIdAsync(database, ["c", "d"]);
        (await ReadComponentLabelAsync(database, "a")).ShouldBe(leftMinimum);
        (await ReadComponentLabelAsync(database, "b")).ShouldBe(leftMinimum);
        (await ReadComponentLabelAsync(database, "c")).ShouldBe(rightMinimum);
        (await ReadComponentLabelAsync(database, "d")).ShouldBe(rightMinimum);
        rightMinimum.ShouldNotBe(leftMinimum);
        (await ReadGroupCodeIdAsync(database, 1)).ShouldBe(leftMinimum);
        (await ReadGroupCodeIdAsync(database, 3)).ShouldBe(rightMinimum);
    }

    [SkippableFact]
    public async Task GivenConcurrentAddsOnIndependentConnections_WhenTheySupportTheSameEdge_ThenBothContributionsAreCounted()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["a", "b"]);
        await SeedObservationAsync(database, 2, ["a", "b"]);
        await using SqlConnection firstConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqlConnection secondConnection = await database.OpenConnectionAsync(CancellationToken.None);

        // Act
        await Task.WhenAll(
            MaintainAsync(firstConnection, null, "Add", [1], CancellationToken.None),
            MaintainAsync(secondConnection, null, "Add", [2], CancellationToken.None));

        // Assert
        (await ReadSupportCountAsync(database, "a", "b")).ShouldBe(2);
    }

    [SkippableFact]
    public async Task GivenMaintenanceRunsInsideAnOuterTransaction_WhenTheOuterTransactionRollsBack_ThenMaintenanceIsNotCommitted()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["a", "b"]);
        await using SqlTransaction transaction = (SqlTransaction)await database.Connection.BeginTransactionAsync();

        // Act
        await MaintainAsync(database.Connection, transaction, "Add", [1], CancellationToken.None);
        await transaction.RollbackAsync();

        // Assert
        (await ReadCountAsync(database, "dbo.LastNCodeIdentity")).ShouldBe(0);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeMembership")).ShouldBe(0);
        (await ReadCountAsync(database, "dbo.LastNCodeEdge")).ShouldBe(0);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(0);
    }

    [SkippableFact]
    public async Task GivenMaintenanceFailsInsideAnOuterTransaction_WhenTheSavepointRollsBack_ThenTheOuterTransactionRemainsCommittable()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedTextOnlyObservationAsync(database, 1, "Alpha");
        await database.SeedTokenTextAsync(ResourceTypeId, 1, SearchParamId, "alpha", false, CancellationToken.None);
        await using SqlTransaction transaction = (SqlTransaction)await database.Connection.BeginTransactionAsync();

        // Act
        SqlException exception = await Should.ThrowAsync<SqlException>(
            () => MaintainAsync(database.Connection, transaction, "Add", [1], CancellationToken.None));
        await ExecuteNonQueryAsync(
            database.Connection,
            transaction,
            "INSERT INTO dbo.ResourceType (Name) VALUES ('savepoint-proof');",
            CancellationToken.None);
        await transaction.CommitAsync();

        // Assert
        exception.Number.ShouldBe(50402);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(0);
        (await ReadScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.ResourceType WHERE Name = 'savepoint-proof';",
            CancellationToken.None)).ShouldBe(1);
    }

    [SkippableFact]
    public async Task GivenAContributionWasAlreadyRemoved_WhenRemoveRunsAgain_ThenTheOperationIsIdempotent()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["only"]);
        await MaintainAsync(database, "Add", [1]);
        await MaintainAsync(database, "Remove", [1]);

        // Act
        await MaintainAsync(database, "Remove", [1]);

        // Assert
        (await ReadCountAsync(database, "dbo.LastNObservationCodeMembership")).ShouldBe(0);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(0);
        (await ReadCountAsync(database, "dbo.LastNCodeIdentity")).ShouldBe(1);
    }

    [SkippableFact]
    public async Task GivenOneCoding_WhenContributionIsAdded_ThenOneCodedGroupAndNoEdgeAreStored()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["only"]);

        // Act
        await MaintainAsync(database, "Add", [1]);

        // Assert
        (await ReadCountAsync(database, "dbo.LastNCodeIdentity")).ShouldBe(1);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeMembership")).ShouldBe(1);
        (await ReadCountAsync(database, "dbo.LastNCodeEdge")).ShouldBe(0);
        (await ReadGroupKindAsync(database, 1)).ShouldBe((byte)0);
        (await ReadGroupCodeIdAsync(database, 1)).ShouldBe(await ReadComponentLabelAsync(database, "only"));
    }

    [SkippableFact]
    public async Task GivenDuplicateCodingRows_WhenContributionIsAdded_ThenCodingAndMembershipAreDeduplicated()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await database.SeedResourceAsync(ResourceTypeId, 1, "observation-1", 1, false, false, CancellationToken.None);
        await database.SeedTokenSearchParamAsync(ResourceTypeId, 1, SearchParamId, 7, "duplicate", null, CancellationToken.None);
        await database.SeedTokenSearchParamAsync(ResourceTypeId, 1, SearchParamId, 7, "duplicate", null, CancellationToken.None);

        // Act
        await MaintainAsync(database, "Add", [1]);

        // Assert
        (await ReadCountAsync(database, "dbo.LastNCodeIdentity")).ShouldBe(1);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeMembership")).ShouldBe(1);
        (await ReadCountAsync(database, "dbo.LastNCodeEdge")).ShouldBe(0);
    }

    [SkippableFact]
    public async Task GivenTextOnlyValuesDifferByCase_WhenContributionsAreAdded_ThenDistinctTextGroupsPreserveCase()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedTextOnlyObservationAsync(database, 1, "Alpha");
        await SeedTextOnlyObservationAsync(database, 2, "alpha");

        // Act
        await MaintainAsync(database, "Add", [1, 2]);

        // Assert
        (await ReadTextGroupAsync(database, 1)).ShouldBe("Alpha");
        (await ReadTextGroupAsync(database, 2)).ShouldBe("alpha");
        (await ReadGroupKindAsync(database, 1)).ShouldBe((byte)1);
        (await ReadGroupKindAsync(database, 2)).ShouldBe((byte)1);
    }

    [SkippableFact]
    public async Task GivenNoCodingOrText_WhenContributionIsAdded_ThenNoGroupIsStored()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await database.SeedResourceAsync(ResourceTypeId, 1, "observation-1", 1, false, false, CancellationToken.None);

        // Act
        await MaintainAsync(database, "Add", [1]);

        // Assert
        (await ReadCountAsync(database, "dbo.LastNCodeIdentity")).ShouldBe(0);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeMembership")).ShouldBe(0);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(0);
    }

    [SkippableFact]
    public async Task GivenCurrentHistoricalAndDeletedResources_WhenContributionsAreAdded_ThenOnlyCurrentDataIsStored()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await database.SeedResourceAsync(ResourceTypeId, 1, "current", 1, false, false, CancellationToken.None);
        await database.SeedResourceAsync(ResourceTypeId, 2, "historical", 1, true, false, CancellationToken.None);
        await database.SeedResourceAsync(ResourceTypeId, 3, "deleted", 1, false, true, CancellationToken.None);
        await database.SeedTokenSearchParamAsync(ResourceTypeId, 1, SearchParamId, 7, "current", null, CancellationToken.None);
        await database.SeedTokenSearchParamAsync(ResourceTypeId, 2, SearchParamId, 7, "historical", null, CancellationToken.None);
        await database.SeedTokenSearchParamAsync(ResourceTypeId, 3, SearchParamId, 7, "deleted", null, CancellationToken.None);

        // Act
        await MaintainAsync(database, "Add", [1, 2, 3]);

        // Assert
        (await ReadCountAsync(database, "dbo.LastNCodeIdentity")).ShouldBe(1);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeMembership")).ShouldBe(1);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(1);
        (await ReadTextGroupCodeAsync(database, 1)).ShouldBe("current");
    }

    [SkippableFact]
    public async Task GivenNullAndNonNullSystemsWithTheSameCode_WhenContributionsAreAdded_ThenIdentitiesRemainDistinct()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await database.SeedResourceAsync(ResourceTypeId, 1, "observation-1", 1, false, false, CancellationToken.None);
        await database.SeedResourceAsync(ResourceTypeId, 2, "observation-2", 1, false, false, CancellationToken.None);
        await database.SeedTokenSearchParamAsync(ResourceTypeId, 1, SearchParamId, null, "same", null, CancellationToken.None);
        await database.SeedTokenSearchParamAsync(ResourceTypeId, 2, SearchParamId, 0, "same", null, CancellationToken.None);

        // Act
        await MaintainAsync(database, "Add", [1, 2]);

        // Assert
        (await ReadCountAsync(database, "dbo.LastNCodeIdentity")).ShouldBe(2);
        (await ReadGroupCodeIdAsync(database, 1)).ShouldNotBe(await ReadGroupCodeIdAsync(database, 2));
    }

    [SkippableFact]
    public async Task GivenEqualLongCodePrefixesAndDifferentOverflow_WhenContributionsAreAdded_ThenFullValuesRemainDistinct()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        string prefix = new('x', 256);
        await database.SeedResourceAsync(ResourceTypeId, 1, "observation-1", 1, false, false, CancellationToken.None);
        await database.SeedResourceAsync(ResourceTypeId, 2, "observation-2", 1, false, false, CancellationToken.None);
        await database.SeedTokenSearchParamAsync(ResourceTypeId, 1, SearchParamId, 7, prefix, "Overflow", CancellationToken.None);
        await database.SeedTokenSearchParamAsync(ResourceTypeId, 2, SearchParamId, 7, prefix, "overflow", CancellationToken.None);

        // Act
        await MaintainAsync(database, "Add", [1, 2]);

        // Assert
        (await ReadCountAsync(database, "dbo.LastNCodeIdentity")).ShouldBe(2);
        (await ReadGroupCodeIdAsync(database, 1)).ShouldNotBe(await ReadGroupCodeIdAsync(database, 2));
    }

    [SkippableFact]
    public async Task GivenAHashCollision_WhenContributionIsAdded_ThenFullEqualitySelectsTheMatchingIdentity()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["target"]);
        await MaintainAsync(database, "Add", [1]);
        long matchingIdentityId = await ReadIdentityIdAsync(database, "target");
        byte[] hash = await ReadIdentityHashAsync(database, matchingIdentityId);
        await MaintainAsync(database, "Remove", [1]);
        await SeedCollidingIdentityAsync(database, 0, "decoy", hash);
        await ExplicitlySetIdentityHashAsync(database, matchingIdentityId, hash);
        await SeedObservationAsync(database, 2, ["target"]);

        // Act
        await MaintainAsync(database, "Add", [2]);

        // Assert
        (await ReadMembershipIdentityIdAsync(database, 2)).ShouldBe(matchingIdentityId);
        (await ReadCountAsync(database, "dbo.LastNCodeIdentity")).ShouldBe(2);
    }

    [SkippableFact]
    public async Task GivenMultipleCaseDistinctTextValues_WhenContributionIsAdded_ThenAmbiguityIsRejected()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedTextOnlyObservationAsync(database, 1, "Alpha");
        await database.SeedTokenTextAsync(ResourceTypeId, 1, SearchParamId, "alpha", false, CancellationToken.None);

        // Act
        SqlException exception = await Should.ThrowAsync<SqlException>(
            () => MaintainAsync(database, "Add", [1]));

        // Assert
        exception.Number.ShouldBe(50402);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(0);
    }

    [SkippableFact]
    public async Task GivenAStoredMembershipHasNoSupportingEdge_WhenContributionIsRemoved_ThenCorruptionIsRejectedAtomically()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 1, ["a", "b"]);
        await MaintainAsync(database, "Add", [1]);
        await ExecuteNonQueryAsync(database, "DELETE FROM dbo.LastNCodeEdge;", CancellationToken.None);

        // Act
        SqlException exception = await Should.ThrowAsync<SqlException>(
            () => MaintainAsync(database, "Remove", [1]));

        // Assert
        exception.Number.ShouldBe(50401);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeMembership")).ShouldBe(2);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(1);
    }

    [SkippableFact]
    public async Task GivenAnUnsupportedMode_WhenMaintenanceRuns_ThenTheExactContractErrorIsReturned()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();

        // Act
        SqlException exception = await Should.ThrowAsync<SqlException>(
            () => MaintainAsync(database, "Replace", []));

        // Assert
        exception.Number.ShouldBe(50400);
        exception.Message.ShouldContain("MaintainLastNCodeGroups mode must be Remove or Add.");
    }

    [SkippableFact]
    public async Task GivenAReplacementMerge_WhenWrapperSucceeds_ThenOldContributionIsRemovedAndNewContributionIsCurrent()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId, "Building");
        await ExecuteMergeWrapperAsync(database.Connection, "observation", 1, 1, [(SearchParamId, "a"), (SearchParamId, "b")]);

        // Act
        await ExecuteMergeWrapperAsync(database.Connection, "observation", 2, 2, [(SearchParamId, "c")]);

        // Assert
        (await ReadMembershipCodesAsync(database, 1)).ShouldBeEmpty();
        (await ReadMembershipCodesAsync(database, 2)).ShouldBe(["c"]);
        (await ReadCurrentVersionAsync(database, "observation")).ShouldBe(2);
        (await ReadDirtyIdsAsync(database)).ShouldBe([1, 2]);
    }

    [SkippableFact]
    public async Task GivenGraphMaintenanceFails_WhenMergeWrapperRuns_ThenBaseRowsAndGroupsRollBack()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await ExecuteMergeWrapperAsync(database.Connection, "observation", 1, 1, [(SearchParamId, "a")]);
        await ExecuteNonQueryAsync(
            database,
            """
            CREATE TRIGGER dbo.FailLastNGroupInsert
            ON dbo.LastNObservationCodeGroup
            AFTER INSERT
            AS
                THROW 50499, 'Injected LastN maintenance failure.', 1;
            """,
            CancellationToken.None);

        // Act
        SqlException exception = await Should.ThrowAsync<SqlException>(
            () => ExecuteMergeWrapperAsync(database.Connection, "observation", 2, 2, [(SearchParamId, "b")]));

        // Assert
        exception.Number.ShouldBe(50499);
        (await ReadCurrentVersionAsync(database, "observation")).ShouldBe(1);
        (await ReadMembershipCodesAsync(database, 1)).ShouldBe(["a"]);
        (await ReadMembershipCodesAsync(database, 2)).ShouldBeEmpty();
    }

    [SkippableFact]
    public async Task GivenASuccessfulMergeIsRetried_WhenWrapperRunsAgain_ThenMaterializationIsIdempotent()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await ExecuteMergeWrapperAsync(database.Connection, "observation", 1, 1, [(SearchParamId, "a"), (SearchParamId, "b")]);

        // Act
        await ExecuteMergeWrapperAsync(database.Connection, "observation", 1, 1, [(SearchParamId, "a"), (SearchParamId, "b")]);

        // Assert
        (await ReadMembershipCodesAsync(database, 1)).ShouldBe(["a", "b"]);
        (await ReadSupportCountAsync(database, "a", "b")).ShouldBe(1);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(1);
    }

    [SkippableFact]
    public async Task GivenAPartialPriorMerge_WhenWrapperRetries_ThenTheCompleteBaseRetryContractIsPreserved()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await ExecuteNonQueryAsync(
            database,
            "INSERT INTO dbo.Parameters (Id, Char) VALUES ('MergeResources', 'LogEvent');",
            CancellationToken.None);
        await ExecuteCompleteMergeWrapperAsync(database.Connection, "partial-retry", 1, 1);
        await DeleteAllRetryIndexFamiliesAsync(database, 1);
        long transactionId = await BeginMergeTransactionAsync(database);

        // Act
        int affectedRows = await ExecuteCompleteMergeWrapperAsync(
            database.Connection,
            "partial-retry",
            1,
            1,
            isResourceChangeCaptureEnabled: true,
            transactionId);

        // Assert
        affectedRows.ShouldBe(15);
        (await ReadRetryIndexFamilyCountsAsync(database, 1)).ShouldAllBe(count => count == 1);
        (await ReadCountAsync(database, "dbo.ResourceChangeData")).ShouldBe(1);
        (await ReadTransactionCompletionAsync(database, transactionId)).ShouldBe((true, true));
        (await ReadMembershipCodesAsync(database, 1)).ShouldBe(["retry-code"]);
        (await ReadCountAsync(database, "dbo.EventLog")).ShouldBe(2);
        (await ReadScalarAsync<string>(
            database,
            "SELECT TOP 1 Mode FROM dbo.EventLog WHERE Process = 'MergeResources' ORDER BY EventId DESC;",
            CancellationToken.None)).ShouldContain("R=1");
    }

    [SkippableFact]
    public async Task GivenReindexedCodes_WhenUpdateWrapperSucceeds_ThenOnlyTheNewContributionRemains()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId, "Building");
        await ExecuteMergeWrapperAsync(database.Connection, "observation", 1, 1, [(SearchParamId, "old")]);

        // Act
        await ExecuteUpdateWrapperAsync(database.Connection, "observation", 1, 1, [(SearchParamId, "new")]);

        // Assert
        (await ReadMembershipCodesAsync(database, 1)).ShouldBe(["new"]);
        (await ReadDirtyIdsAsync(database)).ShouldBe([1]);
    }

    [SkippableFact]
    public async Task GivenAFullHardDelete_WhenWrapperSucceeds_ThenCurrentResourceAndContributionAreRemoved()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId, "Building");
        await ExecuteMergeWrapperAsync(database.Connection, "observation", 1, 1, [(SearchParamId, "a")]);

        // Act
        await ExecuteHardDeleteWrapperAsync(database.Connection, "observation", keepCurrentVersion: false);

        // Assert
        (await ReadResourceCountAsync(database, "observation")).ShouldBe(0);
        (await ReadMembershipCodesAsync(database, 1)).ShouldBeEmpty();
        (await ReadDirtyIdsAsync(database)).ShouldBe([1]);
    }

    [SkippableFact]
    public async Task GivenAHistoryOnlyHardDelete_WhenWrapperSucceeds_ThenCurrentMaterializationIsUnchanged()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await ExecuteMergeWrapperAsync(database.Connection, "observation", 1, 1, [(SearchParamId, "old")]);
        await ExecuteMergeWrapperAsync(database.Connection, "observation", 2, 2, [(SearchParamId, "current")]);

        // Act
        await ExecuteHardDeleteWrapperAsync(database.Connection, "observation", keepCurrentVersion: true);

        // Assert
        (await ReadCurrentVersionAsync(database, "observation")).ShouldBe(2);
        (await ReadMembershipCodesAsync(database, 2)).ShouldBe(["current"]);
    }

    [SkippableFact]
    public async Task GivenANullKeepCurrentVersion_WhenHardDeleteWrapperRuns_ThenBaseNullSemanticsAndCurrentMaterializationArePreserved()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await ExecuteMergeWrapperAsync(database.Connection, "null-delete", 1, 1, [(SearchParamId, "old")]);
        await ExecuteMergeWrapperAsync(database.Connection, "null-delete", 2, 2, [(SearchParamId, "current")]);

        // Act
        await ExecuteHardDeleteWrapperAsync(database.Connection, "null-delete", keepCurrentVersion: null);

        // Assert
        (await ReadResourceCountAsync(database, "null-delete")).ShouldBe(1);
        (await ReadCurrentVersionAsync(database, "null-delete")).ShouldBe(2);
        (await ReadMembershipCodesAsync(database, 2)).ShouldBe(["current"]);
    }

    [SkippableFact]
    public async Task GivenTheBaseMergeRejectsAConflict_WhenWrapperRuns_ThenRemovedMaterializationIsRestored()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await ExecuteMergeWrapperAsync(database.Connection, "observation", 1, 1, [(SearchParamId, "a")]);

        // Act
        SqlException exception = await Should.ThrowAsync<SqlException>(
            () => ExecuteMergeWrapperAsync(database.Connection, "observation", 2, 1, [(SearchParamId, "b")]));

        // Assert
        exception.Number.ShouldBe(50409);
        (await ReadCurrentVersionAsync(database, "observation")).ShouldBe(1);
        (await ReadMembershipCodesAsync(database, 1)).ShouldBe(["a"]);
    }

    [SkippableFact]
    public async Task GivenAnotherTransactionOwnsTheScopeLock_WhenMergeWrapperTimesOut_ThenNothingIsWritten()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await using SqlConnection lockConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqlTransaction lockTransaction = (SqlTransaction)await lockConnection.BeginTransactionAsync();
        (await AcquireScopeLockAsync(lockConnection, lockTransaction, SearchParamId, 0)).ShouldBeGreaterThanOrEqualTo(0);
        await using SqlConnection writerConnection = await database.OpenConnectionAsync(CancellationToken.None);

        // Act
        SqlException exception = await Should.ThrowAsync<SqlException>(
            () => ExecuteMergeWrapperAsync(writerConnection, "blocked", 1, 1, [(SearchParamId, "a")]));

        // Assert
        exception.Number.ShouldBe(50410);
        (await ReadResourceCountAsync(database, "blocked")).ShouldBe(0);
        (await ReadMembershipCodesAsync(database, 1)).ShouldBeEmpty();
        await lockTransaction.RollbackAsync();
    }

    [SkippableFact]
    public async Task GivenAReverseOrderedMultiScopeBatch_WhenWrapperBlocksOnTheHigherScope_ThenItAlreadyOwnsTheLowerScope()
    {
        // Arrange
        const short higherSearchParamId = SearchParamId + 1;
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await ConfigureScopeAsync(database, higherSearchParamId);
        await using SqlConnection highLockConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqlTransaction highLockTransaction = (SqlTransaction)await highLockConnection.BeginTransactionAsync();
        (await AcquireScopeLockAsync(highLockConnection, highLockTransaction, higherSearchParamId, 0)).ShouldBeGreaterThanOrEqualTo(0);
        await using SqlConnection writerConnection = await database.OpenConnectionAsync(CancellationToken.None);
        int writerSessionId = await ReadSessionIdAsync(writerConnection);
#pragma warning disable CA2025 // The task is awaited before writerConnection is disposed.
        Task writeTask = ExecuteMergeWrapperAsync(
            writerConnection,
            "ordered",
            1,
            1,
            [(higherSearchParamId, "higher"), (SearchParamId, "lower")]);
#pragma warning restore CA2025
        await WaitUntilBlockedAsync(database, writerSessionId);
        await using SqlConnection probeConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqlTransaction probeTransaction = (SqlTransaction)await probeConnection.BeginTransactionAsync();

        // Act
        int lowerLockResult = await AcquireScopeLockAsync(probeConnection, probeTransaction, SearchParamId, 0);

        // Assert
        lowerLockResult.ShouldBeLessThan(0);
        await probeTransaction.RollbackAsync();
        await highLockTransaction.RollbackAsync();
        await writeTask;
        (await ReadMembershipCodesAsync(database, 1)).ShouldBe(["higher", "lower"]);
    }

    [SkippableFact]
    public async Task GivenTwoReplacementMergesCaptureStateBeforeTheScopeLock_WhenTheySerialize_ThenOnlyTheFinalContributionRemains()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await ExecuteMergeWrapperAsync(database.Connection, "concurrent", 1, 1, [(SearchParamId, "first")]);
        await using SqlConnection lockConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqlTransaction lockTransaction = (SqlTransaction)await lockConnection.BeginTransactionAsync();
        (await AcquireScopeLockAsync(lockConnection, lockTransaction, SearchParamId, 0)).ShouldBeGreaterThanOrEqualTo(0);
        await using SqlConnection secondVersionConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqlConnection thirdVersionConnection = await database.OpenConnectionAsync(CancellationToken.None);
        int secondVersionSessionId = await ReadSessionIdAsync(secondVersionConnection);
        int thirdVersionSessionId = await ReadSessionIdAsync(thirdVersionConnection);
#pragma warning disable CA2025 // Both tasks are awaited before either connection is disposed.
        Task secondVersionWrite = ExecuteMergeWrapperAsync(
            secondVersionConnection,
            "concurrent",
            2,
            2,
            [(SearchParamId, "second")]);
        await WaitUntilBlockedAsync(database, secondVersionSessionId);
        Task thirdVersionWrite = ExecuteMergeWrapperAsync(
            thirdVersionConnection,
            "concurrent",
            3,
            3,
            [(SearchParamId, "third")]);
#pragma warning restore CA2025
        await WaitUntilBlockedAsync(database, thirdVersionSessionId);

        // Act
        await lockTransaction.RollbackAsync();
        await secondVersionWrite;
        await thirdVersionWrite;

        // Assert
        (await ReadCurrentVersionAsync(database, "concurrent")).ShouldBe(3);
        (await ReadMembershipCodesAsync(database, 1)).ShouldBeEmpty();
        (await ReadMembershipCodesAsync(database, 2)).ShouldBeEmpty();
        (await ReadMembershipCodesAsync(database, 3)).ShouldBe(["third"]);
    }

    [SkippableFact]
    public async Task GivenAReplacementMergePrecedesAFullHardDelete_WhenTheySerialize_ThenNoContributionSurvives()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await ExecuteMergeWrapperAsync(database.Connection, "delete-race", 1, 1, [(SearchParamId, "first")]);
        await using SqlConnection lockConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqlTransaction lockTransaction = (SqlTransaction)await lockConnection.BeginTransactionAsync();
        (await AcquireScopeLockAsync(lockConnection, lockTransaction, SearchParamId, 0)).ShouldBeGreaterThanOrEqualTo(0);
        await using SqlConnection mergeConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqlConnection deleteConnection = await database.OpenConnectionAsync(CancellationToken.None);
        int mergeSessionId = await ReadSessionIdAsync(mergeConnection);
        int deleteSessionId = await ReadSessionIdAsync(deleteConnection);
#pragma warning disable CA2025 // Both tasks are awaited before either connection is disposed.
        Task mergeTask = ExecuteMergeWrapperAsync(
            mergeConnection,
            "delete-race",
            2,
            2,
            [(SearchParamId, "second")]);
        await WaitUntilBlockedAsync(database, mergeSessionId);
        Task deleteTask = ExecuteHardDeleteWrapperAsync(deleteConnection, "delete-race", keepCurrentVersion: false);
#pragma warning restore CA2025
        await WaitUntilBlockedAsync(database, deleteSessionId);

        // Act
        await lockTransaction.RollbackAsync();
        await mergeTask;
        await deleteTask;

        // Assert
        (await ReadResourceCountAsync(database, "delete-race")).ShouldBe(0);
        (await ReadMembershipCodesAsync(database, 1)).ShouldBeEmpty();
        (await ReadMembershipCodesAsync(database, 2)).ShouldBeEmpty();
    }

    [SkippableFact]
    public async Task GivenMaintenanceIsStillRunning_WhenAnotherConnectionReads_ThenBaseStateIsNotPartiallyVisible()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await ExecuteNonQueryAsync(
            database,
            """
            CREATE TRIGGER dbo.DelayLastNGroupInsert
            ON dbo.LastNObservationCodeGroup
            AFTER INSERT
            AS
                WAITFOR DELAY '00:00:03';
            """,
            CancellationToken.None);
        await using SqlConnection writerConnection = await database.OpenConnectionAsync(CancellationToken.None);
        int writerSessionId = await ReadSessionIdAsync(writerConnection);
#pragma warning disable CA2025 // The task is awaited before writerConnection is disposed.
        Task writeTask = ExecuteMergeWrapperAsync(writerConnection, "delayed", 1, 1, [(SearchParamId, "a")]);
#pragma warning restore CA2025
        await WaitForWaitTypeAsync(database, writerSessionId, "WAITFOR");

        // Act
        int visibleResourceCount = await ReadResourceCountAsync(database, "delayed");

        // Assert
        visibleResourceCount.ShouldBe(0);
        await writeTask;
        (await ReadResourceCountAsync(database, "delayed")).ShouldBe(1);
        (await ReadMembershipCodesAsync(database, 1)).ShouldBe(["a"]);
    }

    [SkippableFact]
    public async Task GivenAnotherScopeIsLocked_WhenAnIndependentScopeWrites_ThenItIsNotMutuallyBlocked()
    {
        // Arrange
        const short independentResourceTypeId = ResourceTypeId + 1;
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, ResourceTypeId, SearchParamId);
        await ConfigureScopeAsync(database, independentResourceTypeId, SearchParamId);
        await using SqlConnection lockConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqlTransaction lockTransaction = (SqlTransaction)await lockConnection.BeginTransactionAsync();
        (await AcquireScopeLockAsync(lockConnection, lockTransaction, ResourceTypeId, SearchParamId, 0))
            .ShouldBeGreaterThanOrEqualTo(0);
        await using SqlConnection writerConnection = await database.OpenConnectionAsync(CancellationToken.None);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

        // Act
        await ExecuteMergeWrapperAsync(
            writerConnection,
            independentResourceTypeId,
            "independent",
            1,
            1,
            [(SearchParamId, "independent")],
            timeout.Token);

        // Assert
        (await ReadScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceTypeId = @resourceTypeId AND ResourceId = 'independent';",
            CancellationToken.None,
            new SqlParameter("@resourceTypeId", SqlDbType.SmallInt) { Value = independentResourceTypeId })).ShouldBe(1);
        await lockTransaction.RollbackAsync();
    }

    [SkippableFact]
    public async Task GivenAWrapperIsChosenAsADeadlockVictim_WhenAFreshWriterCallRetries_ThenTheWriteConverges()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId, state: "Building");
        await ExecuteNonQueryAsync(
            database,
            """
            CREATE TABLE dbo.LastNDeadlockGate (Id int NOT NULL PRIMARY KEY, Value int NOT NULL);
            INSERT INTO dbo.LastNDeadlockGate VALUES (1, 0);
            """,
            CancellationToken.None);
        await ExecuteNonQueryAsync(
            database,
            """
            CREATE TRIGGER dbo.CreateLastNWrapperDeadlock
            ON dbo.Resource
            AFTER INSERT
            AS
            IF EXISTS (SELECT 1 FROM inserted WHERE ResourceId = 'deadlock')
                UPDATE dbo.LastNDeadlockGate SET Value += 1 WHERE Id = 1;
            """,
            CancellationToken.None);
        await using SqlConnection blockerConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqlTransaction blockerTransaction = (SqlTransaction)await blockerConnection.BeginTransactionAsync();
        await ExecuteNonQueryAsync(
            blockerConnection,
            blockerTransaction,
            "UPDATE dbo.LastNDeadlockGate SET Value += 1 WHERE Id = 1;",
            CancellationToken.None);
        await using SqlConnection victimConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await ExecuteNonQueryAsync(
            victimConnection,
            null,
            "SET DEADLOCK_PRIORITY LOW;",
            CancellationToken.None);
        int victimSessionId = await ReadSessionIdAsync(victimConnection);
#pragma warning disable CA2025 // The task is awaited before victimConnection is disposed.
        Task victimWrite = ExecuteMergeWrapperAsync(
            victimConnection,
            "deadlock",
            1,
            1,
            [(SearchParamId, "first"), (SearchParamId, "second")]);
#pragma warning restore CA2025
        await WaitUntilBlockedAsync(database, victimSessionId);

        // Act
        int blockerLockResult = await AcquireScopeLockAsync(
            blockerConnection,
            blockerTransaction,
            SearchParamId,
            timeout: 5000);
        SqlException exception = await Should.ThrowAsync<SqlException>(() => victimWrite);

        blockerLockResult.ShouldBeGreaterThanOrEqualTo(0);
        exception.Number.ShouldBe(1205);
        (await ReadResourceCountAsync(database, "deadlock")).ShouldBe(0);
        (await ReadMembershipCodesAsync(database, 1)).ShouldBeEmpty();
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(0);
        (await ReadCountAsync(database, "dbo.LastNCodeEdge")).ShouldBe(0);
        (await ReadDirtyIdsAsync(database)).ShouldBeEmpty();

        await blockerTransaction.RollbackAsync();
        await ExecuteMergeWrapperAsync(
            database.Connection,
            "deadlock",
            1,
            1,
            [(SearchParamId, "first"), (SearchParamId, "second")]);

        // Assert
        (await ReadCurrentVersionAsync(database, "deadlock")).ShouldBe(1);
        (await ReadMembershipCodesAsync(database, 1)).ShouldBe(["first", "second"]);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(1);
        (await ReadSupportCountAsync(database, "first", "second")).ShouldBe(1);
        (await ReadDirtyIdsAsync(database)).ShouldBe([1]);
    }

    [SkippableFact]
    public async Task GivenAWrapperHasWrittenBaseRowsButNotGroups_WhenAReaderQueriesBoth_ThenNoCommittedGapIsObserved()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await ConfigureScopeAsync(database, SearchParamId);
        await ExecuteNonQueryAsync(
            database,
            """
            CREATE TRIGGER dbo.DelayLastNConsistencyInsert
            ON dbo.LastNObservationCodeGroup
            AFTER INSERT
            AS
                WAITFOR DELAY '00:00:03';
            """,
            CancellationToken.None);
        await using SqlConnection writerConnection = await database.OpenConnectionAsync(CancellationToken.None);
        int writerSessionId = await ReadSessionIdAsync(writerConnection);
#pragma warning disable CA2025 // Both tasks are awaited before their owning connections are disposed.
        Task writeTask = ExecuteMergeWrapperAsync(
            writerConnection,
            "consistent",
            1,
            1,
            [(SearchParamId, "a")]);
#pragma warning restore CA2025
        await WaitForWaitTypeAsync(database, writerSessionId, "WAITFOR");
        await using SqlConnection readerConnection = await database.OpenConnectionAsync(CancellationToken.None);
#pragma warning disable CA2025 // Both tasks are awaited before their owning connections are disposed.
        Task<int> inconsistentCountTask = ReadScalarAsync<int>(
            readerConnection,
            null,
            """
            SELECT COUNT(*)
            FROM dbo.Resource AS resource
            LEFT JOIN dbo.LastNObservationCodeGroup AS codeGroup
                ON codeGroup.ResourceTypeId = resource.ResourceTypeId
                AND codeGroup.ResourceSurrogateId = resource.ResourceSurrogateId
                AND codeGroup.SearchParamId = @searchParamId
            WHERE resource.ResourceId = 'consistent'
                AND resource.IsHistory = 0
                AND codeGroup.ResourceSurrogateId IS NULL;
            """,
            CancellationToken.None,
            new SqlParameter("@searchParamId", SqlDbType.SmallInt) { Value = SearchParamId });
#pragma warning restore CA2025

        // Act
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // Assert
        await writeTask;
        (await inconsistentCountTask).ShouldBe(0);
    }

    private static Task ConfigureScopeAsync(
        LastNTestDatabase database,
        short searchParamId,
        string state = "Ready")
        => ConfigureScopeAsync(database, ResourceTypeId, searchParamId, state);

    private static Task ConfigureScopeAsync(
        LastNTestDatabase database,
        short resourceTypeId,
        short searchParamId,
        string state = "Ready")
        => ExecuteNonQueryAsync(
            database,
            """
            INSERT INTO dbo.LastNCodeGroupGeneration
                (ResourceTypeId, SearchParamId, Generation, State, StartedDateTime)
            VALUES (@resourceTypeId, @searchParamId, 1, @state, SYSUTCDATETIME());
            """,
            CancellationToken.None,
            new SqlParameter("@resourceTypeId", SqlDbType.SmallInt) { Value = resourceTypeId },
            new SqlParameter("@searchParamId", SqlDbType.SmallInt) { Value = searchParamId },
            new SqlParameter("@state", SqlDbType.VarChar, 16) { Value = state });

    private static async Task ExecuteMergeWrapperAsync(
        SqlConnection connection,
        string resourceId,
        long resourceSurrogateId,
        int version,
        IReadOnlyList<(short SearchParamId, string Code)> tokens)
        => await ExecuteMergeWrapperAsync(
            connection,
            ResourceTypeId,
            resourceId,
            resourceSurrogateId,
            version,
            tokens,
            CancellationToken.None);

    private static async Task ExecuteMergeWrapperAsync(
        SqlConnection connection,
        short resourceTypeId,
        string resourceId,
        long resourceSurrogateId,
        int version,
        IReadOnlyList<(short SearchParamId, string Code)> tokens,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = DeclareWriteTvpsSql + """
            INSERT INTO @Resources
                (ResourceTypeId, ResourceSurrogateId, ResourceId, Version, HasVersionToCompare,
                 IsDeleted, IsHistory, KeepHistory, RawResource, IsRawResourceMetaSet, RequestMethod,
                 SearchParamHash)
            VALUES
                (@resourceTypeId, @resourceSurrogateId, @resourceId, @version, 1,
                 0, 0, 1, 0x01, 0, 'PUT', 'hash');
            INSERT INTO @TokenSearchParams
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
            SELECT @resourceTypeId, @resourceSurrogateId, token.SearchParamId, 7, token.Code, NULL
            FROM (VALUES
                (@searchParamId1, @code1),
                (@searchParamId2, @code2)) AS token(SearchParamId, Code)
            WHERE token.Code IS NOT NULL;
            DECLARE @AffectedRows INT;
            EXEC dbo.MergeResourcesAndMaintainLastNGroups
                @AffectedRows = @AffectedRows OUTPUT,
                @RaiseExceptionOnConflict = 1,
                @IsResourceChangeCaptureEnabled = 0,
                @TransactionId = NULL,
                @SingleTransaction = 1,
                @Resources = @Resources,
                @ResourceWriteClaims = @ResourceWriteClaims,
                @ReferenceSearchParams = @ReferenceSearchParams,
                @TokenSearchParams = @TokenSearchParams,
                @TokenTexts = @TokenTexts,
                @StringSearchParams = @StringSearchParams,
                @UriSearchParams = @UriSearchParams,
                @NumberSearchParams = @NumberSearchParams,
                @QuantitySearchParams = @QuantitySearchParams,
                @DateTimeSearchParms = @DateTimeSearchParams,
                @ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams,
                @TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams,
                @TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams,
                @TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams,
                @TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams,
                @TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams;
            """;
        AddWriteParameters(command, resourceTypeId, resourceId, resourceSurrogateId, version, tokens);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ExecuteCompleteMergeWrapperAsync(
        SqlConnection connection,
        string resourceId,
        long resourceSurrogateId,
        int version,
        bool isResourceChangeCaptureEnabled = false,
        long? transactionId = null)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = DeclareWriteTvpsSql + """
            INSERT INTO @Resources
                (ResourceTypeId, ResourceSurrogateId, ResourceId, Version, HasVersionToCompare,
                 IsDeleted, IsHistory, KeepHistory, RawResource, IsRawResourceMetaSet, RequestMethod,
                 SearchParamHash)
            VALUES
                (@resourceTypeId, @resourceSurrogateId, @resourceId, @version, 1,
                 0, 0, 1, 0x01, 0, 'PUT', 'complete-hash');
            INSERT INTO @ResourceWriteClaims VALUES (@resourceSurrogateId, 1, N'retry-claim');
            INSERT INTO @ReferenceSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 21, NULL, NULL, 'reference-id', NULL);
            INSERT INTO @TokenSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, @searchParamId, 7, 'retry-code', NULL);
            INSERT INTO @TokenTexts VALUES
                (@resourceTypeId, @resourceSurrogateId, 22, N'retry text');
            INSERT INTO @StringSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 23, N'retry string', NULL, 0, 0);
            INSERT INTO @UriSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 24, 'https://example.test/retry');
            INSERT INTO @NumberSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 25, 1, 1, 1);
            INSERT INTO @QuantitySearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 26, 7, 8, 2, 2, 2);
            INSERT INTO @DateTimeSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 27,
                 '2026-08-29T00:00:00+00:00', '2026-08-30T00:00:00+00:00', 0, 0, 0);
            INSERT INTO @ReferenceTokenCompositeSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 28, NULL, NULL, 'reference-id', NULL,
                 7, 'reference-token', NULL);
            INSERT INTO @TokenTokenCompositeSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 29,
                 7, 'token-one', NULL, 8, 'token-two', NULL);
            INSERT INTO @TokenDateTimeCompositeSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 30, 7, 'token-date', NULL,
                 '2026-08-29T00:00:00+00:00', '2026-08-30T00:00:00+00:00', 0);
            INSERT INTO @TokenQuantityCompositeSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 31, 7, 'token-quantity', NULL,
                 8, 9, 3, 3, 3);
            INSERT INTO @TokenStringCompositeSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 32, 7, 'token-string', NULL,
                 N'composite string', NULL);
            INSERT INTO @TokenNumberNumberCompositeSearchParams VALUES
                (@resourceTypeId, @resourceSurrogateId, 33, 7, 'token-number', NULL,
                 4, 4, 4, 5, 5, 5, 0);
            DECLARE @AffectedRows INT;
            EXEC dbo.MergeResourcesAndMaintainLastNGroups
                @AffectedRows = @AffectedRows OUTPUT,
                @RaiseExceptionOnConflict = 1,
                @IsResourceChangeCaptureEnabled = @isResourceChangeCaptureEnabled,
                @TransactionId = @transactionId,
                @SingleTransaction = 1,
                @Resources = @Resources,
                @ResourceWriteClaims = @ResourceWriteClaims,
                @ReferenceSearchParams = @ReferenceSearchParams,
                @TokenSearchParams = @TokenSearchParams,
                @TokenTexts = @TokenTexts,
                @StringSearchParams = @StringSearchParams,
                @UriSearchParams = @UriSearchParams,
                @NumberSearchParams = @NumberSearchParams,
                @QuantitySearchParams = @QuantitySearchParams,
                @DateTimeSearchParms = @DateTimeSearchParams,
                @ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams,
                @TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams,
                @TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams,
                @TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams,
                @TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams,
                @TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams;
            SELECT @AffectedRows;
            """;
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = ResourceTypeId;
        command.Parameters.Add("@resourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        command.Parameters.Add("@resourceId", SqlDbType.VarChar, 64).Value = resourceId;
        command.Parameters.Add("@version", SqlDbType.Int).Value = version;
        command.Parameters.Add("@searchParamId", SqlDbType.SmallInt).Value = SearchParamId;
        command.Parameters.Add("@isResourceChangeCaptureEnabled", SqlDbType.Bit).Value =
            isResourceChangeCaptureEnabled;
        command.Parameters.Add("@transactionId", SqlDbType.BigInt).Value =
            transactionId is null ? DBNull.Value : transactionId.Value;
        return (int)(await command.ExecuteScalarAsync(CancellationToken.None)).ShouldNotBeNull();
    }

    private static async Task ExecuteUpdateWrapperAsync(
        SqlConnection connection,
        string resourceId,
        long resourceSurrogateId,
        int version,
        IReadOnlyList<(short SearchParamId, string Code)> tokens)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = DeclareWriteTvpsSql + """
            INSERT INTO @Resources
                (ResourceTypeId, ResourceSurrogateId, ResourceId, Version, HasVersionToCompare,
                 IsDeleted, IsHistory, KeepHistory, RawResource, IsRawResourceMetaSet, RequestMethod,
                 SearchParamHash)
            VALUES
                (@resourceTypeId, @resourceSurrogateId, @resourceId, @version, 0,
                 0, 0, 1, 0x01, 0, 'PUT', 'new-hash');
            INSERT INTO @TokenSearchParams
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
            SELECT @resourceTypeId, @resourceSurrogateId, token.SearchParamId, 7, token.Code, NULL
            FROM (VALUES
                (@searchParamId1, @code1),
                (@searchParamId2, @code2)) AS token(SearchParamId, Code)
            WHERE token.Code IS NOT NULL;
            DECLARE @FailedResources INT;
            EXEC dbo.UpdateResourceSearchParamsAndMaintainLastNGroups
                @FailedResources = @FailedResources OUTPUT,
                @Resources = @Resources,
                @ResourceWriteClaims = @ResourceWriteClaims,
                @ReferenceSearchParams = @ReferenceSearchParams,
                @TokenSearchParams = @TokenSearchParams,
                @TokenTexts = @TokenTexts,
                @StringSearchParams = @StringSearchParams,
                @UriSearchParams = @UriSearchParams,
                @NumberSearchParams = @NumberSearchParams,
                @QuantitySearchParams = @QuantitySearchParams,
                @DateTimeSearchParams = @DateTimeSearchParams,
                @ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams,
                @TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams,
                @TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams,
                @TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams,
                @TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams,
                @TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams;
            """;
        AddWriteParameters(command, resourceId, resourceSurrogateId, version, tokens);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task ExecuteHardDeleteWrapperAsync(
        SqlConnection connection,
        string resourceId,
        bool? keepCurrentVersion)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "dbo.HardDeleteResourceAndMaintainLastNGroups";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = ResourceTypeId;
        command.Parameters.Add("@ResourceId", SqlDbType.VarChar, 64).Value = resourceId;
        command.Parameters.Add("@KeepCurrentVersion", SqlDbType.Bit).Value =
            keepCurrentVersion is null ? DBNull.Value : keepCurrentVersion.Value;
        command.Parameters.Add("@IsResourceChangeCaptureEnabled", SqlDbType.Bit).Value = false;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static Task DeleteAllRetryIndexFamiliesAsync(LastNTestDatabase database, long resourceSurrogateId)
        => ExecuteNonQueryAsync(
            database,
            """
            DELETE FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.ReferenceSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.TokenSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.TokenText WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.StringSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.UriSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.NumberSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.QuantitySearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.DateTimeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.ReferenceTokenCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.TokenTokenCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.TokenDateTimeCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.TokenQuantityCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.TokenStringCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            DELETE FROM dbo.TokenNumberNumberCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId;
            """,
            CancellationToken.None,
            new SqlParameter("@resourceSurrogateId", SqlDbType.BigInt) { Value = resourceSurrogateId });

    private static async Task<IReadOnlyList<int>> ReadRetryIndexFamilyCountsAsync(
        LastNTestDatabase database,
        long resourceSurrogateId)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT COUNT(*) FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.ReferenceSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.TokenText WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.StringSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.UriSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.NumberSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.QuantitySearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.DateTimeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.ReferenceTokenCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.TokenTokenCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.TokenDateTimeCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.TokenQuantityCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.TokenStringCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId),
                   (SELECT COUNT(*) FROM dbo.TokenNumberNumberCompositeSearchParam WHERE ResourceSurrogateId = @resourceSurrogateId);
            """;
        command.Parameters.Add("@resourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();
        return Enumerable.Range(0, reader.FieldCount).Select(reader.GetInt32).ToArray();
    }

    private static async Task<long> BeginMergeTransactionAsync(LastNTestDatabase database)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            DECLARE @TransactionId BIGINT;
            EXEC dbo.MergeResourcesBeginTransaction @Count = 1, @TransactionId = @TransactionId OUTPUT;
            SELECT @TransactionId;
            """;
        return (long)(await command.ExecuteScalarAsync(CancellationToken.None)).ShouldNotBeNull();
    }

    private static async Task<(bool IsCompleted, bool IsSuccess)> ReadTransactionCompletionAsync(
        LastNTestDatabase database,
        long transactionId)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            SELECT IsCompleted, IsSuccess
            FROM dbo.Transactions
            WHERE SurrogateIdRangeFirstValue = @transactionId;
            """;
        command.Parameters.Add("@transactionId", SqlDbType.BigInt).Value = transactionId;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();
        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    private static void AddWriteParameters(
        SqlCommand command,
        string resourceId,
        long resourceSurrogateId,
        int version,
        IReadOnlyList<(short SearchParamId, string Code)> tokens)
        => AddWriteParameters(command, ResourceTypeId, resourceId, resourceSurrogateId, version, tokens);

    private static void AddWriteParameters(
        SqlCommand command,
        short resourceTypeId,
        string resourceId,
        long resourceSurrogateId,
        int version,
        IReadOnlyList<(short SearchParamId, string Code)> tokens)
    {
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@resourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        command.Parameters.Add("@resourceId", SqlDbType.VarChar, 64).Value = resourceId;
        command.Parameters.Add("@version", SqlDbType.Int).Value = version;
        command.Parameters.Add("@searchParamId1", SqlDbType.SmallInt).Value = tokens[0].SearchParamId;
        command.Parameters.Add("@code1", SqlDbType.VarChar, 256).Value = tokens[0].Code;
        command.Parameters.Add("@searchParamId2", SqlDbType.SmallInt).Value =
            tokens.Count > 1 ? tokens[1].SearchParamId : SearchParamId;
        command.Parameters.Add("@code2", SqlDbType.VarChar, 256).Value =
            tokens.Count > 1 ? tokens[1].Code : DBNull.Value;
    }

    private static async Task<int> AcquireScopeLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        short searchParamId,
        int timeout)
        => await AcquireScopeLockAsync(connection, transaction, ResourceTypeId, searchParamId, timeout);

    private static async Task<int> AcquireScopeLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        short resourceTypeId,
        short searchParamId,
        int timeout)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @Result INT;
            DECLARE @Resource NVARCHAR(255) =
                CONCAT('LastNCodeGroup:', @resourceTypeId, ':', @searchParamId);
            EXEC @Result = sys.sp_getapplock
                @Resource = @Resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @timeout;
            SELECT @Result;
            """;
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@searchParamId", SqlDbType.SmallInt).Value = searchParamId;
        command.Parameters.Add("@timeout", SqlDbType.Int).Value = timeout;
        return (int)(await command.ExecuteScalarAsync(CancellationToken.None)).ShouldNotBeNull();
    }

    private static async Task<int> ReadSessionIdAsync(SqlConnection connection)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT @@SPID;";
        return (short)(await command.ExecuteScalarAsync(CancellationToken.None)).ShouldNotBeNull();
    }

    private static async Task WaitUntilBlockedAsync(LastNTestDatabase database, int sessionId)
        => await WaitForRequestAsync(
            database,
            sessionId,
            "blocking_session_id <> 0",
            null,
            CancellationToken.None);

    private static async Task WaitForWaitTypeAsync(LastNTestDatabase database, int sessionId, string waitType)
        => await WaitForRequestAsync(
            database,
            sessionId,
            "wait_type = @waitType",
            waitType,
            CancellationToken.None);

    private static async Task WaitForRequestAsync(
        LastNTestDatabase database,
        int sessionId,
        string predicate,
        string? waitType,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            int found = await ReadScalarAsync<int>(
                database,
                $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id = @sessionId AND {predicate};",
                timeout.Token,
                new SqlParameter("@sessionId", SqlDbType.Int) { Value = sessionId },
                new SqlParameter("@waitType", SqlDbType.NVarChar, 60)
                {
                    Value = waitType is null ? DBNull.Value : waitType,
                });
            if (found == 1)
            {
                return;
            }

            await Task.Delay(50, timeout.Token);
        }
    }

    private static async Task<IReadOnlyList<string>> ReadMembershipCodesAsync(
        LastNTestDatabase database,
        long resourceSurrogateId)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            SELECT identityRow.Code
            FROM dbo.LastNObservationCodeMembership AS membership
            INNER JOIN dbo.LastNCodeIdentity AS identityRow
                ON identityRow.CodeIdentityId = membership.CodeIdentityId
            WHERE membership.ResourceSurrogateId = @resourceSurrogateId
            ORDER BY identityRow.Code;
            """;
        command.Parameters.Add("@resourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        List<string> codes = [];
        while (await reader.ReadAsync(CancellationToken.None))
        {
            codes.Add(reader.GetString(0));
        }

        return codes;
    }

    private static async Task<IReadOnlyList<long>> ReadDirtyIdsAsync(LastNTestDatabase database)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            SELECT ResourceSurrogateId
            FROM dbo.LastNCodeGroupDirtyObservation
            ORDER BY ResourceSurrogateId;
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        List<long> ids = [];
        while (await reader.ReadAsync(CancellationToken.None))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private static Task<int> ReadCurrentVersionAsync(LastNTestDatabase database, string resourceId)
        => ReadScalarAsync<int>(
            database,
            "SELECT Version FROM dbo.Resource WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0;",
            CancellationToken.None,
            new SqlParameter("@resourceTypeId", SqlDbType.SmallInt) { Value = ResourceTypeId },
            new SqlParameter("@resourceId", SqlDbType.VarChar, 64) { Value = resourceId });

    private static Task<int> ReadResourceCountAsync(LastNTestDatabase database, string resourceId)
        => ReadScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId;",
            CancellationToken.None,
            new SqlParameter("@resourceTypeId", SqlDbType.SmallInt) { Value = ResourceTypeId },
            new SqlParameter("@resourceId", SqlDbType.VarChar, 64) { Value = resourceId });

    private const string DeclareWriteTvpsSql = """
        DECLARE @Resources dbo.ResourceList;
        DECLARE @ResourceWriteClaims dbo.ResourceWriteClaimList;
        DECLARE @ReferenceSearchParams dbo.ReferenceSearchParamList;
        DECLARE @TokenSearchParams dbo.TokenSearchParamList;
        DECLARE @TokenTexts dbo.TokenTextList;
        DECLARE @StringSearchParams dbo.StringSearchParamList;
        DECLARE @UriSearchParams dbo.UriSearchParamList;
        DECLARE @NumberSearchParams dbo.NumberSearchParamList;
        DECLARE @QuantitySearchParams dbo.QuantitySearchParamList;
        DECLARE @DateTimeSearchParams dbo.DateTimeSearchParamList;
        DECLARE @ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList;
        DECLARE @TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList;
        DECLARE @TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList;
        DECLARE @TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList;
        DECLARE @TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList;
        DECLARE @TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList;
        """;

    private static async Task SeedObservationAsync(
        LastNTestDatabase database,
        long resourceSurrogateId,
        IReadOnlyList<string> codes)
    {
        await database.SeedResourceAsync(
            ResourceTypeId,
            resourceSurrogateId,
            $"observation-{resourceSurrogateId}",
            1,
            false,
            false,
            CancellationToken.None);

        foreach (string code in codes)
        {
            await database.SeedTokenSearchParamAsync(
                ResourceTypeId,
                resourceSurrogateId,
                SearchParamId,
                7,
                code,
                null,
                CancellationToken.None);
        }
    }

    private static async Task SeedTextOnlyObservationAsync(
        LastNTestDatabase database,
        long resourceSurrogateId,
        string text)
    {
        await database.SeedResourceAsync(
            ResourceTypeId,
            resourceSurrogateId,
            $"observation-{resourceSurrogateId}",
            1,
            false,
            false,
            CancellationToken.None);
        await database.SeedTokenTextAsync(
            ResourceTypeId,
            resourceSurrogateId,
            SearchParamId,
            text,
            false,
            CancellationToken.None);
    }

    private static async Task MaintainAsync(
        LastNTestDatabase database,
        string mode,
        IReadOnlyList<long> resourceSurrogateIds)
        => await MaintainAsync(
            database.Connection,
            null,
            mode,
            resourceSurrogateIds,
            CancellationToken.None);

    private static async Task MaintainAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string mode,
        IReadOnlyList<long> resourceSurrogateIds,
        CancellationToken cancellationToken)
    {
        using DataTable resources = new();
        resources.Columns.Add("ResourceTypeId", typeof(short));
        resources.Columns.Add("SearchParamId", typeof(short));
        resources.Columns.Add("ResourceSurrogateId", typeof(long));
        foreach (long resourceSurrogateId in resourceSurrogateIds)
        {
            resources.Rows.Add(ResourceTypeId, SearchParamId, resourceSurrogateId);
        }

        SqlParameter modeParameter = new("@Mode", SqlDbType.VarChar, 8)
        {
            Value = mode,
        };
        SqlParameter resourcesParameter = new("@Resources", SqlDbType.Structured)
        {
            TypeName = "dbo.LastNResourceScopeList",
            Value = resources,
        };

        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "dbo.MaintainLastNCodeGroups";
        command.CommandType = CommandType.StoredProcedure;
        command.Transaction = transaction;
        command.Parameters.Add(modeParameter);
        command.Parameters.Add(resourcesParameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Task<int> ReadCountAsync(LastNTestDatabase database, string tableName)
        => ReadScalarAsync<int>(database, $"SELECT COUNT(*) FROM {tableName};", CancellationToken.None);

    private static Task<int> ReadSupportCountAsync(LastNTestDatabase database, string leftCode, string rightCode)
        => ReadScalarAsync<int>(
            database,
            """
            SELECT edge.SupportCount
            FROM dbo.LastNCodeEdge AS edge
            INNER JOIN dbo.LastNCodeIdentity AS leftIdentity
                ON leftIdentity.CodeIdentityId = edge.LeftCodeIdentityId
            INNER JOIN dbo.LastNCodeIdentity AS rightIdentity
                ON rightIdentity.CodeIdentityId = edge.RightCodeIdentityId
            WHERE leftIdentity.Code = @leftCode AND rightIdentity.Code = @rightCode;
            """,
            CancellationToken.None,
            new SqlParameter("@leftCode", SqlDbType.VarChar, 256) { Value = leftCode },
            new SqlParameter("@rightCode", SqlDbType.VarChar, 256) { Value = rightCode });

    private static Task<long> ReadComponentLabelAsync(LastNTestDatabase database, string code)
        => ReadScalarAsync<long>(
            database,
            "SELECT ComponentCodeIdentityId FROM dbo.LastNCodeIdentity WHERE Code = @code;",
            CancellationToken.None,
            new SqlParameter("@code", SqlDbType.VarChar, 256) { Value = code });

    private static async Task<IReadOnlyList<long>> ReadComponentLabelsAsync(
        LastNTestDatabase database,
        IReadOnlyList<string> codes)
    {
        List<long> labels = [];
        foreach (string code in codes)
        {
            labels.Add(await ReadComponentLabelAsync(database, code));
        }

        return labels;
    }

    private static async Task<long> ReadMinimumIdentityIdAsync(
        LastNTestDatabase database,
        IReadOnlyList<string> codes)
    {
        List<long> identities = [];
        foreach (string code in codes)
        {
            identities.Add(await ReadIdentityIdAsync(database, code));
        }

        return identities.Min();
    }

    private static Task<long> ReadIdentityIdAsync(LastNTestDatabase database, string code)
        => ReadScalarAsync<long>(
            database,
            "SELECT CodeIdentityId FROM dbo.LastNCodeIdentity WHERE Code = @code;",
            CancellationToken.None,
            new SqlParameter("@code", SqlDbType.VarChar, 256) { Value = code });

    private static Task<byte> ReadGroupKindAsync(LastNTestDatabase database, long resourceSurrogateId)
        => ReadScalarAsync<byte>(
            database,
            "SELECT GroupKind FROM dbo.LastNObservationCodeGroup WHERE ResourceSurrogateId = @resourceSurrogateId;",
            CancellationToken.None,
            new SqlParameter("@resourceSurrogateId", SqlDbType.BigInt) { Value = resourceSurrogateId });

    private static Task<long> ReadGroupCodeIdAsync(LastNTestDatabase database, long resourceSurrogateId)
        => ReadScalarAsync<long>(
            database,
            "SELECT CodeGroupId FROM dbo.LastNObservationCodeGroup WHERE ResourceSurrogateId = @resourceSurrogateId;",
            CancellationToken.None,
            new SqlParameter("@resourceSurrogateId", SqlDbType.BigInt) { Value = resourceSurrogateId });

    private static Task<string> ReadTextGroupAsync(LastNTestDatabase database, long resourceSurrogateId)
        => ReadScalarAsync<string>(
            database,
            "SELECT TextCode FROM dbo.LastNObservationCodeGroup WHERE ResourceSurrogateId = @resourceSurrogateId;",
            CancellationToken.None,
            new SqlParameter("@resourceSurrogateId", SqlDbType.BigInt) { Value = resourceSurrogateId });

    private static Task<string> ReadTextGroupCodeAsync(LastNTestDatabase database, long resourceSurrogateId)
        => ReadScalarAsync<string>(
            database,
            """
            SELECT identityRow.Code
            FROM dbo.LastNObservationCodeGroup AS groupRow
            INNER JOIN dbo.LastNCodeIdentity AS identityRow
                ON identityRow.CodeIdentityId = groupRow.CodeGroupId
            WHERE groupRow.ResourceSurrogateId = @resourceSurrogateId;
            """,
            CancellationToken.None,
            new SqlParameter("@resourceSurrogateId", SqlDbType.BigInt) { Value = resourceSurrogateId });

    private static Task<long> ReadMembershipIdentityIdAsync(
        LastNTestDatabase database,
        long resourceSurrogateId)
        => ReadScalarAsync<long>(
            database,
            """
            SELECT CodeIdentityId
            FROM dbo.LastNObservationCodeMembership
            WHERE ResourceSurrogateId = @resourceSurrogateId;
            """,
            CancellationToken.None,
            new SqlParameter("@resourceSurrogateId", SqlDbType.BigInt) { Value = resourceSurrogateId });

    private static Task<byte[]> ReadIdentityHashAsync(LastNTestDatabase database, long codeIdentityId)
        => ReadScalarAsync<byte[]>(
            database,
            "SELECT CodeHash FROM dbo.LastNCodeIdentity WHERE CodeIdentityId = @codeIdentityId;",
            CancellationToken.None,
            new SqlParameter("@codeIdentityId", SqlDbType.BigInt) { Value = codeIdentityId });

    private static Task SeedCollidingIdentityAsync(
        LastNTestDatabase database,
        long codeIdentityId,
        string code,
        byte[] hash)
        => ExecuteNonQueryAsync(
            database,
            """
            SET IDENTITY_INSERT dbo.LastNCodeIdentity ON;
            INSERT INTO dbo.LastNCodeIdentity
                (CodeIdentityId, ResourceTypeId, SearchParamId, SystemId, Code, CodeOverflow, CodeHash,
                 ComponentCodeIdentityId)
            VALUES
                (@codeIdentityId, @resourceTypeId, @searchParamId, 7, @code, NULL, @hash, @codeIdentityId);
            SET IDENTITY_INSERT dbo.LastNCodeIdentity OFF;
            """,
            CancellationToken.None,
            new SqlParameter("@codeIdentityId", SqlDbType.BigInt) { Value = codeIdentityId },
            new SqlParameter("@resourceTypeId", SqlDbType.SmallInt) { Value = ResourceTypeId },
            new SqlParameter("@searchParamId", SqlDbType.SmallInt) { Value = SearchParamId },
            new SqlParameter("@code", SqlDbType.VarChar, 256) { Value = code },
            new SqlParameter("@hash", SqlDbType.Binary, 32) { Value = hash });

    private static Task ExplicitlySetIdentityHashAsync(
        LastNTestDatabase database,
        long codeIdentityId,
        byte[] hash)
        => ExecuteNonQueryAsync(
            database,
            """
            UPDATE dbo.LastNCodeIdentity
            SET CodeHash = @hash
            WHERE CodeIdentityId = @codeIdentityId;
            """,
            CancellationToken.None,
            new SqlParameter("@codeIdentityId", SqlDbType.BigInt) { Value = codeIdentityId },
            new SqlParameter("@hash", SqlDbType.Binary, 32) { Value = hash });

    private static async Task<T> ReadScalarAsync<T>(
        LastNTestDatabase database,
        string commandText,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
        => await ReadScalarAsync<T>(
            database.Connection,
            null,
            commandText,
            cancellationToken,
            parameters);

    private static async Task<T> ReadScalarAsync<T>(
        SqlConnection connection,
        SqlTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
#pragma warning disable CA2100
        command.CommandText = commandText;
#pragma warning restore CA2100
        command.Parameters.AddRange(parameters);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        result.ShouldNotBeNull();
        result.ShouldNotBe(DBNull.Value);
        return (T)result;
    }

    private static async Task ExecuteNonQueryAsync(
        LastNTestDatabase database,
        string commandText,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
        => await ExecuteNonQueryAsync(
            database.Connection,
            null,
            commandText,
            cancellationToken,
            parameters);

    private static async Task ExecuteNonQueryAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
    {
        await using SqlCommand command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = commandText;
#pragma warning restore CA2100
        command.Transaction = transaction;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
