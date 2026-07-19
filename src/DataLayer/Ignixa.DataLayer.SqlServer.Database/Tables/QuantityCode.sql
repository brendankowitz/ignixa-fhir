CREATE TABLE dbo.QuantityCode (
    QuantityCodeId INT            IDENTITY (1, 1) NOT NULL,
    Value          NVARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT UQ_QuantityCode_QuantityCodeId UNIQUE (QuantityCodeId),
    CONSTRAINT PKC_QuantityCode PRIMARY KEY CLUSTERED (Value) WITH (DATA_COMPRESSION = PAGE)
);
