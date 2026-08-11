CREATE TABLE dbo.SchemaVersion (
    Version   INT             NOT NULL,
    AppliedAt DATETIMEOFFSET  NOT NULL DEFAULT sysutcdatetime(),
    CONSTRAINT PK_SchemaVersion PRIMARY KEY (Version)
);
