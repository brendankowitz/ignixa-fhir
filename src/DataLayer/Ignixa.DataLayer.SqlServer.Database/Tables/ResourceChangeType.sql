CREATE TABLE dbo.ResourceChangeType (
    ResourceChangeTypeId TINYINT       NOT NULL,
    Name                 NVARCHAR (50) NOT NULL,
    CONSTRAINT PK_ResourceChangeType PRIMARY KEY CLUSTERED (ResourceChangeTypeId),
    CONSTRAINT UQ_ResourceChangeType_Name UNIQUE NONCLUSTERED (Name)
) ON [PRIMARY];
