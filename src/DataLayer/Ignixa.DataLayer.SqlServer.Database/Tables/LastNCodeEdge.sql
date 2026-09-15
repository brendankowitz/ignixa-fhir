CREATE TABLE dbo.LastNCodeEdge (
    ResourceTypeId SMALLINT NOT NULL,
    SearchParamId SMALLINT NOT NULL,
    LeftCodeIdentityId BIGINT NOT NULL,
    RightCodeIdentityId BIGINT NOT NULL,
    SupportCount INT NOT NULL,
    CONSTRAINT PK_LastNCodeEdge PRIMARY KEY CLUSTERED
        (ResourceTypeId, SearchParamId, LeftCodeIdentityId, RightCodeIdentityId),
    CONSTRAINT CH_LastNCodeEdge_Order CHECK (LeftCodeIdentityId < RightCodeIdentityId),
    CONSTRAINT CH_LastNCodeEdge_Support CHECK (SupportCount > 0),
    CONSTRAINT FK_LastNCodeEdge_Left FOREIGN KEY
        (LeftCodeIdentityId, ResourceTypeId, SearchParamId)
        REFERENCES dbo.LastNCodeIdentity (CodeIdentityId, ResourceTypeId, SearchParamId),
    CONSTRAINT FK_LastNCodeEdge_Right FOREIGN KEY
        (RightCodeIdentityId, ResourceTypeId, SearchParamId)
        REFERENCES dbo.LastNCodeIdentity (CodeIdentityId, ResourceTypeId, SearchParamId)
);

GO

CREATE INDEX IX_LastNCodeEdge_Right
    ON dbo.LastNCodeEdge
        (ResourceTypeId, SearchParamId, RightCodeIdentityId, LeftCodeIdentityId);
