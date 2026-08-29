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
    {
        await using SqlCommand command = database.Connection.CreateCommand();
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
