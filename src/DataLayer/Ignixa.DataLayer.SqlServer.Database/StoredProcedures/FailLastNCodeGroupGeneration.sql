CREATE PROCEDURE dbo.FailLastNCodeGroupGeneration
    @ResourceTypeId SMALLINT,
    @SearchParamId SMALLINT,
    @Generation BIGINT,
    @FailureReason VARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @LockResult INT;
    DECLARE @LockResource NVARCHAR(255) =
        CONCAT('LastNCodeGroup:', @ResourceTypeId, ':', @SearchParamId);

    BEGIN TRY
        BEGIN TRANSACTION;

        EXEC @LockResult = sys.sp_getapplock
            @Resource = @LockResource,
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 15000;
        IF @LockResult < 0
            THROW 50410, 'Unable to acquire LastN code-group scope lock.', 1;

        UPDATE dbo.LastNCodeGroupGeneration WITH (UPDLOCK, HOLDLOCK)
        SET State = 'Failed',
            CompletedDateTime = NULL,
            FailureReason = LEFT(@FailureReason, 1000)
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId
            AND Generation = @Generation
            AND State = 'Building';

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
