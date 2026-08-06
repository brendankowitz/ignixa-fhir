CREATE TABLE dbo.SourceEvents (
    EventId       BIGINT         NOT NULL IDENTITY (1, 1),
    StreamId      NVARCHAR (256) NOT NULL,
    EventType     NVARCHAR (100) NOT NULL,
    EventData     NVARCHAR (MAX) NOT NULL,
    Timestamp     DATETIMEOFFSET DEFAULT sysutcdatetime() NOT NULL,
    TransactionId BIGINT         DEFAULT 0 NOT NULL,
    CONSTRAINT PK_SourceEvents PRIMARY KEY (EventId)
);

GO

CREATE INDEX IX_SourceEvents_StreamId_EventId
    ON dbo.SourceEvents(StreamId, EventId);

GO

CREATE INDEX IX_SourceEvents_EventId
    ON dbo.SourceEvents(EventId);

GO

CREATE INDEX IX_SourceEvents_TransactionId
    ON dbo.SourceEvents(TransactionId);
