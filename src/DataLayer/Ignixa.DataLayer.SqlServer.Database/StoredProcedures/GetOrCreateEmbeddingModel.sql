-- SELECT-first, not insert-first: dbo.EmbeddingModel.EmbeddingModelId is a SMALLINT IDENTITY (at most
-- 32,767 values ever issued across this table's lifetime -- there are only a handful of distinct
-- (endpoint, deployment, dimensions) combinations a tenant will ever configure, so that ceiling is not
-- a practical concern for genuinely NEW keys). But every call for an EXISTING key -- which is the
-- overwhelming majority of calls, since a tenant's embedding model rarely changes -- resolves to the
-- same handful of rows forever. An insert-first design burns one IDENTITY value per call regardless of
-- outcome: the attempted INSERT consumes the next identity before the uniqueness violation is even
-- detected, and SQL Server never reclaims a consumed IDENTITY value on rollback or constraint failure.
-- Five lookups of the same existing ModelKey would silently burn five IDENTITY values for zero new
-- rows, and the one existing row stops being resolvable the moment the counter wraps past 32,767 --
-- the failure mode is error 8115 (arithmetic overflow) on a column nothing about the request implied
-- should ever overflow for a request that creates nothing.
--
-- SELECT-first makes a lookup of an existing key free of IDENTITY consumption: only a genuinely new
-- ModelKey reaches the INSERT branch below, so the counter advances once per distinct model this tenant
-- ever uses, not once per call.
--
-- Still race-safe without an explicit lock on the initial SELECT: a bare SELECT-then-INSERT takes no
-- lock on a key that does not yet exist (range locks only apply under SERIALIZABLE), so two concurrent
-- callers creating the same new ModelKey can both see "not found" and both attempt the INSERT. Rather
-- than preventing that race, this procedure lets it happen and resolves it afterward -- the loser's
-- INSERT fails on UQ_EmbeddingModel_ModelKey (error 2627) or the clustered PK (2601), which is caught,
-- and the loser then re-SELECTs the winner's row. Both callers return the same EmbeddingModelId either
-- way; only the race's loser pays the one-time IDENTITY cost of its failed INSERT attempt, not every
-- subsequent lookup of the same key.
CREATE PROCEDURE dbo.GetOrCreateEmbeddingModel
@ModelKey VARCHAR (256), @Dimensions SMALLINT, @EmbeddingModelId SMALLINT OUTPUT
AS
SET NOCOUNT ON;
DECLARE @ExistingDimensions AS SMALLINT;
SET @EmbeddingModelId = NULL;
SELECT @EmbeddingModelId = EmbeddingModelId,
       @ExistingDimensions = Dimensions
FROM   dbo.EmbeddingModel
WHERE  ModelKey = @ModelKey;
IF @EmbeddingModelId IS NOT NULL
    BEGIN
        IF @ExistingDimensions <> @Dimensions
            THROW 50409, 'EmbeddingModel exists with a different Dimensions value.', 1;
        RETURN;
    END
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
