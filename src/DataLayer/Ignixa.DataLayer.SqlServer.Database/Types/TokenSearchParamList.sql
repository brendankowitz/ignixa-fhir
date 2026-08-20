CREATE TYPE dbo.TokenSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    SystemId            INT           NULL,
    Code                VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow        VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL);
