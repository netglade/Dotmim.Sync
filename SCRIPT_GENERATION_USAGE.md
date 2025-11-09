# SQL Script Generation for Pre-Provisioned Schemas

This document explains how to use the automatic SQL script generation feature to create migration files for pre-provisioned database scenarios.

## Overview

The `GetProvisioningScriptsAsync()` method generates SQL scripts for all required Dotmim.Sync objects without executing them. This is perfect for:
- Creating migration files for version control
- Deploying to databases with limited permissions
- Reviewing what will be created before executing
- Generating client-side scripts from server schema

---

## Basic Usage

### Server-Side Script Generation

```csharp
using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Enumerations;

// 1. Create server provider
var serverProvider = new SqlSyncProvider(serverConnectionString);

// 2. Create server orchestrator
var remoteOrchestrator = new RemoteOrchestrator(serverProvider);

// 3. Define your setup (tables and filters)
var setup = new SyncSetup("ProductCategory", "Product");

// Add filters (optional)
var categoryFilter = new SetupFilter("ProductCategory");
categoryFilter.AddParameter("IsActive", "ProductCategory", true);
categoryFilter.AddParameter("ProductCategoryID", "ProductCategory", true);
categoryFilter.AddWhere("IsActive", "ProductCategory", "IsActive");
categoryFilter.AddWhere("ProductCategoryID", "ProductCategory", "ProductCategoryID");
setup.Filters.Add(categoryFilter);

// Add simple filter on Product
setup.Filters.Add("Product", "ProductCategoryID");

// 4. ⭐ GENERATE SCRIPTS
var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
    setup: setup,
    provision: SyncProvision.TrackingTable | SyncProvision.StoredProcedures | SyncProvision.Triggers
);

// 5. Save to file
await File.WriteAllTextAsync("server_migration.sql", scripts);

Console.WriteLine("Scripts generated successfully!");
```

---

## Complete Example: Web Scenario with Filters

This example shows how to generate scripts for both server and client in a HelloWebAuthSync-style scenario.

### Server Project (Generate Server Scripts)

```csharp
using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Enumerations;
using System;
using System.IO;
using System.Threading.Tasks;

class ServerScriptGenerator
{
    static async Task Main(string[] args)
    {
        // Server connection (full permissions for script generation)
        var serverConnectionString =
            "Server=localhost;Database=ServerDb;Integrated Security=true;";

        var serverProvider = new SqlSyncProvider(serverConnectionString);
        var remoteOrchestrator = new RemoteOrchestrator(serverProvider);

        // ⭐ Define setup with tables and filters (same as your HelloWebAuthSync)
        var setup = new SyncSetup("ProductCategory", "Product");

        // Complex filter on ProductCategory
        var pcFilter = new SetupFilter("ProductCategory");
        pcFilter.AddParameter("IsActive", "ProductCategory", true);
        pcFilter.AddParameter("ProductCategoryID", "ProductCategory", true);
        pcFilter.AddWhere("IsActive", "ProductCategory", "IsActive");
        pcFilter.AddWhere("ProductCategoryID", "ProductCategory", "ProductCategoryID");
        setup.Filters.Add(pcFilter);

        // Simple filter on Product
        setup.Filters.Add("Product", "ProductCategoryID");

        // ⭐ GENERATE SERVER SCRIPTS
        Console.WriteLine("Generating server provisioning scripts...");

        var serverScripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
            setup: setup,
            provision: SyncProvision.TrackingTable |
                      SyncProvision.StoredProcedures |
                      SyncProvision.Triggers
        );

        // Save to migrations folder
        var serverScriptPath = "Migrations/001_Server_Provision.sql";
        Directory.CreateDirectory(Path.GetDirectoryName(serverScriptPath));
        await File.WriteAllTextAsync(serverScriptPath, serverScripts);

        Console.WriteLine($"Server scripts saved to: {serverScriptPath}");

        // ⭐ GENERATE CLIENT SCRIPTS (for deployment to client databases)
        Console.WriteLine("Generating client provisioning scripts...");

        // First, provision the server to get the full schema
        await remoteOrchestrator.ProvisionAsync(setup);
        var serverScopeInfo = await remoteOrchestrator.GetScopeInfoAsync();

        // Now generate client scripts using server schema
        var localOrchestrator = new LocalOrchestrator(serverProvider); // Just for script generation

        var clientScripts = await localOrchestrator.GetProvisioningScriptsAsync(
            serverScopeInfo: serverScopeInfo,
            provision: SyncProvision.Table |           // ← Client needs tables
                      SyncProvision.TrackingTable |
                      SyncProvision.StoredProcedures |
                      SyncProvision.Triggers |
                      SyncProvision.ScopeInfo |        // ← Client needs scope tables
                      SyncProvision.ScopeInfoClient
        );

        var clientScriptPath = "Migrations/001_Client_Provision.sql";
        await File.WriteAllTextAsync(clientScriptPath, clientScripts);

        Console.WriteLine($"Client scripts saved to: {clientScriptPath}");
        Console.WriteLine("✅ All scripts generated successfully!");
    }
}
```

### What Gets Generated

**Server Scripts (`001_Server_Provision.sql`):**
```sql
-- =====================================================
-- Dotmim.Sync Provisioning Scripts
-- =====================================================
-- Generated: 2025-11-09 15:30:00 UTC
-- Scope: DefaultScope
-- Version: 1.0
-- Provision: TrackingTable, StoredProcedures, Triggers
-- =====================================================

-- =====================================================
-- Table: [dbo].[ProductCategory]
-- =====================================================

-- Creating tracking table for [dbo].[ProductCategory]
CREATE TABLE [dbo].[ProductCategory_tracking] (
    [ProductCategoryID] [uniqueidentifier] NOT NULL,
    [update_scope_id] [uniqueidentifier] NULL,
    [timestamp] [bigint] NULL,
    [sync_row_is_tombstone] [bit] NOT NULL DEFAULT 0,
    [last_change_datetime] [datetime] NULL,
    PRIMARY KEY ([ProductCategoryID])
)
GO

-- Creating triggers for [dbo].[ProductCategory]
-- Insert trigger
CREATE TRIGGER [dbo].[ProductCategory_insert_trigger] ON [dbo].[ProductCategory]
AFTER INSERT
AS
BEGIN
    -- ... (trigger code with change tracking)
END
GO

-- Update trigger
CREATE TRIGGER [dbo].[ProductCategory_update_trigger] ON [dbo].[ProductCategory]
AFTER UPDATE
AS
BEGIN
    -- ... (trigger code)
END
GO

-- Delete trigger
CREATE TRIGGER [dbo].[ProductCategory_delete_trigger] ON [dbo].[ProductCategory]
AFTER DELETE
AS
BEGIN
    -- ... (trigger code)
END
GO

-- Creating stored procedures for [dbo].[ProductCategory]
-- SelectChanges
CREATE PROCEDURE [dbo].[ProductCategory_selectchanges]
    @sync_min_timestamp BIGINT,
    @sync_scope_id UNIQUEIDENTIFIER
AS
BEGIN
    -- ... (stored procedure code)
END
GO

-- SelectChangesWithFilters (includes filter parameters!)
CREATE PROCEDURE [dbo].[ProductCategory_selectchanges_filtered]
    @sync_min_timestamp BIGINT,
    @sync_scope_id UNIQUEIDENTIFIER,
    @IsActive BIT,
    @ProductCategoryID UNIQUEIDENTIFIER
AS
BEGIN
    -- ... (stored procedure code with WHERE clauses from filter)
END
GO

-- ... more stored procedures ...

-- =====================================================
-- Table: [dbo].[Product]
-- =====================================================
-- ... (repeat for Product table)

-- =====================================================
-- Provisioning scripts generation completed
-- =====================================================
```

---

## Script Generation Options

### Controlling What Gets Generated

Use `SyncProvision` flags to control which objects are scripted:

```csharp
// ⭐ Generate everything (for initial setup)
var allScripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
    setup: setup,
    provision: SyncProvision.Table |
              SyncProvision.TrackingTable |
              SyncProvision.StoredProcedures |
              SyncProvision.Triggers |
              SyncProvision.ScopeInfo |
              SyncProvision.ScopeInfoClient
);

// ⭐ Generate only tracking infrastructure (tables already exist)
var trackingOnlyScripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
    setup: setup,
    provision: SyncProvision.TrackingTable |
              SyncProvision.StoredProcedures |
              SyncProvision.Triggers
);

// ⭐ Generate only stored procedures (for updates)
var spOnlyScripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
    setup: setup,
    provision: SyncProvision.StoredProcedures
);
```

### Saving Directly to File

```csharp
// Convenience method to generate and save in one call
await remoteOrchestrator.SaveProvisioningScriptsAsync(
    filePath: "Migrations/001_Server_Provision.sql",
    setup: setup,
    provision: SyncProvision.TrackingTable |
              SyncProvision.StoredProcedures |
              SyncProvision.Triggers
);
```

---

## Using with Specific Schemas

For the "Report" schema scenario:

```csharp
// Define setup with specific schema
var setup = new SyncSetup();
setup.Tables.Add("ProductCategory", "Report");  // ← Schema = "Report"
setup.Tables.Add("Product", "Report");

// Add filters...
var pcFilter = new SetupFilter("ProductCategory", "Report");  // ← Schema here too
pcFilter.AddParameter("IsActive", "ProductCategory", true);
// ...
setup.Filters.Add(pcFilter);

// Generate scripts - all objects will be in "Report" schema
var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
    setup: setup,
    provision: SyncProvision.TrackingTable |
              SyncProvision.StoredProcedures |
              SyncProvision.Triggers
);
```

Generated output will include:
```sql
-- Creating schema Report
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'Report')
    EXEC('CREATE SCHEMA [Report]')
GO

-- Creating tracking table for [Report].[ProductCategory]
CREATE TABLE [Report].[ProductCategory_tracking] (
    -- ...
)
GO
```

---

## Workflow: From Setup to Deployment

### 1. Development Phase

```csharp
// Generate scripts from your setup
var setup = new SyncSetup("Table1", "Table2");
// ... configure filters ...

var remoteOrchestrator = new RemoteOrchestrator(serverProvider);

// Save server scripts
await remoteOrchestrator.SaveProvisioningScriptsAsync(
    "Migrations/001_Server_Initial.sql",
    setup: setup
);

// Provision server to get schema
await remoteOrchestrator.ProvisionAsync(setup);
var serverScope = await remoteOrchestrator.GetScopeInfoAsync();

// Save client scripts
var localOrchestrator = new LocalOrchestrator(serverProvider);
await localOrchestrator.SaveProvisioningScriptsAsync(
    "Migrations/001_Client_Initial.sql",
    serverScopeInfo: serverScope
);
```

### 2. Version Control

```bash
git add Migrations/001_Server_Initial.sql
git add Migrations/001_Client_Initial.sql
git commit -m "Add Dotmim.Sync provisioning scripts"
```

### 3. Deployment

**Server deployment (full permissions):**
```bash
sqlcmd -S ServerInstance -d ServerDb -i Migrations/001_Server_Initial.sql
```

**Client deployment (limited permissions):**
```bash
sqlcmd -S ClientInstance -d ClientDb -U ReportUser -P Password -i Migrations/001_Client_Initial.sql
```

### 4. Validation

```bash
# Run validation script
sqlcmd -S ClientInstance -d ClientDb -U ReportUser -P Password -i Scripts/ValidatePreProvisionedObjects.sql
```

### 5. Client Application

```csharp
var options = new SyncOptions { DisableProvisioning = true };
var agent = new SyncAgent(clientProvider, webRemoteOrchestrator, options);
var result = await agent.SynchronizeAsync();
```

---

## Advanced Scenarios

### Regenerating Scripts After Schema Changes

```csharp
// Server schema changed - regenerate scripts
var setup = new SyncSetup("Table1", "Table2", "NewTable3");  // ← Added new table
// ... reconfigure filters ...

// Regenerate with version number
await remoteOrchestrator.SaveProvisioningScriptsAsync(
    "Migrations/002_Server_AddNewTable.sql",
    setup: setup
);

await remoteOrchestrator.ProvisionAsync(setup);
var serverScope = await remoteOrchestrator.GetScopeInfoAsync();

await localOrchestrator.SaveProvisioningScriptsAsync(
    "Migrations/002_Client_AddNewTable.sql",
    serverScopeInfo: serverScope
);
```

### Generating Scripts for Specific Tables

```csharp
// Only generate scripts for one table
var setup = new SyncSetup("ProductCategory");  // ← Single table
// ... configure filters for this table ...

var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
    setup: setup
);
```

### Combining with Manual SQL

```csharp
// Generate Dotmim.Sync objects
var syncScripts = await remoteOrchestrator.GetProvisioningScriptsAsync(setup);

// Combine with your custom migration SQL
var combinedScripts = new StringBuilder();
combinedScripts.AppendLine("-- Custom migration script");
combinedScripts.AppendLine("-- Add custom indexes");
combinedScripts.AppendLine("CREATE INDEX IX_ProductCategory_Name ON Report.ProductCategory(Name);");
combinedScripts.AppendLine("GO");
combinedScripts.AppendLine();
combinedScripts.AppendLine("-- Dotmim.Sync objects");
combinedScripts.AppendLine(syncScripts);

await File.WriteAllTextAsync("Migrations/001_Combined.sql", combinedScripts.ToString());
```

---

## Troubleshooting

### Error: "No Setup in your server scopeInfo"

**Cause:** ScopeInfo doesn't have a setup defined.

**Solution:** Pass the setup explicitly:
```csharp
var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
    setup: setup  // ← Provide setup here
);
```

### Error: "No Schema in your server scopeInfo"

**Cause:** Schema hasn't been loaded from the database.

**Solution:** The method will automatically load the schema. Ensure your tables exist in the database, or provision first:
```csharp
await remoteOrchestrator.ProvisionAsync(setup);
var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync();
```

### Scripts contain errors when applied

**Cause:** SQL syntax may be database-specific.

**Solution:** Review generated scripts and ensure they match your database provider. The scripts are generated by the provider's builders, so they should be correct for that provider.

---

## Benefits

✅ **Version Control:** Scripts are plain text SQL files
✅ **Review Process:** Review SQL before deployment
✅ **CI/CD Integration:** Run scripts in deployment pipelines
✅ **Rollback Support:** Create corresponding DROP scripts
✅ **Documentation:** Scripts serve as documentation
✅ **Compliance:** Meet change management requirements
✅ **Testing:** Apply scripts to test environments first

---

## Related Documentation

- [PREPROVISIONED_SCHEMA_USAGE.md](PREPROVISIONED_SCHEMA_USAGE.md) - How to use DisableProvisioning
- [PREPROVISIONED_TECHNICAL_DETAILS.md](PREPROVISIONED_TECHNICAL_DETAILS.md) - Technical deep dive
- [WEB_SCENARIO_ANALYSIS.md](WEB_SCENARIO_ANALYSIS.md) - Web/HTTP scenarios
- [Scripts/ValidatePreProvisionedObjects.sql](Scripts/ValidatePreProvisionedObjects.sql) - Validation script

---

## API Reference

### RemoteOrchestrator Methods

```csharp
// Generate scripts from setup
Task<string> GetProvisioningScriptsAsync(
    string scopeName = "DefaultScope",
    SyncSetup setup = null,
    SyncProvision provision = SyncProvision.NotSet,
    DbConnection connection = null,
    DbTransaction transaction = null,
    IProgress<ProgressArgs> progress = null,
    CancellationToken cancellationToken = default)

// Save scripts to file
Task SaveProvisioningScriptsAsync(
    string filePath,
    string scopeName = "DefaultScope",
    SyncSetup setup = null,
    SyncProvision provision = SyncProvision.NotSet,
    DbConnection connection = null,
    DbTransaction transaction = null,
    IProgress<ProgressArgs> progress = null,
    CancellationToken cancellationToken = default)
```

### LocalOrchestrator Methods

```csharp
// Generate client scripts from server scope
Task<string> GetProvisioningScriptsAsync(
    ScopeInfo serverScopeInfo,
    SyncProvision provision = SyncProvision.NotSet,
    DbConnection connection = null,
    DbTransaction transaction = null,
    IProgress<ProgressArgs> progress = null,
    CancellationToken cancellationToken = default)

// Save client scripts to file
Task SaveProvisioningScriptsAsync(
    string filePath,
    ScopeInfo serverScopeInfo,
    SyncProvision provision = SyncProvision.NotSet,
    DbConnection connection = null,
    DbTransaction transaction = null,
    IProgress<ProgressArgs> progress = null,
    CancellationToken cancellationToken = default)
```

### BaseOrchestrator Method

```csharp
// Generate scripts from ScopeInfo
Task<string> GetProvisioningScriptsAsync(
    ScopeInfo scopeInfo,
    SyncProvision provision = SyncProvision.NotSet,
    DbConnection connection = null,
    DbTransaction transaction = null,
    IProgress<ProgressArgs> progress = null,
    CancellationToken cancellationToken = default)
```
