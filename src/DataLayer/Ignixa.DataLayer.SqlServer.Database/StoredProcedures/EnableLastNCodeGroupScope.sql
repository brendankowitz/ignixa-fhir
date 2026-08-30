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
                (ResourceTypeId, SearchParamId, Generation, State, SnapshotHighWaterSurrogateId,
                 StartedDateTime, CompletedDateTime, FailureReason)
            VALUES
                (@ResourceTypeId, @SearchParamId, 0, 'Pending', NULL,
                 SYSUTCDATETIME(), NULL, NULL);
        END;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
