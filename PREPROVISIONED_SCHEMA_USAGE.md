# Pre-Provisioned Schema Support

This document explains how to use Dotmim.Sync with pre-provisioned database objects, useful when database users have limited permissions.

## Use Case

This feature is designed for scenarios where:
- Database users only have limited permissions (SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER)
- Users **cannot** create new database objects (no CREATE permission)
- All database objects must be created through migrations or SQL scripts before the application runs
- Client database access is restricted to specific schemas

## Configuration

### Step 1: Enable DisableProvisioning Option

On the client side (where you have limited permissions), create `SyncOptions` with `DisableProvisioning` set to `true`:

```csharp
var options = new SyncOptions
{
    DisableProvisioning = true
};
```

### Step 2: Pre-Create Database Objects

Before running your client application, you must create all required database objects:
- Tracking tables
- Triggers
- Stored procedures
- Scope info tables (scope_info and scope_info_client)

You can generate the SQL scripts using the server-side orchestrator (see Script Generation section below).

### Step 3: Configure Client Synchronization

```csharp
// Client connection string with limited permissions
var clientConnectionString = "Server=...;Database=ClientDb;User=LimitedUser;...";

// Create provider
var clientProvider = new SqlSyncProvider(clientConnectionString);

// Create options with DisableProvisioning
var options = new SyncOptions
{
    DisableProvisioning = true,
    ScopeInfoTableName = "scope_info"
};

// Create remote orchestrator (web endpoint)
var serverOrchestrator = new WebRemoteOrchestrator("https://server/api/sync");

// Create agent
var agent = new SyncAgent(clientProvider, serverOrchestrator, options);

// Synchronize
var result = await agent.SynchronizeAsync();
```

## Complete Example: Server (.NET 8) and Client (.NET Core 3.1)

### Server Side (Full Permissions)

```csharp
using Dotmim.Sync;
using Dotmim.Sync.SqlServer;

// Server connection string (full permissions)
var serverConnectionString = "Server=localhost;Database=ServerDb;Integrated Security=true;";
var serverProvider = new SqlSyncProvider(serverConnectionString);

// Setup tables and filters
var setup = new SyncSetup("ProductCategory", "Product");

// Add filters (similar to HelloWebAuthSync sample)
var categoryFilter = new SetupFilter("ProductCategory");
categoryFilter.AddParameter("IsActive", "ProductCategory", true);
categoryFilter.AddParameter("ProductCategoryID", "ProductCategory", true);
categoryFilter.AddWhere("IsActive", "ProductCategory", "IsActive");
categoryFilter.AddWhere("ProductCategoryID", "ProductCategory", "ProductCategoryID");
setup.Filters.Add(categoryFilter);

// Filter Product by ProductCategoryID
setup.Filters.Add("Product", "ProductCategoryID");

// Provision server (creates tracking tables, triggers, stored procedures)
var remoteOrchestrator = new RemoteOrchestrator(serverProvider);
await remoteOrchestrator.ProvisionAsync(setup);

Console.WriteLine("Server provisioned successfully!");
```

### Generate SQL Scripts for Client Migration

```csharp
// Get server scope info
var serverScopeInfo = await remoteOrchestrator.GetScopeInfoAsync();

// Note: Script generation feature is planned for Phase 3
// For now, you can manually create the scripts by:
// 1. Running ProvisionAsync on a test client database with full permissions
// 2. Using SQL Server Management Studio to script out the created objects
// 3. Applying those scripts to your production client database

// Alternatively, use SQL Server's built-in scripting:
// - Right-click database → Tasks → Generate Scripts
// - Select tracking tables, triggers, and stored procedures created by Dotmim.Sync
```

### Client Side (Limited Permissions - .NET Core 3.1)

**Prerequisites:**
- All tracking tables, triggers, stored procedures, and scope tables must be created via migration scripts
- Database user has SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER permissions on the required schema

```csharp
using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Web.Client;

// Client connection string (limited permissions)
var clientConnectionString = "Server=localhost;Database=ClientDb;User=ReportUser;Password=...;";

// Create provider
var clientProvider = new SqlSyncProvider(clientConnectionString);

// Create options with DisableProvisioning
var options = new SyncOptions
{
    DisableProvisioning = true,  // CRITICAL: Skip provisioning
    ScopeInfoTableName = "scope_info"
};

// Create remote orchestrator (web endpoint)
var serverOrchestrator = new WebRemoteOrchestrator("https://your-server/api/sync");

// Create agent
var agent = new SyncAgent(clientProvider, serverOrchestrator, options);

// Set up filter parameters
var parameters = new SyncParameters(
    ("ProductCategoryID", new Guid("CFBDA25C-DF71-47A7-B81B-64EE161AA37C"))
);

// Synchronize (download-only if using custom provider)
var result = await agent.SynchronizeAsync(parameters);

Console.WriteLine($"Sync completed!");
Console.WriteLine($"Downloaded: {result.TotalChangesDownloadedFromServer} changes");
Console.WriteLine($"Uploaded: {result.TotalChangesUploadedToServer} changes");
```

## Database Permissions Required

### Minimum Client Permissions

Grant the following permissions to your limited database user:

```sql
-- Grant schema access (assuming "Report" schema)
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER ON SCHEMA::Report TO ReportUser;

-- Or grant specific object permissions
GRANT SELECT ON Report.scope_info TO ReportUser;
GRANT SELECT, INSERT, UPDATE, DELETE ON Report.scope_info_client TO ReportUser;

-- For each synced table (example: ProductCategory)
GRANT SELECT, INSERT, UPDATE, DELETE ON Report.ProductCategory TO ReportUser;
GRANT SELECT, INSERT, UPDATE, DELETE ON Report.ProductCategory_tracking TO ReportUser;
GRANT EXECUTE ON Report.ProductCategory_selectchanges TO ReportUser;
GRANT EXECUTE ON Report.ProductCategory_update TO ReportUser;
-- ... (repeat for all stored procedures)

-- Repeat for all synced tables
```

## Download-Only Synchronization

If you need download-only synchronization (client never uploads changes), create a custom provider:

```csharp
public class SqlSyncDownloadOnlyProvider : SqlSyncProvider
{
    public SqlSyncDownloadOnlyProvider(string connectionString)
        : base(connectionString)
    {
    }

    public override DbSyncAdapter GetSyncAdapter(SyncTable tableDescription, ScopeInfo scopeInfo)
    {
        return new SqlDownloadOnlySyncAdapter(tableDescription, scopeInfo);
    }
}

public class SqlDownloadOnlySyncAdapter : SqlSyncAdapter
{
    public SqlDownloadOnlySyncAdapter(SyncTable tableDescription, ScopeInfo scopeInfo)
        : base(tableDescription, scopeInfo)
    {
    }

    public override Task<DbCommand> GetCommandAsync(DbCommandType commandType, SyncFilter filter = null)
    {
        // Only allow SELECT operations
        if (commandType == DbCommandType.SelectChanges ||
            commandType == DbCommandType.SelectInitializedChanges ||
            commandType == DbCommandType.SelectRow)
        {
            return base.GetCommandAsync(commandType, filter);
        }

        // Return null for upload operations (INSERT/UPDATE/DELETE from client)
        return Task.FromResult<DbCommand>(null);
    }
}
```

Then use the custom provider on the client:

```csharp
var clientProvider = new SqlSyncDownloadOnlyProvider(clientConnectionString);
```

## Working with Specific Schemas

To ensure all objects are created in a specific schema (e.g., "Report"):

```csharp
var setup = new SyncSetup();
setup.Tables.Add("ProductCategory", "Report");  // Table in "Report" schema
setup.Tables.Add("Product", "Report");          // Table in "Report" schema
```

Tracking tables will automatically be created in the same schema as the base tables.

## Troubleshooting

### "Object does not exist" errors

**Cause:** The required tracking tables, triggers, or stored procedures are missing.

**Solution:** Ensure all objects are pre-created through migrations before running the client application.

### Permission denied errors

**Cause:** The database user lacks necessary permissions.

**Solution:** Grant SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER permissions on the schema or specific objects.

### Schema mismatch errors

**Cause:** The client's pre-provisioned schema doesn't match the server's schema.

**Solution:** Regenerate the client migration scripts using the latest server schema.

## Migration Workflow

1. **Server Side:**
   - Define your `SyncSetup` with tables and filters
   - Run `ProvisionAsync()` on server
   - Get server scope info via `GetScopeInfoAsync()`

2. **Generate Client Scripts:**
   - Temporarily provision a test client database with full permissions
   - Script out all created objects (tracking tables, triggers, stored procedures)
   - Save scripts to version control

3. **Client Deployment:**
   - Apply migration scripts to client database
   - Grant minimal permissions to client database user
   - Deploy client application with `DisableProvisioning = true`

4. **Client Runtime:**
   - Application runs synchronization without attempting to create objects
   - All objects are assumed to exist and are used as-is

## Benefits

✅ **Security:** Clients run with minimal database permissions
✅ **Compliance:** Database changes are tracked through migration system
✅ **Auditability:** All schema changes are version-controlled
✅ **Safety:** Prevents accidental schema modifications by client applications
✅ **Compatibility:** Works with existing Dotmim.Sync features (filters, web sync, etc.)

## Limitations

⚠️ **Manual Script Generation:** For now, you must manually generate and maintain client migration scripts
⚠️ **Schema Synchronization:** You must ensure client schema matches server schema
⚠️ **No Auto-Migration:** Schema changes on the server require manual updates to client migration scripts

## Related Samples

- **HelloWebAuthSync:** Demonstrates web synchronization with filters and authentication
- **CustomProvider:** Shows how to create download-only providers

## Future Enhancements (Planned)

- **Phase 3:** Automatic SQL script generation via `GetProvisioningScriptsAsync()`
- **Phase 4:** Enhanced schema validation for pre-provisioned scenarios
- **Phase 5:** Migration generation tools and utilities
