# Pre-Provisioned Schema Sample

This sample demonstrates how to use Dotmim.Sync with pre-provisioned database schemas, which is useful when database users have limited permissions or when all schema changes must go through controlled migration pipelines.

## Overview

This sample shows a complete workflow for working with pre-provisioned schemas:

1. **Generate SQL Scripts**: Create SQL scripts for all required sync objects
2. **Apply Scripts**: Deploy scripts through migration pipeline (simulated)
3. **Sync with DisableProvisioning**: Run synchronization with pre-existing objects

## Use Cases

Use pre-provisioned schemas when:

- **Limited Permissions**: Database users cannot CREATE objects at runtime (only SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER)
- **Schema Restrictions**: Database users are restricted to specific schemas (e.g., "Report" schema only)
- **Controlled Migrations**: All schema changes must be approved and deployed via migration tools
- **Security Policies**: Production environments require pre-approved database changes
- **Compliance**: Audit trails required for all schema modifications

## How It Works

### Step 1: Generate Provisioning Scripts

```csharp
var remoteOrchestrator = new RemoteOrchestrator(serverProvider);
var setup = new SyncSetup("SalesLT.ProductCategory", "SalesLT.Product");

// Generate scripts for server side
var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
    setup: setup,
    provision: SyncProvision.TrackingTable | SyncProvision.Triggers | SyncProvision.StoredProcedures
);

// Save to file for migration pipeline
await File.WriteAllTextAsync("server_provisioning.sql", scripts);
```

The generated scripts include:
- Tracking tables (e.g., `SalesLT.Product_tracking`)
- Triggers (INSERT, UPDATE, DELETE on base tables)
- Stored procedures (SelectChanges, UpdateRow, DeleteRow, etc.)
- Scope info tables

### Step 2: Apply Scripts via Migration Pipeline

In this sample, scripts are applied directly. In production, you would:

1. Commit scripts to version control
2. Review scripts (DBA, security team)
3. Apply via migration tools:
   - **Flyway**: SQL-based migrations
   - **Liquibase**: Database-agnostic migrations
   - **EF Core Migrations**: .NET-based migrations
   - **Custom deployment scripts**

### Step 3: Sync with DisableProvisioning

```csharp
var options = new SyncOptions
{
    DisableProvisioning = true  // Skip all CREATE operations
};

var agent = new SyncAgent(clientProvider, serverProvider, options);
var result = await agent.SynchronizeAsync(setup);
```

When `DisableProvisioning = true`:
- ✓ Skips all provisioning operations
- ✓ Assumes objects already exist
- ✓ Requires only DML permissions (SELECT, INSERT, UPDATE, DELETE, EXECUTE)
- ✓ No CREATE permissions needed

## Prerequisites

- SQL Server with AdventureWorksLT database
- Tables: `SalesLT.ProductCategory`, `SalesLT.Product`
- Connection to (localdb)\\mssqllocaldb or update connection strings

## Running the Sample

1. Ensure AdventureWorksLT database exists
2. Run the sample:
   ```bash
   dotnet run
   ```
3. Follow the interactive steps

The sample will:
- Generate SQL scripts in the bin directory
- Apply scripts to PreProvServer and PreProvClient databases
- Run a sync with DisableProvisioning enabled

## Generated Files

After running, you'll find:
- `server_provisioning.sql` - Server-side objects (tracking tables, triggers, stored procedures)
- `client_provisioning.sql` - Client-side objects (tables, tracking tables, triggers, stored procedures)

## Web Sync Scenario

For ASP.NET Core web sync, configure both client and server:

**Server (Startup.cs):**
```csharp
services.AddSyncServer<SqlSyncProvider>(
    connectionString,
    "MyScope",
    setup => setup.Tables.Add("SalesLT.Product"),
    options => options.DisableProvisioning = true
);
```

**Client:**
```csharp
var options = new SyncOptions { DisableProvisioning = true };
var agent = new SyncAgent(clientProvider, webRemoteOrchestrator, options);
```

## Important Notes

### Critical: Validate All Objects

Missing objects cause different failures:

| Missing Object | Impact |
|----------------|--------|
| **Triggers** | **SILENT DATA LOSS** - Changes not tracked |
| Stored Procedures | Immediate SQL exception during sync |
| Tracking Tables | Immediate SQL exception during sync |
| Scope Tables | Sync initialization fails |

**Always validate** before deploying to production. Use the provided validation script:
```sql
-- From Scripts/ValidatePreProvisionedObjects.sql
-- Returns CRITICAL/WARNING/LOW severity for missing objects
```

### Filter Support

Filters are fully supported and included in generated stored procedures:

```csharp
var filter = new SetupFilter("SalesLT.Product");
filter.AddParameter("ProductCategoryID", "SalesLT.Product");
filter.AddWhere("ProductCategoryID", "SalesLT.Product", "ProductCategoryID");
setup.Filters.Add(filter);

// Generated stored procedures will include filter parameters
var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(setup);
```

## Additional Resources

- [PREPROVISIONED_SCHEMA_USAGE.md](../../PREPROVISIONED_SCHEMA_USAGE.md) - Complete usage guide
- [SCRIPT_GENERATION_USAGE.md](../../SCRIPT_GENERATION_USAGE.md) - Script generation guide
- [PREPROVISIONED_TECHNICAL_DETAILS.md](../../PREPROVISIONED_TECHNICAL_DETAILS.md) - Technical deep-dive
- [Scripts/ValidatePreProvisionedObjects.sql](../../Scripts/ValidatePreProvisionedObjects.sql) - Validation script

## Sample Output

```
=======================================================
Pre-Provisioned Schema Sample
=======================================================

STEP 1: Generating Provisioning Scripts
=========================================

Generating server-side provisioning scripts...
✓ Server scripts saved to: server_provisioning.sql
Generating client-side provisioning scripts...
✓ Client scripts saved to: client_provisioning.sql

Scripts generated successfully!

STEP 2: Applying Provisioning Scripts
======================================

Applying server provisioning scripts...
✓ Server provisioning complete
Applying client provisioning scripts...
✓ Client provisioning complete

All database objects are now pre-provisioned!

STEP 3: Synchronizing with DisableProvisioning
===============================================

Starting synchronization with DisableProvisioning=true...

Synchronization completed!

Results:
  Total changes downloaded: 295
  Total changes uploaded: 0
  Total changes applied: 295
  Total duration: 00:00:02.1234567

✓ Sync completed successfully without provisioning!
  - No CREATE operations were performed
  - All objects were already in place
  - Database user only needs SELECT/INSERT/UPDATE/DELETE/EXECUTE permissions
```

## Troubleshooting

**"Object not found" errors during sync:**
- Ensure scripts were applied successfully
- Check object names match exactly (schema.table)
- Validate using Scripts/ValidatePreProvisionedObjects.sql

**"Permission denied" errors:**
- User needs EXECUTE permission on stored procedures
- User needs SELECT/INSERT/UPDATE/DELETE on tables and tracking tables
- User needs ALTER permission for scope tables

**Changes not syncing:**
- Verify triggers exist and are enabled
- Missing triggers cause silent data loss
- Use validation script to check all triggers
