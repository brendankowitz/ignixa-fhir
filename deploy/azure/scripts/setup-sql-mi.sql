-- -------------------------------------------------------------------------------------------------
-- Copyright (c) Ignixa Contributors. All rights reserved.
-- Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
-- -------------------------------------------------------------------------------------------------
--
-- SQL Managed Identity Setup Script for FHIR Server
--
-- This script configures Azure SQL Database for Managed Identity (MI) authentication.
-- It creates a database user for the App Service Managed Identity and grants appropriate permissions.
--
-- Usage:
--   1. Connect to Azure SQL Server using SQL Server Management Studio or Azure Data Studio
--   2. Authenticate using Azure AD (admin account)
--   3. Run this script against the FhirDatabase
--
-- Prerequisites:
--   - Azure SQL Server with Azure AD admin configured
--   - App Service created with System-Assigned Managed Identity
--   - Server-level Azure AD authentication enabled
--

-- ========================================
-- 1. Create database user for App Service MI
-- ========================================
-- The App Service Managed Identity (MI) is represented by the App Service resource name
-- Get the MI principal name from the App Service resource in Azure Portal

-- Example: Replace 'fhir-prod-yourorg' with your actual App Service name
CREATE USER [fhir-prod-yourorg] FROM EXTERNAL PROVIDER;

-- ========================================
-- 2. Grant database roles to the MI user
-- ========================================

-- Grant db_datareader role (allows SELECT on all tables/views)
ALTER ROLE db_datareader ADD MEMBER [fhir-prod-yourorg];

-- Grant db_datawriter role (allows INSERT, UPDATE, DELETE on all tables)
ALTER ROLE db_datawriter ADD MEMBER [fhir-prod-yourorg];

-- Grant db_ddladmin role (allows schema modifications for migrations)
-- Note: Be careful with this - consider restricting to admin users in production
-- ALTER ROLE db_ddladmin ADD MEMBER [fhir-prod-yourorg];

-- ========================================
-- 3. Grant specific object-level permissions
-- ========================================

-- Grant EXECUTE on all stored procedures (if applicable)
GRANT EXECUTE ON SCHEMA::dbo TO [fhir-prod-yourorg];

-- Grant CREATE TABLE (for initial schema setup, remove after migration complete)
GRANT CREATE TABLE TO [fhir-prod-yourorg];

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
-- WHERE DP1.name = 'fhir-prod-yourorg';

-- ========================================
-- 5. Notes for FHIR Server Operations
-- ========================================
-- - The MI user can now authenticate to SQL Server using Azure AD tokens (no password needed)
-- - The FHIR Server application must be deployed on the App Service to use this MI
-- - Connection string format: Server=tcp:servername.database.windows.net,1433;Database=FhirDatabase;Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;Authentication=Active Directory Managed Identity;
-- - In code, use DefaultAzureCredential or similar to obtain AD token automatically
