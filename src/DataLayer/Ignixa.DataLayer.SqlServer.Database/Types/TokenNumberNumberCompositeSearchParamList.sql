CREATE TYPE dbo.TokenNumberNumberCompositeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId1           INT              NULL,
    Code1               VARCHAR (256)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX)    COLLATE Latin1_General_100_CS_AS NULL,
    SingleValue2        DECIMAL (36, 18) NULL,
    LowValue2           DECIMAL (36, 18) NULL,
    HighValue2          DECIMAL (36, 18) NULL,
    SingleValue3        DECIMAL (36, 18) NULL,
    LowValue3           DECIMAL (36, 18) NULL,
    HighValue3          DECIMAL (36, 18) NULL,
    HasRange            BIT              NOT NULL);
