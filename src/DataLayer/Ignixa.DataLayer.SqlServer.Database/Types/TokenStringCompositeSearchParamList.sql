CREATE TYPE dbo.TokenStringCompositeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    SystemId1           INT            NULL,
    Code1               VARCHAR (256)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX)  COLLATE Latin1_General_100_CS_AS NULL,
    Text2               NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow2       NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL);
