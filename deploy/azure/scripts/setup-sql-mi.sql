-- -------------------------------------------------------------------------------------------------
-- Copyright (c) Ignixa Contributors. All rights reserved.
-- Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
-- -------------------------------------------------------------------------------------------------
--
-- SQL Managed Identity Setup Script for FHIR Server
--
-- This script configures Azure SQL Database for Managed Identity (MI) authentication.
-- It creates a database user for the SQL user-assigned Managed Identity and grants runtime permissions.
--
-- Usage:
--   1. Connect to Azure SQL Server using SQL Server Management Studio or Azure Data Studio
--   2. Authenticate using Azure AD (admin account)
--   3. Replace every fhir-prod-yourorg-sql-mi placeholder with the SQL UAMI's display name.
--   4. Run this script against each target FhirTenantN database before starting the app.
--
-- Prerequisites:
--   - Azure SQL Server with Azure AD admin configured
--   - SQL UAMI attached to the App Service (and the schema deployment compute resource)
--   - Server-level Azure AD authentication enabled
--

-- ========================================
-- 1. Create database user for the SQL UAMI
-- ========================================
-- Use the UAMI resource's display name here, not its client ID or the App Service name.
-- The connection string uses User ID=<UAMI client ID> to select this identity.

CREATE USER [fhir-prod-yourorg-sql-mi] FROM EXTERNAL PROVIDER;

-- ========================================
-- 2. Grant database roles to the MI user
-- ========================================

-- Grant db_datareader role (allows SELECT on all tables/views)
ALTER ROLE db_datareader ADD MEMBER [fhir-prod-yourorg-sql-mi];

-- Grant db_datawriter role (allows INSERT, UPDATE, DELETE on all tables)
ALTER ROLE db_datawriter ADD MEMBER [fhir-prod-yourorg-sql-mi];

-- Schema deployment requires additional DDL permissions. Run the CLI as an authorized
-- deployment principal (the template's SQL-admin UAMI already has these), not a runtime-only user.
-- Do not leave broad deployment grants on a separate least-privilege runtime identity.

-- ========================================
-- 3. Grant specific object-level permissions
-- ========================================

-- Grant EXECUTE on all stored procedures (if applicable)
GRANT EXECUTE ON SCHEMA::dbo TO [fhir-prod-yourorg-sql-mi];

-- ========================================
-- 4. Verify permissions
-- ========================================

-- List all users in the database
-- SELECT * FROM sys.database_principals WHERE type IN ('E', 'X');

-- Verify role membership
-- SELECT USER_NAME() as CurrentUser;
-- SELECT DP1.name as DatabaseUser, DP2.name as RoleName
-- FROM sys.database_role_members as DRM
-- RIGHT OUTER JOIN sys.database_principals as DP1 on DRM.member_principal_id = DP1.principal_id
-- LEFT OUTER JOIN sys.database_principals as DP2 on DRM.role_principal_id = DP2.principal_id
-- WHERE DP1.name = 'fhir-prod-yourorg-sql-mi';

-- ========================================
-- 5. Notes for FHIR Server Operations
-- ========================================
-- - The MI user can now authenticate to SQL Server using Azure AD tokens (no password needed)
-- - The process must run on compute with this UAMI attached.
-- - Connection string: Server=tcp:servername.database.windows.net,1433;Database=FhirTenant1;User ID=<UAMI client ID>;Encrypt=true;TrustServerCertificate=false;Authentication=Active Directory Managed Identity;
-- - Microsoft.Data.SqlClient acquires the token. Neither the server nor CLI creates this SQL user.
