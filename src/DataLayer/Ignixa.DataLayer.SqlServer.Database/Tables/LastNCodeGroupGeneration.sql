CREATE TABLE dbo.LastNCodeGroupGeneration (
    ResourceTypeId SMALLINT NOT NULL,
    SearchParamId SMALLINT NOT NULL,
    Generation BIGINT NOT NULL,
    AttemptId UNIQUEIDENTIFIER NULL,
    State VARCHAR(16) NOT NULL,
    SnapshotHighWaterSurrogateId BIGINT NULL,
    StartedDateTime DATETIME2(7) NOT NULL,
    CompletedDateTime DATETIME2(7) NULL,
    FailureReason VARCHAR(1000) NULL,
    CONSTRAINT PK_LastNCodeGroupGeneration PRIMARY KEY CLUSTERED (ResourceTypeId, SearchParamId),
    CONSTRAINT CH_LastNCodeGroupGeneration_State
        CHECK (State IN ('Pending', 'Building', 'Ready', 'Failed'))
);
