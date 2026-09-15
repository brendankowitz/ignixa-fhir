CREATE TABLE dbo.LastNObservationCodeMembership (
    ResourceTypeId SMALLINT NOT NULL,
    SearchParamId SMALLINT NOT NULL,
    ResourceSurrogateId BIGINT NOT NULL,
    CodeIdentityId BIGINT NOT NULL,
    CONSTRAINT PK_LastNObservationCodeMembership PRIMARY KEY CLUSTERED
        (ResourceTypeId, SearchParamId, ResourceSurrogateId, CodeIdentityId),
    CONSTRAINT FK_LastNObservationCodeMembership_Identity FOREIGN KEY
        (CodeIdentityId, ResourceTypeId, SearchParamId)
        REFERENCES dbo.LastNCodeIdentity (CodeIdentityId, ResourceTypeId, SearchParamId)
);

GO

CREATE INDEX IX_LastNObservationCodeMembership_Code
    ON dbo.LastNObservationCodeMembership
        (ResourceTypeId, SearchParamId, CodeIdentityId, ResourceSurrogateId);
