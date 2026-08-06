CREATE TABLE dbo.SchemaMigrationProgress (
    Timestamp DATETIME2 (3)  DEFAULT CURRENT_TIMESTAMP,
    Message   NVARCHAR (MAX)
);
