-- Runs after dbo.MergeResources has already committed the write it is persisting vectors for -- never
-- inside the same transaction (Global Constraints: MergeResources, its TVPs and MergeResourcesAndSearchParams
-- stay untouched by this feature). A vector write that races a concurrent re-version of the same resource
-- must not corrupt either version's vectors, and must not require MergeResources to know this table exists:
--
--   1. Each evaluated resource's CURRENT row is locked with UPDLOCK, HOLDLOCK before anything else happens.
--      MergeResources.sql marks a resource's previous row IsHistory = 1 with a plain UPDATE (not a DELETE)
--      whenever KeepHistory = 1, and even its history-capture branch (KeepHistory = 0) still UPDATEs the
--      row rather than removing it outright before commit -- either way that UPDATE takes an X lock on
--      exactly the row this SELECT is locking. So this SELECT either blocks until that concurrent
--      MergeResources commits and then correctly observes IsHistory = 1 (excluding the row), or it wins the
--      lock first and the concurrent UPDATE waits behind it instead. There is no interleaving in which this
--      procedure reads a row as current that a concurrent writer is about to retire.
--   2. An evaluated surrogate that is NOT current by the time its lock is acquired (a newer version has
--      already been merged) is skipped entirely: no delete, no insert. The newer version's own post-merge
--      write is the one that owns that resource's vectors from then on; touching them here would destroy
--      work a transaction that has already committed is entitled to keep.
--   3. For resources that ARE still current, vectors are deleted for EVERY version of the resource (joined
--      by ResourceId, not just the evaluated surrogate) before the new set is inserted, because a resource
--      whose semantic text changed on update must not leave the previous version's chunks behind under a
--      different ResourceSurrogateId.
CREATE PROCEDURE dbo.MergeVectorSearchParams
@Evaluated dbo.VectorResourceList READONLY, @Vectors dbo.VectorSearchParamList READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @st AS DATETIME = getUTCdate(), @InitialTranCount AS INT = @@trancount, @CurrentRows AS INT, @DeletedRows AS INT, @InsertedRows AS INT, @Text AS VARCHAR (100);
DECLARE @Mode AS VARCHAR (200) = 'Evaluated=' + CONVERT (VARCHAR, (SELECT count(*)
                                                                   FROM   @Evaluated)) + ' Vectors=' + CONVERT (VARCHAR, (SELECT count(*)
                                                                                                                          FROM   @Vectors));
BEGIN TRY
    DECLARE @Current AS TABLE (
        ResourceTypeId      SMALLINT     NOT NULL,
        ResourceSurrogateId BIGINT       NOT NULL,
        ResourceId          VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceSurrogateId));
    IF @InitialTranCount = 0
        BEGIN TRANSACTION;
    INSERT INTO @Current (ResourceTypeId, ResourceSurrogateId, ResourceId)
    SELECT R.ResourceTypeId,
           R.ResourceSurrogateId,
           R.ResourceId
    FROM   @Evaluated AS E
           INNER JOIN
           dbo.Resource AS R WITH (UPDLOCK, HOLDLOCK)
           ON R.ResourceTypeId = E.ResourceTypeId
              AND R.ResourceSurrogateId = E.ResourceSurrogateId
    WHERE  R.IsHistory = 0
           AND R.IsDeleted = 0;
    SET @CurrentRows = @@rowcount;
    DELETE V
    FROM   dbo.VectorSearchParam AS V
           INNER JOIN
           dbo.Resource AS R
           ON R.ResourceTypeId = V.ResourceTypeId
              AND R.ResourceSurrogateId = V.ResourceSurrogateId
           INNER JOIN
           @Current AS C
           ON C.ResourceTypeId = R.ResourceTypeId
              AND C.ResourceId = R.ResourceId;
    SET @DeletedRows = @@rowcount;
    INSERT INTO dbo.VectorSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, EmbeddingModelId, ChunkOrdinal, SourceTextCompressed, SourceTextHash, Embedding)
    SELECT V.ResourceTypeId,
           V.ResourceSurrogateId,
           V.SearchParamId,
           V.EmbeddingModelId,
           V.ChunkOrdinal,
           V.SourceTextCompressed,
           V.SourceTextHash,
           CAST (V.Embedding AS VECTOR (1536))
    FROM   @Vectors AS V
           INNER JOIN
           @Current AS C
           ON C.ResourceTypeId = V.ResourceTypeId
              AND C.ResourceSurrogateId = V.ResourceSurrogateId;
    SET @InsertedRows = @@rowcount;
    IF @InitialTranCount = 0
        COMMIT TRANSACTION;
    SET @Text = CONVERT (VARCHAR, @CurrentRows) + '/' + CONVERT (VARCHAR, @DeletedRows);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @InsertedRows, @Target = 'Current/Deleted', @Text = @Text;
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0
       AND @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
