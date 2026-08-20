CREATE TYPE dbo.SearchParamList AS TABLE (
    Uri                  VARCHAR (128)      COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status               VARCHAR (20)       NOT NULL,
    IsPartiallySupported BIT                NOT NULL,
    LastUpdated          DATETIMEOFFSET (7) NOT NULL UNIQUE (Uri));
