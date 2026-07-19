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
