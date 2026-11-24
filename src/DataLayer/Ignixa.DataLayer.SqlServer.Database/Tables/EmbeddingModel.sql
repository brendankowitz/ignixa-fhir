CREATE TABLE dbo.EmbeddingModel (
    EmbeddingModelId SMALLINT      IDENTITY (1, 1) NOT NULL,
    ModelKey         VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Dimensions       SMALLINT      NOT NULL,
    CONSTRAINT PKC_EmbeddingModel PRIMARY KEY CLUSTERED (EmbeddingModelId) WITH (DATA_COMPRESSION = PAGE),
    CONSTRAINT UQ_EmbeddingModel_ModelKey UNIQUE (ModelKey)
);

GO

ALTER TABLE dbo.EmbeddingModel SET (LOCK_ESCALATION = AUTO);
