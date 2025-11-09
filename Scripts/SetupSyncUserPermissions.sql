-- =====================================================================================
-- Dotmim.Sync - Database User Permissions Setup Script
-- =====================================================================================
-- This script creates a dedicated sync user with appropriate permissions for Dotmim.Sync
--
-- USAGE:
-- 1. Customize the variables in the CONFIGURATION section
-- 2. Choose SCENARIO A (pre-provisioned) or SCENARIO B (runtime provisioning)
-- 3. Execute the relevant sections
--
-- For detailed information, see:
-- - PREPROVISIONED_SCHEMA_USAGE.md
-- - docs/Provision.rst
-- =====================================================================================

-- =====================================================================================
-- CONFIGURATION - Customize these variables
-- =====================================================================================

DECLARE @DatabaseName NVARCHAR(128) = 'YourDatabase';          -- Database name
DECLARE @LoginName NVARCHAR(128) = 'SyncAppLogin';             -- SQL Server login name
DECLARE @UserName NVARCHAR(128) = 'SyncUser';                  -- Database user name
DECLARE @Password NVARCHAR(128) = 'YourSecurePassword123!';    -- Login password (change this!)
DECLARE @SyncSchema NVARCHAR(128) = 'Report';                  -- Schema for sync objects
DECLARE @ScopeSchema NVARCHAR(128) = 'dbo';                    -- Schema for scope_info tables (usually 'dbo')

-- =====================================================================================
-- SCENARIO A: PRE-PROVISIONED SCHEMAS (DisableProvisioning = true)
-- =====================================================================================
-- Use this when:
-- - Database objects are created via migration tools (Flyway, Liquibase, EF Migrations)
-- - Database user has restricted permissions (no CREATE rights)
-- - Production environments with strict security policies
-- - Schema changes must be pre-approved
--
-- Permissions granted:
-- ✓ SELECT, INSERT, UPDATE, DELETE (data manipulation)
-- ✓ EXECUTE (run stored procedures)
-- ✓ ALTER (update scope metadata)
-- ✗ NO CREATE permissions (objects must exist before sync)
-- =====================================================================================

-- Step 1: Create Login (Server-level)
USE [master];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'SyncAppLogin')
BEGIN
    CREATE LOGIN [SyncAppLogin] WITH PASSWORD = 'YourSecurePassword123!';
    PRINT '✓ Login [SyncAppLogin] created';
END
ELSE
    PRINT '⚠ Login [SyncAppLogin] already exists';
GO

-- Step 2: Create User in Database
USE [YourDatabase];  -- CHANGE THIS to your database name
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'SyncUser')
BEGIN
    CREATE USER [SyncUser] FOR LOGIN [SyncAppLogin];
    PRINT '✓ User [SyncUser] created';
END
ELSE
    PRINT '⚠ User [SyncUser] already exists';
GO

-- Step 3: Set Default Schema (optional but recommended)
ALTER USER [SyncUser] WITH DEFAULT_SCHEMA = [Report];  -- CHANGE THIS to your sync schema
PRINT '✓ Default schema set to [Report]';
GO

-- Step 4: Grant Minimal Permissions for Pre-Provisioned Scenarios

-- Grant data manipulation permissions on sync schema
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[Report] TO [SyncUser];  -- CHANGE schema name
PRINT '✓ Granted SELECT, INSERT, UPDATE, DELETE on schema [Report]';

-- Grant EXECUTE for stored procedures
GRANT EXECUTE ON SCHEMA::[Report] TO [SyncUser];  -- CHANGE schema name
PRINT '✓ Granted EXECUTE on schema [Report]';

-- Grant ALTER for scope metadata updates
GRANT ALTER ON SCHEMA::[Report] TO [SyncUser];  -- CHANGE schema name
PRINT '✓ Granted ALTER on schema [Report]';

-- Step 5: Grant Permissions on Scope Info Tables (if in different schema)
-- If scope_info tables are in 'dbo' schema, grant permissions on them
IF EXISTS (SELECT 1 FROM sys.objects WHERE name = 'scope_info' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    GRANT SELECT, INSERT, UPDATE, DELETE, ALTER ON OBJECT::dbo.scope_info TO [SyncUser];
    PRINT '✓ Granted permissions on dbo.scope_info';
END

IF EXISTS (SELECT 1 FROM sys.objects WHERE name = 'scope_info_client' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    GRANT SELECT, INSERT, UPDATE, DELETE, ALTER ON OBJECT::dbo.scope_info_client TO [SyncUser];
    PRINT '✓ Granted permissions on dbo.scope_info_client';
END

PRINT '';
PRINT '========================================================================';
PRINT 'SCENARIO A: Pre-Provisioned Permissions Setup Complete!';
PRINT '========================================================================';
PRINT 'User [SyncUser] can now sync with DisableProvisioning = true';
PRINT '';
PRINT 'Required objects must be created before sync:';
PRINT '  - Tracking tables (e.g., Report.Product_tracking)';
PRINT '  - Triggers (INSERT, UPDATE, DELETE on base tables)';
PRINT '  - Stored procedures (SelectChanges, UpdateRow, DeleteRow, etc.)';
PRINT '  - Scope info tables (scope_info, scope_info_client)';
PRINT '';
PRINT 'Use GetProvisioningScriptsAsync() to generate migration scripts.';
PRINT '========================================================================';
GO

-- =====================================================================================
-- SCENARIO B: RUNTIME PROVISIONING (DisableProvisioning = false - DEFAULT)
-- =====================================================================================
-- Use this when:
-- - Development/testing environments
-- - Database user has full permissions
-- - Dotmim.Sync creates objects automatically during first sync
--
-- Permissions granted:
-- ✓ SELECT, INSERT, UPDATE, DELETE (data manipulation)
-- ✓ EXECUTE (run stored procedures)
-- ✓ ALTER (modify objects)
-- ✓ CREATE TABLE (create tracking tables)
-- ✓ CREATE PROCEDURE (create sync stored procedures)
-- ✓ Additional permissions for triggers and views
-- =====================================================================================

/*
-- UNCOMMENT THIS SECTION IF YOU NEED RUNTIME PROVISIONING

USE [YourDatabase];  -- CHANGE THIS to your database name
GO

-- Grant data manipulation permissions
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[Report] TO [SyncUser];  -- CHANGE schema name
PRINT '✓ Granted SELECT, INSERT, UPDATE, DELETE on schema [Report]';

-- Grant EXECUTE for stored procedures
GRANT EXECUTE ON SCHEMA::[Report] TO [SyncUser];  -- CHANGE schema name
PRINT '✓ Granted EXECUTE on schema [Report]';

-- Grant ALTER for object modifications
GRANT ALTER ON SCHEMA::[Report] TO [SyncUser];  -- CHANGE schema name
PRINT '✓ Granted ALTER on schema [Report]';

-- Grant CREATE permissions (DATABASE-LEVEL)
GRANT CREATE TABLE TO [SyncUser];
PRINT '✓ Granted CREATE TABLE (database-level)';

GRANT CREATE PROCEDURE TO [SyncUser];
PRINT '✓ Granted CREATE PROCEDURE (database-level)';

-- For SQL Server with triggers (not needed if using Change Tracking)
GRANT ALTER ANY DATABASE DDL TRIGGER TO [SyncUser];
PRINT '✓ Granted ALTER ANY DATABASE DDL TRIGGER';

-- Optional: Grant VIEW DEFINITION to inspect objects
GRANT VIEW DEFINITION ON SCHEMA::[Report] TO [SyncUser];  -- CHANGE schema name
PRINT '✓ Granted VIEW DEFINITION on schema [Report]';

-- Optional: If using Change Tracking instead of triggers
-- GRANT VIEW CHANGE TRACKING ON SCHEMA::[Report] TO [SyncUser];  -- CHANGE schema name
-- PRINT '✓ Granted VIEW CHANGE TRACKING on schema [Report]';

PRINT '';
PRINT '========================================================================';
PRINT 'SCENARIO B: Runtime Provisioning Permissions Setup Complete!';
PRINT '========================================================================';
PRINT 'User [SyncUser] can now sync with default settings (DisableProvisioning = false)';
PRINT 'Dotmim.Sync will automatically create all required objects during first sync.';
PRINT '========================================================================';
GO
*/

-- =====================================================================================
-- VERIFICATION - Check Granted Permissions
-- =====================================================================================

PRINT '';
PRINT '========================================================================';
PRINT 'PERMISSION VERIFICATION';
PRINT '========================================================================';

-- Show all permissions for the sync user
SELECT
    dp.name AS UserName,
    dp.type_desc AS UserType,
    dp.default_schema_name AS DefaultSchema,
    perm.class_desc AS PermissionClass,
    perm.permission_name AS Permission,
    perm.state_desc AS PermissionState,
    CASE
        WHEN perm.class_desc = 'SCHEMA' THEN SCHEMA_NAME(perm.major_id)
        WHEN perm.class_desc = 'OBJECT_OR_COLUMN' THEN OBJECT_SCHEMA_NAME(perm.major_id) + '.' + OBJECT_NAME(perm.major_id)
        WHEN perm.class_desc = 'DATABASE' THEN 'DATABASE LEVEL'
        ELSE 'N/A'
    END AS AppliesTo
FROM sys.database_principals dp
LEFT JOIN sys.database_permissions perm ON dp.principal_id = perm.grantee_principal_id
WHERE dp.name = 'SyncUser'
ORDER BY perm.class_desc, AppliesTo, perm.permission_name;

PRINT '========================================================================';
GO

-- =====================================================================================
-- PERMISSION COMPARISON TABLE
-- =====================================================================================

/*
+------------------------+-------------------------+-------------------------+
| Permission             | Pre-Provisioned (A)     | Runtime Provisioning (B)|
+------------------------+-------------------------+-------------------------+
| SELECT                 | ✓ Required              | ✓ Required              |
| INSERT                 | ✓ Required              | ✓ Required              |
| UPDATE                 | ✓ Required              | ✓ Required              |
| DELETE                 | ✓ Required              | ✓ Required              |
| EXECUTE                | ✓ Required              | ✓ Required              |
| ALTER                  | ✓ Required              | ✓ Required              |
| CREATE TABLE           | ✗ NOT NEEDED            | ✓ Required              |
| CREATE PROCEDURE       | ✗ NOT NEEDED            | ✓ Required              |
| ALTER DDL TRIGGER      | ✗ NOT NEEDED            | ✓ Required (triggers)   |
| VIEW CHANGE TRACKING   | ✗ Optional              | ✗ Optional              |
+------------------------+-------------------------+-------------------------+

SCHEMA-LEVEL vs DATABASE-LEVEL Permissions:

Schema-Level (applies to specific schema, e.g., "Report"):
  - GRANT SELECT ON SCHEMA::[Report] TO [SyncUser]
  - More restrictive, follows principle of least privilege
  - Recommended for production

Database-Level (applies to entire database):
  - GRANT CREATE TABLE TO [SyncUser]
  - Less restrictive, easier to manage
  - Common in development environments

*/

-- =====================================================================================
-- TESTING PERMISSIONS
-- =====================================================================================

/*
-- Test permissions by executing as the sync user:

-- 1. Test data manipulation (should work in both scenarios)
EXECUTE AS USER = 'SyncUser';
SELECT * FROM Report.Product WHERE 1=0;  -- Change table name
REVERT;
PRINT '✓ SELECT permission test passed';

-- 2. Test stored procedure execution (should work in both scenarios)
EXECUTE AS USER = 'SyncUser';
-- EXEC Report.Product_selectchanges @sync_min_timestamp = 0;  -- Uncomment if objects exist
REVERT;
PRINT '✓ EXECUTE permission test passed';

-- 3. Test CREATE TABLE (should fail in Scenario A, work in Scenario B)
EXECUTE AS USER = 'SyncUser';
BEGIN TRY
    CREATE TABLE Report.TestTable (Id INT);
    DROP TABLE Report.TestTable;
    PRINT '✓ CREATE TABLE permission exists (Runtime Provisioning)';
END TRY
BEGIN CATCH
    PRINT '✗ CREATE TABLE permission denied (Pre-Provisioned - Expected)';
END CATCH
REVERT;
*/

-- =====================================================================================
-- CLEANUP (OPTIONAL - Use with caution!)
-- =====================================================================================

/*
-- UNCOMMENT TO REMOVE SYNC USER AND LOGIN

USE [YourDatabase];
DROP USER IF EXISTS [SyncUser];
PRINT 'User [SyncUser] dropped';

USE [master];
DROP LOGIN IF EXISTS [SyncAppLogin];
PRINT 'Login [SyncAppLogin] dropped';
*/

-- =====================================================================================
-- ADDITIONAL NOTES
-- =====================================================================================

/*
SECURITY BEST PRACTICES:

1. Use Strong Passwords:
   - Minimum 12 characters
   - Include uppercase, lowercase, numbers, and special characters
   - Consider using Azure Key Vault or similar for password management

2. Principle of Least Privilege:
   - Use SCENARIO A (pre-provisioned) in production
   - Only grant permissions on specific schemas
   - Avoid database owner (db_owner) role

3. Connection Strings:
   - Store connection strings securely (Azure Key Vault, AWS Secrets Manager)
   - Use connection string encryption
   - Never commit credentials to source control

4. Monitoring:
   - Enable auditing for sync user activities
   - Monitor for unusual query patterns
   - Set up alerts for failed login attempts

5. Schema Isolation:
   - Keep sync objects in dedicated schema (e.g., "Report")
   - Separate from application tables when possible
   - Makes permission management clearer

COMMON PERMISSION ISSUES:

1. "CREATE TABLE permission denied"
   → Using runtime provisioning but missing CREATE TABLE permission
   → Solution: Either grant CREATE TABLE or switch to pre-provisioned schemas

2. "Cannot find stored procedure [SchemaName].[TableName]_selectchanges"
   → Using pre-provisioned mode but objects don't exist
   → Solution: Apply provisioning scripts first

3. "Changes not syncing"
   → Missing triggers (silent failure)
   → Solution: Validate all triggers exist using ValidatePreProvisionedObjects.sql

4. "Access denied on scope_info table"
   → scope_info tables in different schema than sync schema
   → Solution: Grant explicit permissions on scope_info tables

FOR MORE INFORMATION:
- PREPROVISIONED_SCHEMA_USAGE.md - Complete guide for pre-provisioned schemas
- SCRIPT_GENERATION_USAGE.md - How to generate SQL provisioning scripts
- Scripts/ValidatePreProvisionedObjects.sql - Validate deployed objects
- docs/Provision.rst - Official documentation
*/
