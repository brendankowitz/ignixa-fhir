-- Value is CS_AS because FHIR compares system URIs as case-sensitive strings, and the clustered primary key
-- below is on Value: under the database's default (case-insensitive) collation `http://loinc.org` and
-- `http://LOINC.org` collapsed onto one SystemId, taking every concept, expansion entry and token
-- search-parameter row keyed on it with them. SqlServerSearchIndexReferenceDataCache's own in-memory map is
-- an ordinally-keyed ConcurrentDictionary, so the collation was the one half of that pairing that disagreed
-- -- a URI already cached under one casing resolved to a row stored under another. See TermConcept.sql for
-- the same reasoning applied to codes.
CREATE TABLE dbo.System (
    SystemId INT            IDENTITY (1, 1) NOT NULL,
    Value    NVARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT UQ_System_SystemId UNIQUE (SystemId),
    CONSTRAINT PKC_System PRIMARY KEY CLUSTERED (Value) WITH (DATA_COMPRESSION = PAGE)
);
