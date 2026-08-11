CREATE TYPE dbo.TokenTextList AS TABLE (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    Text                NVARCHAR (400) COLLATE Latin1_General_CI_AI NOT NULL);
