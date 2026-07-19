CREATE TYPE dbo.TokenQuantityCompositeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId1           INT              NULL,
    Code1               VARCHAR (256)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX)    COLLATE Latin1_General_100_CS_AS NULL,
    SystemId2           INT              NULL,
    QuantityCodeId2     INT              NULL,
    SingleValue2        DECIMAL (36, 18) NULL,
    LowValue2           DECIMAL (36, 18) NULL,
    HighValue2          DECIMAL (36, 18) NULL);
