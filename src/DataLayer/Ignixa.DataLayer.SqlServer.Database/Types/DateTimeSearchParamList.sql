CREATE TYPE dbo.DateTimeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT           NOT NULL,
    ResourceSurrogateId BIGINT             NOT NULL,
    SearchParamId       SMALLINT           NOT NULL,
    StartDateTime       DATETIMEOFFSET (7) NOT NULL,
    EndDateTime         DATETIMEOFFSET (7) NOT NULL,
    IsLongerThanADay    BIT                NOT NULL,
    IsMin               BIT                NOT NULL,
    IsMax               BIT                NOT NULL UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax));
