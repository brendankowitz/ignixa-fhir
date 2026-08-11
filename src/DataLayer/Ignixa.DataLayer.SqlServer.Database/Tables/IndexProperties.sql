CREATE TABLE dbo.IndexProperties (
    TableName     VARCHAR (100) NOT NULL,
    IndexName     VARCHAR (200) NOT NULL,
    PropertyName  VARCHAR (100) NOT NULL,
    PropertyValue VARCHAR (100) NOT NULL,
    CreateDate    DATETIME      CONSTRAINT DF_IndexProperties_CreateDate DEFAULT getUTCdate() NOT NULL CONSTRAINT PKC_IndexProperties_TableName_IndexName_PropertyName PRIMARY KEY CLUSTERED (TableName, IndexName, PropertyName)
);
