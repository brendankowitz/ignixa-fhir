CREATE PROCEDURE dbo.EnableLastNCodeGroupScope
    @ResourceTypeId SMALLINT,
    @SearchParamId SMALLINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE generation WITH (UPDLOCK, HOLDLOCK)
        SET ResourceTypeId = generation.ResourceTypeId
        FROM dbo.LastNCodeGroupGeneration AS generation
        WHERE generation.ResourceTypeId = @ResourceTypeId
            AND generation.SearchParamId = @SearchParamId;

        IF @@ROWCOUNT = 0
        BEGIN
            INSERT INTO dbo.LastNCodeGroupGeneration
                (ResourceTypeId, SearchParamId, Generation, AttemptId, State, SnapshotHighWaterSurrogateId,
                 LastCommittedResourceSurrogateId, LeaseExpiresDateTime, StartedDateTime,
                 CompletedDateTime, FailureReason)
            VALUES
                (@ResourceTypeId, @SearchParamId, 0, NULL, 'Pending', NULL,
                 NULL, NULL, SYSUTCDATETIME(), NULL, NULL);
        END;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
