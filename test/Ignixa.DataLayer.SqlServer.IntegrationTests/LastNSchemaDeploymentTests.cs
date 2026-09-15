using System.Data;
using Ignixa.DataLayer.SqlServer;
using Microsoft.Data.SqlClient;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class LastNSchemaDeploymentTests
{
    [SkippableFact]
    public async Task GivenTheCurrentDacpac_WhenDeployed_ThenLastNMaterializationCatalogMatchesTheDesign()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();

        // Act
        IReadOnlyList<string> tables = await database.ReadStringsAsync(
            "SELECT name FROM sys.tables WHERE name LIKE 'LastN%' ORDER BY name;");

        // Assert
        tables.ShouldBe([
            "LastNCodeEdge",
            "LastNCodeGroupDirtyObservation",
            "LastNCodeGroupGeneration",
            "LastNCodeIdentity",
            "LastNObservationCodeGroup",
            "LastNObservationCodeMembership",
        ]);
        (await database.ReadColumnNamesAsync("LastNCodeIdentity")).ShouldBe([
            "CodeIdentityId",
            "ResourceTypeId",
            "SearchParamId",
            "SystemId",
            "Code",
            "CodeOverflow",
            "CodeHash",
            "ComponentCodeIdentityId",
        ]);
        (await database.ReadColumnNamesAsync("LastNObservationCodeMembership")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "ResourceSurrogateId",
            "CodeIdentityId",
        ]);
        (await database.ReadColumnNamesAsync("LastNCodeEdge")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "LeftCodeIdentityId",
            "RightCodeIdentityId",
            "SupportCount",
        ]);
        (await database.ReadColumnNamesAsync("LastNObservationCodeGroup")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "ResourceSurrogateId",
            "GroupKind",
            "CodeGroupId",
            "TextCode",
        ]);
        (await database.ReadColumnNamesAsync("LastNCodeGroupGeneration")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "Generation",
            "AttemptId",
            "State",
            "SnapshotHighWaterSurrogateId",
            "LastCommittedResourceSurrogateId",
            "LeaseExpiresDateTime",
            "StartedDateTime",
            "CompletedDateTime",
            "FailureReason",
        ]);
        (await database.ReadColumnNamesAsync("LastNCodeGroupDirtyObservation")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "Generation",
            "ResourceSurrogateId",
        ]);
        (await database.ReadTableTypeColumnsAsync("LastNResourceScopeList")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "ResourceSurrogateId",
        ]);
        (await database.ReadTableTypePrimaryKeyColumnsAsync("LastNResourceScopeList")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "ResourceSurrogateId",
        ]);

        (await database.ReadPrimaryKeyColumnsAsync("LastNCodeIdentity"))
            .ShouldBe(["CodeIdentityId"]);
        (await database.ReadPrimaryKeyColumnsAsync("LastNObservationCodeMembership"))
            .ShouldBe(["ResourceTypeId", "SearchParamId", "ResourceSurrogateId", "CodeIdentityId"]);
        (await database.ReadPrimaryKeyColumnsAsync("LastNCodeEdge"))
            .ShouldBe(["ResourceTypeId", "SearchParamId", "LeftCodeIdentityId", "RightCodeIdentityId"]);
        (await database.ReadPrimaryKeyColumnsAsync("LastNObservationCodeGroup"))
            .ShouldBe(["ResourceTypeId", "SearchParamId", "ResourceSurrogateId"]);
        (await database.ReadPrimaryKeyColumnsAsync("LastNCodeGroupGeneration"))
            .ShouldBe(["ResourceTypeId", "SearchParamId"]);
        (await database.ReadPrimaryKeyColumnsAsync("LastNCodeGroupDirtyObservation"))
            .ShouldBe(["ResourceTypeId", "SearchParamId", "Generation", "ResourceSurrogateId"]);

        (await database.ReadIndexNamesAsync("LastNCodeIdentity")).ShouldBe([
            "IX_LastNCodeIdentity_Component",
            "IX_LastNCodeIdentity_Lookup",
            "UX_LastNCodeIdentity_Id_Scope",
        ]);
        (await database.ReadIndexColumnsAsync("LastNCodeIdentity", "UX_LastNCodeIdentity_Id_Scope")).ShouldBe([
            "CodeIdentityId",
            "ResourceTypeId",
            "SearchParamId",
        ]);
        (await database.ReadIndexColumnsAsync("LastNCodeIdentity", "IX_LastNCodeIdentity_Lookup")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "CodeHash",
            "INCLUDE:SystemId",
            "INCLUDE:Code",
            "INCLUDE:CodeOverflow",
        ]);
        (await database.ReadIndexColumnsAsync("LastNCodeIdentity", "IX_LastNCodeIdentity_Component")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "ComponentCodeIdentityId",
            "CodeIdentityId",
        ]);
        (await database.ReadIndexNamesAsync("LastNObservationCodeMembership"))
            .ShouldBe(["IX_LastNObservationCodeMembership_Code"]);
        (await database.ReadIndexColumnsAsync("LastNObservationCodeMembership", "IX_LastNObservationCodeMembership_Code")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "CodeIdentityId",
            "ResourceSurrogateId",
        ]);
        (await database.ReadIndexNamesAsync("LastNCodeEdge"))
            .ShouldBe(["IX_LastNCodeEdge_Right"]);
        (await database.ReadIndexColumnsAsync("LastNCodeEdge", "IX_LastNCodeEdge_Right")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "RightCodeIdentityId",
            "LeftCodeIdentityId",
        ]);
        (await database.ReadIndexNamesAsync("LastNObservationCodeGroup"))
            .ShouldBe(["IX_LastNObservationCodeGroup_Rank"]);
        (await database.ReadIndexColumnsAsync("LastNObservationCodeGroup", "IX_LastNObservationCodeGroup_Rank")).ShouldBe([
            "ResourceTypeId",
            "SearchParamId",
            "GroupKind",
            "CodeGroupId",
            "TextCode",
            "ResourceSurrogateId",
        ]);

        (await database.ReadCheckDefinitionsAsync("LastNCodeEdge"))
            .ShouldContain(definition => definition.Contains("[LeftCodeIdentityId]<[RightCodeIdentityId]"));
        (await database.ReadCheckDefinitionsAsync("LastNCodeEdge"))
            .ShouldContain(definition => definition.Contains("[SupportCount]>0"));
        (await database.ReadCheckDefinitionsAsync("LastNCodeGroupGeneration"))
            .Single().ShouldContain("'Pending'");
        (await database.ReadForeignKeyNamesAsync("LastNObservationCodeMembership"))
            .ShouldBe(["FK_LastNObservationCodeMembership_Identity"]);
        (await database.ReadForeignKeyNamesAsync("LastNCodeEdge")).ShouldBe([
            "FK_LastNCodeEdge_Left",
            "FK_LastNCodeEdge_Right",
        ]);

        (await database.ReadColumnCollationAsync("LastNCodeIdentity", "Code"))
            .ShouldBe("Latin1_General_100_CS_AS");
        (await database.ReadColumnCollationAsync("LastNCodeIdentity", "CodeOverflow"))
            .ShouldBe("Latin1_General_100_CS_AS");
        (await database.ReadColumnCollationAsync("LastNObservationCodeGroup", "TextCode"))
            .ShouldBe("Latin1_General_100_CS_AS");

        await AssertGroupRepresentationsAsync(database, CancellationToken.None);
        await AssertGenerationStatesAsync(database, CancellationToken.None);
        SchemaVersionConstants.CurrentVersion.ShouldBe(2);
    }

    private static async Task AssertGroupRepresentationsAsync(
        LastNTestDatabase database,
        CancellationToken cancellationToken)
    {
        // The break this catches is accepting a mixed or empty representation, which would make
        // a row ambiguous to the materialized LastN query.
        await InsertGroupAsync(database, resourceSurrogateId: 1, groupKind: 0, codeGroupId: 10, textCode: null, cancellationToken);
        await InsertGroupAsync(database, resourceSurrogateId: 2, groupKind: 1, codeGroupId: null, textCode: "text-only", cancellationToken);

        await Should.ThrowAsync<SqlException>(
            () => InsertGroupAsync(database, resourceSurrogateId: 3, groupKind: 0, codeGroupId: 11, textCode: "invalid", cancellationToken));
        await Should.ThrowAsync<SqlException>(
            () => InsertGroupAsync(database, resourceSurrogateId: 4, groupKind: 1, codeGroupId: null, textCode: null, cancellationToken));
    }

    private static async Task AssertGenerationStatesAsync(
        LastNTestDatabase database,
        CancellationToken cancellationToken)
    {
        string[] validStates = ["Pending", "Building", "Ready", "Failed"];
        for (short resourceTypeId = 1; resourceTypeId <= validStates.Length; resourceTypeId++)
        {
            await InsertGenerationAsync(database, resourceTypeId, validStates[resourceTypeId - 1], cancellationToken);
        }

        await Should.ThrowAsync<SqlException>(
            () => InsertGenerationAsync(database, resourceTypeId: 5, state: "Invalid", cancellationToken));
    }

    private static async Task InsertGroupAsync(
        LastNTestDatabase database,
        long resourceSurrogateId,
        byte groupKind,
        long? codeGroupId,
        string? textCode,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.LastNObservationCodeGroup
                (ResourceTypeId, SearchParamId, ResourceSurrogateId, GroupKind, CodeGroupId, TextCode)
            VALUES
                (@resourceTypeId, @searchParamId, @resourceSurrogateId, @groupKind, @codeGroupId, @textCode);
            """;
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = (short)1;
        command.Parameters.Add("@searchParamId", SqlDbType.SmallInt).Value = (short)1;
        command.Parameters.Add("@resourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        command.Parameters.Add("@groupKind", SqlDbType.TinyInt).Value = groupKind;
        command.Parameters.Add("@codeGroupId", SqlDbType.BigInt).Value = codeGroupId is long value ? value : DBNull.Value;
        command.Parameters.Add("@textCode", SqlDbType.NVarChar, 400).Value = textCode ?? (object)DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertGenerationAsync(
        LastNTestDatabase database,
        short resourceTypeId,
        string state,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.LastNCodeGroupGeneration
                (ResourceTypeId, SearchParamId, Generation, State, StartedDateTime)
            VALUES (@resourceTypeId, @searchParamId, @generation, @state, @startedDateTime);
            """;
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@searchParamId", SqlDbType.SmallInt).Value = (short)1;
        command.Parameters.Add("@generation", SqlDbType.BigInt).Value = 0L;
        command.Parameters.Add("@state", SqlDbType.VarChar, 16).Value = state;
        command.Parameters.Add("@startedDateTime", SqlDbType.DateTime2).Value = DateTime.UtcNow;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
