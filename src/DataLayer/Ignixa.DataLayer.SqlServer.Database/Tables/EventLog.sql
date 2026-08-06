CREATE TABLE dbo.EventLog (
    PartitionId  AS              isnull(CONVERT (TINYINT, EventId % 8), 0) PERSISTED,
    EventId      BIGINT          IDENTITY (1, 1) NOT NULL,
    EventDate    DATETIME        NOT NULL,
    Process      VARCHAR (100)   NOT NULL,
    Status       VARCHAR (10)    NOT NULL,
    Mode         VARCHAR (200)   NULL,
    Action       VARCHAR (20)    NULL,
    Target       VARCHAR (100)   NULL,
    Rows         BIGINT          NULL,
    Milliseconds INT             NULL,
    EventText    NVARCHAR (3500) NULL,
    SPID         SMALLINT        NOT NULL,
    HostName     VARCHAR (64)    NOT NULL CONSTRAINT PKC_EventLog_EventDate_EventId_PartitionId PRIMARY KEY CLUSTERED (EventDate, EventId, PartitionId) ON EventLogPartitionScheme (PartitionId)
);
