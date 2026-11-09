CREATE TABLE dbo.Transactions (
    SurrogateIdRangeFirstValue  BIGINT         NOT NULL,
    SurrogateIdRangeLastValue   BIGINT         NOT NULL,
    Definition                  VARCHAR (2000) NULL,
    IsCompleted                 BIT            CONSTRAINT DF_Transactions_IsCompleted DEFAULT 0 NOT NULL,
    IsSuccess                   BIT            CONSTRAINT DF_Transactions_IsSuccess DEFAULT 0 NOT NULL,
    IsVisible                   BIT            CONSTRAINT DF_Transactions_IsVisible DEFAULT 0 NOT NULL,
    IsHistoryMoved              BIT            CONSTRAINT DF_Transactions_IsHistoryMoved DEFAULT 0 NOT NULL,
    CreateDate                  DATETIME       CONSTRAINT DF_Transactions_CreateDate DEFAULT getUTCdate() NOT NULL,
    EndDate                     DATETIME       NULL,
    VisibleDate                 DATETIME       NULL,
    HistoryMovedDate            DATETIME       NULL,
    HeartbeatDate               DATETIME       CONSTRAINT DF_Transactions_HeartbeatDate DEFAULT getUTCdate() NOT NULL,
    FailureReason               VARCHAR (MAX)  NULL,
    IsControlledByClient        BIT            CONSTRAINT DF_Transactions_IsControlledByClient DEFAULT 1 NOT NULL,
    InvisibleHistoryRemovedDate DATETIME       NULL CONSTRAINT PKC_Transactions_SurrogateIdRangeFirstValue PRIMARY KEY CLUSTERED (SurrogateIdRangeFirstValue)
);

GO

CREATE INDEX IX_IsVisible
    ON dbo.Transactions(IsVisible);

GO

CREATE INDEX IX_Transactions_SurrogateIdRangeLastValue
    ON dbo.Transactions(SurrogateIdRangeLastValue DESC) WITH (ONLINE = ON);

GO

-- Only incomplete transactions, so the reindex drain's TOP (1) seeks a near-empty index instead of reading every
-- completed row below the cutoff: dbo.Transactions is never pruned and nothing is incomplete in the normal case.
-- Covering on purpose: without the INCLUDE the optimizer costs the key lookup above a clustered scan and never
-- uses the index.
CREATE INDEX IX_Transactions_SurrogateIdRangeFirstValue_Incomplete
    ON dbo.Transactions(SurrogateIdRangeFirstValue) INCLUDE (CreateDate, HeartbeatDate)
    WHERE IsCompleted = 0 WITH (ONLINE = ON);
