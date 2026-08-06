CREATE TABLE dbo.ParametersHistory (
    ChangeId    INT             IDENTITY (1, 1) NOT NULL,
    Id          VARCHAR (100)   NOT NULL,
    Date        DATETIME        NULL,
    Number      FLOAT           NULL,
    Bigint      BIGINT          NULL,
    Char        VARCHAR (4000)  NULL,
    Binary      VARBINARY (MAX) NULL,
    UpdatedDate DATETIME        NULL,
    UpdatedBy   NVARCHAR (255)  NULL
);
