CREATE TABLE dbo.JobQueue (
    QueueType       TINYINT        NOT NULL,
    GroupId         BIGINT         NOT NULL,
    JobId           BIGINT         NOT NULL,
    PartitionId     AS             CONVERT (TINYINT, JobId % 16) PERSISTED,
    Definition      VARCHAR (MAX)  NOT NULL,
    DefinitionHash  VARBINARY (20) NOT NULL,
    Version         BIGINT         CONSTRAINT DF_JobQueue_Version DEFAULT datediff_big(millisecond, '0001-01-01', getUTCdate()) NOT NULL,
    Status          TINYINT        CONSTRAINT DF_JobQueue_Status DEFAULT 0 NOT NULL,
    Priority        TINYINT        CONSTRAINT DF_JobQueue_Priority DEFAULT 100 NOT NULL,
    Data            BIGINT         NULL,
    Result          VARCHAR (MAX)  NULL,
    CreateDate      DATETIME       CONSTRAINT DF_JobQueue_CreateDate DEFAULT getUTCdate() NOT NULL,
    StartDate       DATETIME       NULL,
    EndDate         DATETIME       NULL,
    HeartbeatDate   DATETIME       CONSTRAINT DF_JobQueue_HeartbeatDate DEFAULT getUTCdate() NOT NULL,
    Worker          VARCHAR (100)  NULL,
    Info            VARCHAR (1000) NULL,
    CancelRequested BIT            CONSTRAINT DF_JobQueue_CancelRequested DEFAULT 0 NOT NULL CONSTRAINT PKC_JobQueue_QueueType_PartitionId_JobId PRIMARY KEY CLUSTERED (QueueType, PartitionId, JobId) ON TinyintPartitionScheme (QueueType),
    CONSTRAINT U_JobQueue_QueueType_JobId UNIQUE (QueueType, JobId)
);

GO

CREATE INDEX IX_QueueType_PartitionId_Status_Priority
    ON dbo.JobQueue(PartitionId, Status, Priority)
    ON TinyintPartitionScheme (QueueType);

GO

CREATE INDEX IX_QueueType_GroupId
    ON dbo.JobQueue(QueueType, GroupId)
    ON TinyintPartitionScheme (QueueType);

GO

CREATE INDEX IX_QueueType_DefinitionHash
    ON dbo.JobQueue(QueueType, DefinitionHash)
    ON TinyintPartitionScheme (QueueType);
