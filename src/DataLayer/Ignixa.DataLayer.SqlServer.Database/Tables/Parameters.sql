CREATE TABLE dbo.Parameters (
    Id          VARCHAR (100)   NOT NULL,
    Date        DATETIME        NULL,
    Number      FLOAT           NULL,
    Bigint      BIGINT          NULL,
    Char        VARCHAR (4000)  NULL,
    Binary      VARBINARY (MAX) NULL,
    UpdatedDate DATETIME        NULL,
    UpdatedBy   NVARCHAR (255)  NULL CONSTRAINT PKC_Parameters_Id PRIMARY KEY CLUSTERED (Id) WITH (IGNORE_DUP_KEY = ON)
);
