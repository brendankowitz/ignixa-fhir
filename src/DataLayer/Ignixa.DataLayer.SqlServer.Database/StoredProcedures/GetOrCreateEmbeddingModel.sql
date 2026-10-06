-- Race-safe without an explicit lock: a bare SELECT-then-INSERT takes no lock on a key that does not yet
-- exist (range locks only apply under SERIALIZABLE), so two concurrent callers creating the same new
-- ModelKey would both see "not found" and both attempt the INSERT. Rather than preventing that race, this
-- procedure lets it happen and resolves it afterward -- the loser's INSERT fails on
-- UQ_EmbeddingModel_ModelKey (error 2627) or the clustered PK (2601), which is caught, and the loser then
-- SELECTs the winner's row. Both callers return the same EmbeddingModelId either way.
CREATE PROCEDURE dbo.GetOrCreateEmbeddingModel
@ModelKey VARCHAR (256), @Dimensions SMALLINT, @EmbeddingModelId SMALLINT OUTPUT
AS
SET NOCOUNT ON;
DECLARE @ExistingDimensions AS SMALLINT;
SET @EmbeddingModelId = NULL;
BEGIN TRY
    INSERT INTO dbo.EmbeddingModel (ModelKey, Dimensions)
    VALUES (@ModelKey, @Dimensions);
    SET @EmbeddingModelId = CONVERT (SMALLINT, scope_identity());
    RETURN;
END TRY
BEGIN CATCH
    IF error_number() NOT IN (2601, 2627)
        THROW;
END CATCH
SELECT @EmbeddingModelId = EmbeddingModelId,
       @ExistingDimensions = Dimensions
FROM   dbo.EmbeddingModel
WHERE  ModelKey = @ModelKey;
IF @ExistingDimensions <> @Dimensions
    THROW 50409, 'EmbeddingModel exists with a different Dimensions value.', 1;
