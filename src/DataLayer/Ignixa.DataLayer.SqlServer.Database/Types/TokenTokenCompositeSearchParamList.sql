CREATE TYPE dbo.TokenTokenCompositeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    SystemId1           INT           NULL,
    Code1               VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL,
    SystemId2           INT           NULL,
    Code2               VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow2       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL);
