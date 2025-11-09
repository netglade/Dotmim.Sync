# Database Permissions Guide for Dotmim.Sync

This guide explains the minimum database permissions required for Dotmim.Sync in different scenarios.

## Table of Contents

- [Quick Reference](#quick-reference)
- [Scenario A: Pre-Provisioned Schemas (Recommended for Production)](#scenario-a-pre-provisioned-schemas-recommended-for-production)
- [Scenario B: Runtime Provisioning (Development/Testing)](#scenario-b-runtime-provisioning-developmenttesting)
- [Permission Comparison](#permission-comparison)
- [Setup Instructions](#setup-instructions)
- [Testing Permissions](#testing-permissions)
- [Common Issues](#common-issues)
- [Security Best Practices](#security-best-practices)

---

## Quick Reference

| Scenario | Use When | Required Permissions |
|----------|----------|---------------------|
| **Pre-Provisioned** | Production, restricted permissions, migration pipelines | SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER |
| **Runtime Provisioning** | Development, full permissions available | + CREATE TABLE, CREATE PROCEDURE, ALTER DDL TRIGGER |

---

## Scenario A: Pre-Provisioned Schemas (Recommended for Production)

### When to Use

Use pre-provisioned schemas when:
- ✅ Database users have **limited permissions** (no CREATE rights)
- ✅ Database users are **restricted to specific schemas** (e.g., "Report" schema only)
- ✅ All schema changes must go through **controlled migration pipelines**
- ✅ **Security policies** require pre-approved database changes
- ✅ **Compliance requirements** need audit trails for schema modifications
- ✅ **Production environments** with strict access control

### Required Permissions (Minimal)

```sql
-- Data manipulation
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[YourSchema] TO [SyncUser];

-- Execute stored procedures
GRANT EXECUTE ON SCHEMA::[YourSchema] TO [SyncUser];

-- Update scope metadata
GRANT ALTER ON SCHEMA::[YourSchema] TO [SyncUser];

-- If scope_info tables are in different schema (e.g., dbo)
GRANT SELECT, INSERT, UPDATE, DELETE, ALTER ON OBJECT::dbo.scope_info TO [SyncUser];
GRANT SELECT, INSERT, UPDATE, DELETE, ALTER ON OBJECT::dbo.scope_info_client TO [SyncUser];
```

### Why Each Permission is Needed

| Permission | Purpose | Impact if Missing |
|------------|---------|-------------------|
| **SELECT** | Read data from base tables and tracking tables | Cannot retrieve changes, sync fails immediately |
| **INSERT** | Insert new rows during sync | Cannot apply downloaded changes |
| **UPDATE** | Update existing rows during sync | Cannot apply modifications |
| **DELETE** | Delete rows during sync | Cannot apply deletions |
| **EXECUTE** | Run sync stored procedures (SelectChanges, UpdateRow, etc.) | Sync fails with "permission denied" error |
| **ALTER** | Update scope_info metadata (timestamps, versions) | Cannot track sync state, every sync becomes full sync |

### What is NOT Needed

- ❌ CREATE TABLE - Objects created via migrations
- ❌ CREATE PROCEDURE - Objects created via migrations
- ❌ CREATE TRIGGER - Objects created via migrations
- ❌ Database owner (db_owner) role
- ❌ ALTER ANY DDL TRIGGER

### Code Configuration

```csharp
// Enable DisableProvisioning to skip object creation
var options = new SyncOptions
{
    DisableProvisioning = true  // Skip all CREATE operations
};

var serverProvider = new SqlSyncProvider(serverConnectionString);
var clientProvider = new SqlSyncProvider(clientConnectionString);
var agent = new SyncAgent(clientProvider, serverProvider, options);

var setup = new SyncSetup("Product", "Customer");
var result = await agent.SynchronizeAsync(setup);
```

### Required Objects (Must Exist Before Sync)

All objects must be created via migration scripts before running sync:

1. **Tracking Tables** (e.g., `Report.Product_tracking`)
   - Stores change metadata for each row
   - Same schema as base table

2. **Triggers** (INSERT, UPDATE, DELETE on each base table)
   - `Report.Product_insert_trigger`
   - `Report.Product_update_trigger`
   - `Report.Product_delete_trigger`
   - ⚠️ **CRITICAL**: Missing triggers cause **silent data loss**

3. **Stored Procedures** (for each table)
   - `Report.Product_selectchanges` - Retrieve changed rows
   - `Report.Product_selectrow` - Get specific row
   - `Report.Product_updaterow` - Apply updates
   - `Report.Product_deleterow` - Apply deletes
   - `Report.Product_deletemetadata` - Clean up tracking
   - Additional procedures for bulk operations

4. **Scope Info Tables**
   - `scope_info` - Server-side sync metadata
   - `scope_info_client` - Client-side sync metadata

### Generating Migration Scripts

Use `GetProvisioningScriptsAsync()` to generate SQL scripts:

```csharp
var remoteOrchestrator = new RemoteOrchestrator(serverProvider);
var setup = new SyncSetup("Product", "Customer");

// Generate scripts
var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
    setup: setup,
    provision: SyncProvision.TrackingTable |
               SyncProvision.Triggers |
               SyncProvision.StoredProcedures
);

// Save for deployment via Flyway, Liquibase, EF Migrations, etc.
await File.WriteAllTextAsync("provisioning_scripts.sql", scripts);
```

---

## Scenario B: Runtime Provisioning (Development/Testing)

### When to Use

Use runtime provisioning when:
- ✅ **Development or testing** environments
- ✅ Database user has **full permissions**
- ✅ **Rapid iteration** needed without manual migrations
- ✅ **Proof of concept** or prototype development
- ✅ Local developer databases with unrestricted access

### Required Permissions (Full)

```sql
-- Data manipulation (same as pre-provisioned)
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[YourSchema] TO [SyncUser];
GRANT EXECUTE ON SCHEMA::[YourSchema] TO [SyncUser];
GRANT ALTER ON SCHEMA::[YourSchema] TO [SyncUser];

-- Object creation (DATABASE-LEVEL permissions)
GRANT CREATE TABLE TO [SyncUser];
GRANT CREATE PROCEDURE TO [SyncUser];

-- For triggers (SQL Server)
GRANT ALTER ANY DATABASE DDL TRIGGER TO [SyncUser];

-- Optional: View object definitions
GRANT VIEW DEFINITION ON SCHEMA::[YourSchema] TO [SyncUser];

-- Optional: For Change Tracking instead of triggers
GRANT VIEW CHANGE TRACKING ON SCHEMA::[YourSchema] TO [SyncUser];
```

### Why Additional Permissions are Needed

| Permission | Purpose | When Used |
|------------|---------|-----------|
| **CREATE TABLE** | Create tracking tables automatically | First sync per table |
| **CREATE PROCEDURE** | Create sync stored procedures automatically | First sync per table |
| **ALTER DDL TRIGGER** | Create change tracking triggers | First sync (if not using Change Tracking) |
| **VIEW CHANGE TRACKING** | Use SQL Server Change Tracking feature | If using SqlSyncChangeTrackingProvider |

### Code Configuration

```csharp
// Default configuration (DisableProvisioning = false)
var serverProvider = new SqlSyncProvider(serverConnectionString);
var clientProvider = new SqlSyncProvider(clientConnectionString);
var agent = new SyncAgent(clientProvider, serverProvider);

var setup = new SyncSetup("Product", "Customer");

// First sync will automatically create all required objects
var result = await agent.SynchronizeAsync(setup);
```

### What Happens During First Sync

1. **Server Side** (RemoteOrchestrator):
   - Reads schema from existing tables
   - Creates tracking tables (`Product_tracking`)
   - Creates triggers (INSERT, UPDATE, DELETE)
   - Creates stored procedures for sync operations

2. **Client Side** (LocalOrchestrator):
   - Creates base tables if they don't exist
   - Creates tracking tables
   - Creates triggers
   - Creates stored procedures
   - Creates scope_info tables

---

## Permission Comparison

### Complete Permission Matrix

| Permission Type | Pre-Provisioned | Runtime | Scope | Required For |
|----------------|-----------------|---------|-------|--------------|
| **SELECT** | ✅ Required | ✅ Required | Schema | Reading data |
| **INSERT** | ✅ Required | ✅ Required | Schema | Applying changes |
| **UPDATE** | ✅ Required | ✅ Required | Schema | Applying changes |
| **DELETE** | ✅ Required | ✅ Required | Schema | Applying changes |
| **EXECUTE** | ✅ Required | ✅ Required | Schema | Running stored procedures |
| **ALTER** | ✅ Required | ✅ Required | Schema | Updating scope metadata |
| **CREATE TABLE** | ❌ Not Needed | ✅ Required | Database | Creating tracking tables |
| **CREATE PROCEDURE** | ❌ Not Needed | ✅ Required | Database | Creating sync procedures |
| **ALTER DDL TRIGGER** | ❌ Not Needed | ✅ Required | Database | Creating triggers |
| **VIEW DEFINITION** | ❌ Optional | ❌ Optional | Schema | Inspecting objects |
| **VIEW CHANGE TRACKING** | ❌ Optional | ❌ Optional | Schema | Change Tracking provider |

### Schema-Level vs Database-Level Permissions

**Schema-Level** (more restrictive, recommended):
```sql
-- Applies only to specific schema (e.g., "Report")
GRANT SELECT ON SCHEMA::[Report] TO [SyncUser];
```
- ✅ Follows principle of least privilege
- ✅ User cannot access other schemas
- ✅ Recommended for production

**Database-Level** (less restrictive):
```sql
-- Applies to entire database
GRANT CREATE TABLE TO [SyncUser];
```
- ⚠️ Required for CREATE permissions
- ⚠️ User can create objects in any schema (unless restricted)
- ℹ️ Common in development environments

---

## Setup Instructions

### Using the Provided Script

1. **Open the script**: `Scripts/SetupSyncUserPermissions.sql`

2. **Customize configuration** (lines 15-20):
   ```sql
   DECLARE @DatabaseName NVARCHAR(128) = 'YourDatabase';
   DECLARE @LoginName NVARCHAR(128) = 'SyncAppLogin';
   DECLARE @UserName NVARCHAR(128) = 'SyncUser';
   DECLARE @Password NVARCHAR(128) = 'YourSecurePassword123!';
   DECLARE @SyncSchema NVARCHAR(128) = 'Report';
   DECLARE @ScopeSchema NVARCHAR(128) = 'dbo';
   ```

3. **Choose scenario**:
   - **Production**: Execute SCENARIO A section (pre-provisioned)
   - **Development**: Uncomment and execute SCENARIO B section (runtime provisioning)

4. **Run verification** query to check permissions

### Manual Setup Example

For pre-provisioned sync user on "Report" schema:

```sql
-- 1. Create login
USE [master];
CREATE LOGIN [SyncAppLogin] WITH PASSWORD = 'YourSecurePassword123!';

-- 2. Create user
USE [YourDatabase];
CREATE USER [SyncUser] FOR LOGIN [SyncAppLogin];

-- 3. Set default schema
ALTER USER [SyncUser] WITH DEFAULT_SCHEMA = [Report];

-- 4. Grant minimal permissions
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[Report] TO [SyncUser];
GRANT EXECUTE ON SCHEMA::[Report] TO [SyncUser];
GRANT ALTER ON SCHEMA::[Report] TO [SyncUser];

-- 5. Grant permissions on scope_info tables
GRANT SELECT, INSERT, UPDATE, DELETE, ALTER ON OBJECT::dbo.scope_info TO [SyncUser];
GRANT SELECT, INSERT, UPDATE, DELETE, ALTER ON OBJECT::dbo.scope_info_client TO [SyncUser];
```

---

## Testing Permissions

### Test Data Access

```sql
-- Execute as sync user
EXECUTE AS USER = 'SyncUser';

-- Should succeed
SELECT * FROM Report.Product WHERE 1=0;

REVERT;
```

### Test Stored Procedure Execution

```sql
EXECUTE AS USER = 'SyncUser';

-- Should succeed (if procedures exist)
EXEC Report.Product_selectchanges @sync_min_timestamp = 0;

REVERT;
```

### Test CREATE Permission

```sql
EXECUTE AS USER = 'SyncUser';

BEGIN TRY
    CREATE TABLE Report.TestTable (Id INT);
    DROP TABLE Report.TestTable;
    PRINT '✓ Has CREATE TABLE permission (Runtime Provisioning)';
END TRY
BEGIN CATCH
    PRINT '✗ No CREATE TABLE permission (Pre-Provisioned - Expected)';
END CATCH

REVERT;
```

### Verify All Permissions

```sql
SELECT
    dp.name AS UserName,
    perm.permission_name AS Permission,
    perm.state_desc AS State,
    CASE
        WHEN perm.class_desc = 'SCHEMA' THEN SCHEMA_NAME(perm.major_id)
        WHEN perm.class_desc = 'OBJECT_OR_COLUMN' THEN OBJECT_NAME(perm.major_id)
        ELSE perm.class_desc
    END AS AppliesTo
FROM sys.database_principals dp
JOIN sys.database_permissions perm ON dp.principal_id = perm.grantee_principal_id
WHERE dp.name = 'SyncUser'
ORDER BY perm.class_desc, perm.permission_name;
```

---

## Common Issues

### Issue 1: "Permission Denied" on CREATE TABLE

**Error**: `CREATE TABLE permission denied in database 'YourDatabase'`

**Cause**: Using runtime provisioning but user lacks CREATE TABLE permission

**Solutions**:
- **Option A**: Grant CREATE TABLE permission (development only)
- **Option B**: Switch to pre-provisioned schemas (production)
  ```csharp
  var options = new SyncOptions { DisableProvisioning = true };
  ```

### Issue 2: "Cannot Find Stored Procedure"

**Error**: `Could not find stored procedure 'Report.Product_selectchanges'`

**Cause**: Using pre-provisioned mode but objects don't exist

**Solutions**:
1. Generate and apply provisioning scripts:
   ```csharp
   var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(setup);
   // Apply scripts via migration tool
   ```
2. Validate objects exist using `Scripts/ValidatePreProvisionedObjects.sql`

### Issue 3: Changes Not Syncing (Silent Failure)

**Symptom**: Sync completes successfully but changes don't appear

**Cause**: **Missing triggers** - changes not being tracked

**Solutions**:
1. Verify triggers exist:
   ```sql
   SELECT name, type_desc
   FROM sys.triggers
   WHERE parent_id = OBJECT_ID('Report.Product');
   ```
2. Expected triggers: `*_insert_trigger`, `*_update_trigger`, `*_delete_trigger`
3. Use validation script: `Scripts/ValidatePreProvisionedObjects.sql`

### Issue 4: "Access Denied" on scope_info

**Error**: `SELECT permission denied on object 'scope_info'`

**Cause**: scope_info tables in different schema than sync schema

**Solution**: Grant explicit permissions:
```sql
GRANT SELECT, INSERT, UPDATE, DELETE, ALTER ON OBJECT::dbo.scope_info TO [SyncUser];
GRANT SELECT, INSERT, UPDATE, DELETE, ALTER ON OBJECT::dbo.scope_info_client TO [SyncUser];
```

### Issue 5: Every Sync is a Full Sync

**Symptom**: Sync always downloads all rows, ignoring previous sync

**Cause**: Cannot update scope metadata (missing ALTER permission)

**Solution**: Grant ALTER permission:
```sql
GRANT ALTER ON SCHEMA::[Report] TO [SyncUser];
```

---

## Security Best Practices

### 1. Use Strong Authentication

```sql
-- Use strong passwords
CREATE LOGIN [SyncAppLogin] WITH PASSWORD = 'Complex!Pass123$%^';

-- Consider Windows Authentication (more secure)
CREATE LOGIN [DOMAIN\SyncAppService] FROM WINDOWS;
```

### 2. Principle of Least Privilege

```sql
-- ✅ Good: Schema-specific permissions
GRANT SELECT ON SCHEMA::[Report] TO [SyncUser];

-- ❌ Bad: Database-wide permissions
GRANT SELECT TO [SyncUser];  -- Gives access to ALL schemas

-- ❌ Worse: Database owner
ALTER ROLE db_owner ADD MEMBER [SyncUser];  -- Never do this!
```

### 3. Separate Sync Schema

```sql
-- Create dedicated schema for sync objects
CREATE SCHEMA [Sync];

-- Move sync tables to dedicated schema
-- Report.Product → Report schema (app tables)
-- Report.Product_tracking → Sync schema (sync infrastructure)
```

### 4. Connection String Security

```csharp
// ✅ Good: Secure storage
var connectionString = await keyVault.GetSecretAsync("SyncConnectionString");

// ❌ Bad: Hardcoded credentials
var connectionString = "Server=...;User=SyncUser;Password=hardcoded123;";
```

### 5. Enable Auditing

```sql
-- Enable SQL Server Audit for sync user
CREATE SERVER AUDIT SyncUserAudit TO FILE (FILEPATH = 'C:\Audit\');
CREATE DATABASE AUDIT SPECIFICATION SyncUserActivity
FOR SERVER AUDIT SyncUserAudit
ADD (SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[Report] BY [SyncUser]);
```

### 6. Use Read-Only Connections When Possible

```csharp
// If client is download-only
var options = new SyncOptions
{
    DisableProvisioning = true
};

// Use ApplicationIntent=ReadOnly in connection string (if using AlwaysOn)
var connectionString = "Server=...;ApplicationIntent=ReadOnly;...";
```

### 7. Regular Permission Reviews

```sql
-- Review all permissions quarterly
SELECT
    USER_NAME(grantee_principal_id) AS User,
    permission_name AS Permission,
    state_desc AS State,
    OBJECT_NAME(major_id) AS Object
FROM sys.database_permissions
WHERE USER_NAME(grantee_principal_id) = 'SyncUser';
```

---

## Additional Resources

- **[PREPROVISIONED_SCHEMA_USAGE.md](PREPROVISIONED_SCHEMA_USAGE.md)** - Complete guide for pre-provisioned schemas
- **[SCRIPT_GENERATION_USAGE.md](SCRIPT_GENERATION_USAGE.md)** - Generate SQL provisioning scripts
- **[Scripts/SetupSyncUserPermissions.sql](Scripts/SetupSyncUserPermissions.sql)** - Ready-to-use setup script
- **[Scripts/ValidatePreProvisionedObjects.sql](Scripts/ValidatePreProvisionedObjects.sql)** - Validate deployed objects
- **[Samples/PreProvisionedSync/](Samples/PreProvisionedSync/)** - Working code example
- **[docs/Provision.rst](docs/Provision.rst)** - Official documentation

---

## Summary

### For Production (Recommended)

1. **Use pre-provisioned schemas** (`DisableProvisioning = true`)
2. **Grant minimal permissions**: SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER
3. **Deploy objects via migrations**: Flyway, Liquibase, EF Migrations
4. **Validate before sync**: Use `ValidatePreProvisionedObjects.sql`

### For Development

1. **Use runtime provisioning** (default behavior)
2. **Grant full permissions**: Include CREATE TABLE, CREATE PROCEDURE
3. **Let Dotmim.Sync create objects** automatically during first sync
4. **Transition to pre-provisioned** before going to production

### Key Takeaway

> **Pre-provisioned schemas** with **minimal permissions** provide the best security posture for production environments while maintaining full sync functionality.
