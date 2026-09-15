CREATE TABLE dbo.LastNCodeIdentity (
    CodeIdentityId BIGINT IDENTITY NOT NULL,
    ResourceTypeId SMALLINT NOT NULL,
    SearchParamId SMALLINT NOT NULL,
    SystemId INT NULL,
    Code VARCHAR(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow VARCHAR(MAX) COLLATE Latin1_General_100_CS_AS NULL,
    CodeHash BINARY(32) NOT NULL,
    ComponentCodeIdentityId BIGINT NOT NULL,
    CONSTRAINT PK_LastNCodeIdentity PRIMARY KEY CLUSTERED (CodeIdentityId)
);

GO

CREATE UNIQUE INDEX UX_LastNCodeIdentity_Id_Scope
    ON dbo.LastNCodeIdentity (CodeIdentityId, ResourceTypeId, SearchParamId);

GO

CREATE INDEX IX_LastNCodeIdentity_Lookup
    ON dbo.LastNCodeIdentity (ResourceTypeId, SearchParamId, CodeHash)
    INCLUDE (SystemId, Code, CodeOverflow);

GO

CREATE INDEX IX_LastNCodeIdentity_Component
    ON dbo.LastNCodeIdentity (ResourceTypeId, SearchParamId, ComponentCodeIdentityId, CodeIdentityId);
