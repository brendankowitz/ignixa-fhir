-- -------------------------------------------------------------------------------------------------
-- Copyright (c) Ignixa Contributors. All rights reserved.
-- Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
-- -------------------------------------------------------------------------------------------------
--
-- Managed Identity Database User Setup (Auto-Embedded)
--
-- This script configures the database for Managed Identity (MI) authentication.
-- It runs automatically on first application startup if the MI user doesn't exist.
--
-- Prerequisites:
--   - Azure SQL Server with Azure AD admin configured
--   - App Service created with System-Assigned Managed Identity
--   - Server-level Azure AD authentication enabled
--   - Connection string must use: Authentication=Active Directory Managed Identity;
--

-- ========================================
-- 1. Create database user for App Service MI
-- ========================================
-- NOTE: The MI principal name should be passed as a parameter from the application
-- (typically the App Service resource name, e.g., 'fhir-prod-yourorg')

-- Check if user already exists (idempotent)
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE type = 'E' AND name = @ManagedIdentityName)
BEGIN
    CREATE USER [@ManagedIdentityName] FROM EXTERNAL PROVIDER;
    PRINT 'Created database user for Managed Identity: ' + @ManagedIdentityName;
END
ELSE
BEGIN
    PRINT 'Managed Identity user already exists: ' + @ManagedIdentityName;
END

-- ========================================
-- 2. Grant database roles to the MI user
-- ========================================

-- Grant db_datareader role (allows SELECT on all tables/views)
IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members drm
    INNER JOIN sys.database_principals dp ON drm.member_principal_id = dp.principal_id
    WHERE dp.name = @ManagedIdentityName AND drm.role_principal_id IN (
        SELECT principal_id FROM sys.database_principals WHERE name = 'db_datareader'
    )
)
BEGIN
    ALTER ROLE db_datareader ADD MEMBER [@ManagedIdentityName];
    PRINT 'Granted db_datareader role to: ' + @ManagedIdentityName;
END
ELSE
BEGIN
    PRINT 'db_datareader role already assigned to: ' + @ManagedIdentityName;
END

-- Grant db_datawriter role (allows INSERT, UPDATE, DELETE on all tables)
IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members drm
    INNER JOIN sys.database_principals dp ON drm.member_principal_id = dp.principal_id
    WHERE dp.name = @ManagedIdentityName AND drm.role_principal_id IN (
        SELECT principal_id FROM sys.database_principals WHERE name = 'db_datawriter'
    )
)
BEGIN
    ALTER ROLE db_datawriter ADD MEMBER [@ManagedIdentityName];
    PRINT 'Granted db_datawriter role to: ' + @ManagedIdentityName;
END
ELSE
BEGIN
    PRINT 'db_datawriter role already assigned to: ' + @ManagedIdentityName;
END

-- ========================================
-- 3. Grant specific object-level permissions
-- ========================================

-- Grant EXECUTE on all stored procedures and functions
IF NOT EXISTS (
    SELECT 1 FROM sys.database_permissions
    WHERE grantee_principal_id = (SELECT principal_id FROM sys.database_principals WHERE name = @ManagedIdentityName)
    AND permission_name = 'EXECUTE'
    AND class = 1
)
BEGIN
    GRANT EXECUTE ON SCHEMA::dbo TO [@ManagedIdentityName];
    PRINT 'Granted EXECUTE permission on dbo schema to: ' + @ManagedIdentityName;
END
ELSE
BEGIN
    PRINT 'EXECUTE permission already granted to: ' + @ManagedIdentityName;
END

-- Grant CREATE TABLE (for schema operations, can be removed after initial setup if desired)
IF NOT EXISTS (
    SELECT 1 FROM sys.database_permissions
    WHERE grantee_principal_id = (SELECT principal_id FROM sys.database_principals WHERE name = @ManagedIdentityName)
    AND permission_name = 'CREATE TABLE'
    AND class = 0
)
BEGIN
    GRANT CREATE TABLE TO [@ManagedIdentityName];
    PRINT 'Granted CREATE TABLE permission to: ' + @ManagedIdentityName;
END
ELSE
BEGIN
    PRINT 'CREATE TABLE permission already granted to: ' + @ManagedIdentityName;
END

-- ========================================
-- Summary
-- ========================================
PRINT '';
PRINT '========================================';
PRINT 'Managed Identity Setup Complete';
PRINT '========================================';
PRINT 'User: ' + @ManagedIdentityName;
PRINT 'Roles: db_datareader, db_datawriter';
PRINT 'Permissions: EXECUTE (schema), CREATE TABLE';
PRINT '';
