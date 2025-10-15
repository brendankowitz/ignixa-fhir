-- Corrected FHIR schema matching microsoft/fhir-server current version (no IsHistory in search params)
USE Fhir_Tenant2;
GO

-- ResourceType table
CREATE TABLE dbo.ResourceType
(
    ResourceTypeId smallint NOT NULL IDENTITY(1,1),
    Name varchar(50) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PKC_ResourceType PRIMARY KEY CLUSTERED (Name),
    CONSTRAINT UQ_ResourceType_ResourceTypeId UNIQUE NONCLUSTERED (ResourceTypeId)
);
GO

-- Resource table
CREATE TABLE dbo.Resource
(
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version int NOT NULL,
    IsHistory bit NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    IsDeleted bit NOT NULL,
    RequestMethod varchar(10) NULL,
    RawResource varbinary(max) NOT NULL,
    IsRawResourceMetaSet bit NOT NULL DEFAULT 0,
    SearchParamHash varchar(64) NULL,
    TransactionId bigint NULL,
    HistoryTransactionId bigint NULL,
    CONSTRAINT PKC_Resource PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId),
    CONSTRAINT FK_Resource_ResourceType FOREIGN KEY (ResourceTypeId) REFERENCES dbo.ResourceType(ResourceTypeId),
    CONSTRAINT CH_Resource_RawResource_Length CHECK (RawResource > 0x0)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version
ON dbo.Resource (ResourceTypeId, ResourceId, Version);
GO

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId
ON dbo.Resource (ResourceTypeId, ResourceId)
INCLUDE (Version, IsDeleted)
WHERE IsHistory = 0;
GO

-- SearchParam table
CREATE TABLE dbo.SearchParam
(
    SearchParamId smallint NOT NULL IDENTITY(1,1),
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PK_SearchParam PRIMARY KEY (SearchParamId),
    CONSTRAINT UQ_SearchParam_Uri UNIQUE (Uri)
);
GO

-- System table
CREATE TABLE dbo.System
(
    SystemId int NOT NULL IDENTITY(1,1),
    Value varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PK_System PRIMARY KEY (SystemId),
    CONSTRAINT UQ_System_Value UNIQUE (Value)
);
GO

-- QuantityCode table
CREATE TABLE dbo.QuantityCode
(
    QuantityCodeId int NOT NULL IDENTITY(1,1),
    Value varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PK_QuantityCode PRIMARY KEY (QuantityCodeId),
    CONSTRAINT UQ_QuantityCode_Value UNIQUE (Value)
);
GO

-- TokenSearchParam table (NO IsHistory)
CREATE TABLE dbo.TokenSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    Code varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow varchar(max) COLLATE Latin1_General_100_CS_AS NULL,
    CONSTRAINT PK_TokenSearchParam PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId),
    CONSTRAINT FK_TokenSearchParam_Resource FOREIGN KEY (ResourceTypeId, ResourceSurrogateId) REFERENCES dbo.Resource(ResourceTypeId, ResourceSurrogateId),
    CONSTRAINT FK_TokenSearchParam_SearchParam FOREIGN KEY (SearchParamId) REFERENCES dbo.SearchParam(SearchParamId),
    CONSTRAINT FK_TokenSearchParam_System FOREIGN KEY (SystemId) REFERENCES dbo.System(SystemId)
);
GO

-- StringSearchParam table (NO IsHistory)
CREATE TABLE dbo.StringSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL,
    IsMin bit NOT NULL DEFAULT 0,
    IsMax bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_StringSearchParam PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId),
    CONSTRAINT FK_StringSearchParam_Resource FOREIGN KEY (ResourceTypeId, ResourceSurrogateId) REFERENCES dbo.Resource(ResourceTypeId, ResourceSurrogateId),
    CONSTRAINT FK_StringSearchParam_SearchParam FOREIGN KEY (SearchParamId) REFERENCES dbo.SearchParam(SearchParamId)
);
GO

-- NumberSearchParam table (NO IsHistory)
CREATE TABLE dbo.NumberSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SingleValue decimal(36,18) NULL,
    LowValue decimal(36,18) NOT NULL,
    HighValue decimal(36,18) NOT NULL,
    CONSTRAINT PK_NumberSearchParam PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId),
    CONSTRAINT FK_NumberSearchParam_Resource FOREIGN KEY (ResourceTypeId, ResourceSurrogateId) REFERENCES dbo.Resource(ResourceTypeId, ResourceSurrogateId),
    CONSTRAINT FK_NumberSearchParam_SearchParam FOREIGN KEY (SearchParamId) REFERENCES dbo.SearchParam(SearchParamId)
);
GO

-- DateTimeSearchParam table (NO IsHistory)
CREATE TABLE dbo.DateTimeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime datetime2(7) NOT NULL,
    EndDateTime datetime2(7) NOT NULL,
    IsLongerThanADay bit NOT NULL,
    IsMin bit NOT NULL DEFAULT 0,
    IsMax bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_DateTimeSearchParam PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId),
    CONSTRAINT FK_DateTimeSearchParam_Resource FOREIGN KEY (ResourceTypeId, ResourceSurrogateId) REFERENCES dbo.Resource(ResourceTypeId, ResourceSurrogateId),
    CONSTRAINT FK_DateTimeSearchParam_SearchParam FOREIGN KEY (SearchParamId) REFERENCES dbo.SearchParam(SearchParamId)
);
GO

-- QuantitySearchParam table (NO IsHistory)
CREATE TABLE dbo.QuantitySearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    QuantityCodeId int NULL,
    SingleValue decimal(36,18) NULL,
    LowValue decimal(36,18) NOT NULL,
    HighValue decimal(36,18) NOT NULL,
    CONSTRAINT PK_QuantitySearchParam PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId),
    CONSTRAINT FK_QuantitySearchParam_Resource FOREIGN KEY (ResourceTypeId, ResourceSurrogateId) REFERENCES dbo.Resource(ResourceTypeId, ResourceSurrogateId),
    CONSTRAINT FK_QuantitySearchParam_SearchParam FOREIGN KEY (SearchParamId) REFERENCES dbo.SearchParam(SearchParamId),
    CONSTRAINT FK_QuantitySearchParam_System FOREIGN KEY (SystemId) REFERENCES dbo.System(SystemId),
    CONSTRAINT FK_QuantitySearchParam_QuantityCode FOREIGN KEY (QuantityCodeId) REFERENCES dbo.QuantityCode(QuantityCodeId)
);
GO

-- ReferenceSearchParam table (NO IsHistory)
CREATE TABLE dbo.ReferenceSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId smallint NULL,
    ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion int NULL,
    CONSTRAINT PK_ReferenceSearchParam PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId, ReferenceResourceId),
    CONSTRAINT FK_ReferenceSearchParam_Resource FOREIGN KEY (ResourceTypeId, ResourceSurrogateId) REFERENCES dbo.Resource(ResourceTypeId, ResourceSurrogateId),
    CONSTRAINT FK_ReferenceSearchParam_SearchParam FOREIGN KEY (SearchParamId) REFERENCES dbo.SearchParam(SearchParamId)
);
GO

-- UriSearchParam table (NO IsHistory)
CREATE TABLE dbo.UriSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Uri varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PK_UriSearchParam PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId),
    CONSTRAINT FK_UriSearchParam_Resource FOREIGN KEY (ResourceTypeId, ResourceSurrogateId) REFERENCES dbo.Resource(ResourceTypeId, ResourceSurrogateId),
    CONSTRAINT FK_UriSearchParam_SearchParam FOREIGN KEY (SearchParamId) REFERENCES dbo.SearchParam(SearchParamId)
);
GO

-- ResourceSurrogateIdUniquifierSequence
CREATE SEQUENCE dbo.ResourceSurrogateIdUniquifierSequence
    AS INT
    START WITH 0
    INCREMENT BY 1
    MINVALUE 0
    MAXVALUE 79999
    CYCLE
    CACHE 1000000;
GO

-- Seed ResourceType table
INSERT INTO dbo.ResourceType (Name) VALUES
('Patient'), ('Observation'), ('Condition'), ('Procedure'), ('Medication'),
('MedicationRequest'), ('Encounter'), ('Practitioner'), ('Organization'), ('Device');
GO

PRINT 'Corrected FHIR schema created successfully (no IsHistory in search params)';
GO
