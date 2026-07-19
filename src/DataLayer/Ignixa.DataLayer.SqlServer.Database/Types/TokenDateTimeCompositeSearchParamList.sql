CREATE TYPE dbo.TokenDateTimeCompositeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT           NOT NULL,
    ResourceSurrogateId BIGINT             NOT NULL,
    SearchParamId       SMALLINT           NOT NULL,
    SystemId1           INT                NULL,
    Code1               VARCHAR (256)      COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX)      COLLATE Latin1_General_100_CS_AS NULL,
    StartDateTime2      DATETIMEOFFSET (7) NOT NULL,
    EndDateTime2        DATETIMEOFFSET (7) NOT NULL,
    IsLongerThanADay2   BIT                NOT NULL);
