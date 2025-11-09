using Dotmim.Sync;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.SqlServer;
using Microsoft.Data.SqlClient;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PreProvisionedSync
{
    /// <summary>
    /// This sample demonstrates how to use pre-provisioned database schemas with Dotmim.Sync.
    ///
    /// This is useful when:
    /// - Database users have limited permissions (no CREATE rights)
    /// - Database users are restricted to specific schemas
    /// - All schema changes must go through migration pipelines
    /// - Security policies require pre-approved database changes
    ///
    /// The sample shows:
    /// 1. Generating SQL provisioning scripts from a setup
    /// 2. Applying scripts to databases (simulating migration pipeline)
    /// 3. Running sync with DisableProvisioning option
    /// </summary>
    internal class Program
    {
        // Use different databases to demonstrate the complete workflow
        private static string serverConnectionString = $"Data Source=(localdb)\\mssqllocaldb; Initial Catalog=PreProvServer;Integrated Security=true;";
        private static string clientConnectionString = $"Data Source=(localdb)\\mssqllocaldb; Initial Catalog=PreProvClient;Integrated Security=true;";

        private static async Task Main()
        {
            Console.WriteLine("=======================================================");
            Console.WriteLine("Pre-Provisioned Schema Sample");
            Console.WriteLine("=======================================================");
            Console.WriteLine();

            try
            {
                // Step 1: Setup and generate scripts
                await GenerateProvisioningScriptsAsync();

                Console.WriteLine();
                Console.WriteLine("Press any key to continue to Step 2 (Apply Scripts)...");
                Console.ReadKey();

                // Step 2: Apply scripts (simulating migration)
                await ApplyProvisioningScriptsAsync();

                Console.WriteLine();
                Console.WriteLine("Press any key to continue to Step 3 (Sync with DisableProvisioning)...");
                Console.ReadKey();

                // Step 3: Sync with DisableProvisioning
                await SyncWithDisableProvisioningAsync();

                Console.WriteLine();
                Console.WriteLine("=======================================================");
                Console.WriteLine("Sample completed successfully!");
                Console.WriteLine("=======================================================");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        /// <summary>
        /// Step 1: Generate SQL provisioning scripts
        /// This demonstrates how to generate scripts that can be deployed via migration tools
        /// </summary>
        private static async Task GenerateProvisioningScriptsAsync()
        {
            Console.WriteLine("STEP 1: Generating Provisioning Scripts");
            Console.WriteLine("=========================================");
            Console.WriteLine();

            // Create providers
            var serverProvider = new SqlSyncProvider(serverConnectionString);

            // Define what tables to sync
            // NOTE: Ensure these tables exist in your AdventureWorks database
            var setup = new SyncSetup("SalesLT.ProductCategory", "SalesLT.Product");

            // Optional: Add filters
            var filter = new SetupFilter("SalesLT.Product");
            filter.AddParameter("ProductCategoryID", "SalesLT.Product");
            filter.AddWhere("ProductCategoryID", "SalesLT.Product", "ProductCategoryID");
            setup.Filters.Add(filter);

            // Create orchestrator
            var remoteOrchestrator = new RemoteOrchestrator(serverProvider);

            Console.WriteLine("Generating server-side provisioning scripts...");

            // Generate scripts for tracking tables, triggers, and stored procedures
            var serverScripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
                setup: setup,
                provision: SyncProvision.TrackingTable | SyncProvision.Triggers | SyncProvision.StoredProcedures
            );

            // Save to file
            var serverScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_provisioning.sql");
            await File.WriteAllTextAsync(serverScriptPath, serverScripts);
            Console.WriteLine($"✓ Server scripts saved to: {serverScriptPath}");

            // For client, we need the schema from server
            var serverScopeInfo = await remoteOrchestrator.GetScopeInfoAsync(setup);

            Console.WriteLine("Generating client-side provisioning scripts...");

            var clientProvider = new SqlSyncProvider(clientConnectionString);
            var localOrchestrator = new LocalOrchestrator(clientProvider);

            // Generate client scripts (includes base tables + tracking infrastructure)
            var clientScripts = await localOrchestrator.GetProvisioningScriptsAsync(
                serverScopeInfo: serverScopeInfo,
                provision: SyncProvision.Table | SyncProvision.TrackingTable |
                          SyncProvision.Triggers | SyncProvision.StoredProcedures | SyncProvision.ScopeInfo
            );

            // Save to file
            var clientScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client_provisioning.sql");
            await File.WriteAllTextAsync(clientScriptPath, clientScripts);
            Console.WriteLine($"✓ Client scripts saved to: {clientScriptPath}");

            Console.WriteLine();
            Console.WriteLine("Scripts generated successfully!");
            Console.WriteLine("In a real scenario, these scripts would be:");
            Console.WriteLine("  1. Committed to version control");
            Console.WriteLine("  2. Reviewed by DBA/Security team");
            Console.WriteLine("  3. Applied via migration tools (Flyway, Liquibase, EF Migrations, etc.)");
        }

        /// <summary>
        /// Step 2: Apply provisioning scripts
        /// This simulates what would happen in your migration pipeline
        /// </summary>
        private static async Task ApplyProvisioningScriptsAsync()
        {
            Console.WriteLine();
            Console.WriteLine("STEP 2: Applying Provisioning Scripts");
            Console.WriteLine("======================================");
            Console.WriteLine();

            // Read the generated scripts
            var serverScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_provisioning.sql");
            var clientScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client_provisioning.sql");

            Console.WriteLine("Applying server provisioning scripts...");
            await ApplyScriptAsync(serverConnectionString, serverScriptPath);
            Console.WriteLine("✓ Server provisioning complete");

            Console.WriteLine("Applying client provisioning scripts...");
            await ApplyScriptAsync(clientConnectionString, clientScriptPath);
            Console.WriteLine("✓ Client provisioning complete");

            Console.WriteLine();
            Console.WriteLine("All database objects are now pre-provisioned!");
            Console.WriteLine("The database is ready for sync with DisableProvisioning=true");
        }

        /// <summary>
        /// Step 3: Sync with DisableProvisioning
        /// This demonstrates running sync with pre-provisioned objects
        /// </summary>
        private static async Task SyncWithDisableProvisioningAsync()
        {
            Console.WriteLine();
            Console.WriteLine("STEP 3: Synchronizing with DisableProvisioning");
            Console.WriteLine("===============================================");
            Console.WriteLine();

            // Create providers
            var serverProvider = new SqlSyncProvider(serverConnectionString);
            var clientProvider = new SqlSyncProvider(clientConnectionString);

            // Same setup as before
            var setup = new SyncSetup("SalesLT.ProductCategory", "SalesLT.Product");

            // Add the same filter
            var filter = new SetupFilter("SalesLT.Product");
            filter.AddParameter("ProductCategoryID", "SalesLT.Product");
            filter.AddWhere("ProductCategoryID", "SalesLT.Product", "ProductCategoryID");
            setup.Filters.Add(filter);

            // Create options with DisableProvisioning enabled
            var options = new SyncOptions
            {
                DisableProvisioning = true
            };

            // Create agent
            var agent = new SyncAgent(clientProvider, serverProvider, options);

            Console.WriteLine("Starting synchronization with DisableProvisioning=true...");
            Console.WriteLine("(Provisioning steps will be skipped)");
            Console.WriteLine();

            // Perform sync
            var result = await agent.SynchronizeAsync(setup);

            Console.WriteLine("Synchronization completed!");
            Console.WriteLine();
            Console.WriteLine("Results:");
            Console.WriteLine($"  Total changes downloaded: {result.TotalChangesDownloaded}");
            Console.WriteLine($"  Total changes uploaded: {result.TotalChangesUploaded}");
            Console.WriteLine($"  Total changes applied: {result.TotalChangesApplied}");
            Console.WriteLine($"  Total duration: {result.CompleteTime}");
            Console.WriteLine();

            Console.WriteLine("✓ Sync completed successfully without provisioning!");
            Console.WriteLine("  - No CREATE operations were performed");
            Console.WriteLine("  - All objects were already in place");
            Console.WriteLine("  - Database user only needs SELECT/INSERT/UPDATE/DELETE/EXECUTE permissions");
        }

        /// <summary>
        /// Helper method to apply SQL scripts to a database
        /// </summary>
        private static async Task ApplyScriptAsync(string connectionString, string scriptPath)
        {
            if (!File.Exists(scriptPath))
            {
                Console.WriteLine($"Warning: Script file not found: {scriptPath}");
                return;
            }

            var script = await File.ReadAllTextAsync(scriptPath);

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // Split by GO statements and execute each batch
            var batches = script.Split(new[] { "\nGO\n", "\nGO\r\n", "\r\nGO\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var batch in batches)
            {
                var trimmedBatch = batch.Trim();

                // Skip empty batches and comment-only batches
                if (string.IsNullOrWhiteSpace(trimmedBatch) ||
                    trimmedBatch.StartsWith("--") ||
                    trimmedBatch.StartsWith("/*"))
                    continue;

                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = trimmedBatch;
                    await command.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    // Only show errors for non-comment batches
                    if (!trimmedBatch.TrimStart().StartsWith("--"))
                    {
                        Console.WriteLine($"Warning executing batch: {ex.Message}");
                        Console.WriteLine($"Batch preview: {trimmedBatch.Substring(0, Math.Min(100, trimmedBatch.Length))}...");
                    }
                }
            }
        }
    }
}
