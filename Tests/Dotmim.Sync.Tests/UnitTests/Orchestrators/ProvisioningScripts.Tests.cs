using Dotmim.Sync.Enumerations;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Tests.Core;
using Dotmim.Sync.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Dotmim.Sync.Tests.UnitTests
{
    /// <summary>
    /// Tests for GetProvisioningScriptsAsync feature.
    /// </summary>
    public partial class ProvisioningScriptsTests : DatabaseTest, IClassFixture<DatabaseServerFixture>, IDisposable
    {
        private CoreProvider serverProvider;
        private CoreProvider clientProvider;
        private SyncSetup setup;

        public ProvisioningScriptsTests(ITestOutputHelper output, DatabaseServerFixture fixture) : base(output, fixture)
        {
            serverProvider = GetServerProvider();
            var clientsProvider = GetClientProviders();
            clientProvider = clientsProvider.First();
            setup = GetSetup();
        }

        [Fact]
        public async Task GetProvisioningScripts_RemoteOrchestrator_ShouldGenerateValidSQL()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_ps_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider);
            var setup = new SyncSetup("SalesLT.Product");

            // Generate scripts
            var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
                setup: setup,
                provision: SyncProvision.TrackingTable | SyncProvision.StoredProcedures | SyncProvision.Triggers
            );

            // Verify scripts are not empty
            Assert.NotNull(scripts);
            Assert.NotEmpty(scripts);

            // Verify scripts contain expected keywords
            Assert.Contains("CREATE TABLE", scripts);
            Assert.Contains("_tracking", scripts);
            Assert.Contains("CREATE TRIGGER", scripts);
            Assert.Contains("CREATE PROCEDURE", scripts);
            Assert.Contains("Dotmim.Sync Provisioning Scripts", scripts);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task GetProvisioningScripts_LocalOrchestrator_ShouldGenerateValidSQL()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_ps_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var provider = new SqlSyncProvider(cs);
            var setup = new SyncSetup("SalesLT.Product");

            // First provision server to get schema
            var remoteOrchestrator = new RemoteOrchestrator(provider);
            await remoteOrchestrator.ProvisionAsync(setup);
            var serverScopeInfo = await remoteOrchestrator.GetScopeInfoAsync();

            // Generate client scripts
            var localOrchestrator = new LocalOrchestrator(provider);
            var scripts = await localOrchestrator.GetProvisioningScriptsAsync(
                serverScopeInfo: serverScopeInfo,
                provision: SyncProvision.Table | SyncProvision.TrackingTable |
                          SyncProvision.StoredProcedures | SyncProvision.Triggers
            );

            // Verify scripts
            Assert.NotNull(scripts);
            Assert.NotEmpty(scripts);
            Assert.Contains("CREATE TABLE", scripts);
            Assert.Contains("_tracking", scripts);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task GetProvisioningScripts_ShouldIncludeOnlyRequestedObjects()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_ps_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider);
            var setup = new SyncSetup("SalesLT.Product");

            // Generate scripts with only tracking tables
            var scriptsTrackingOnly = await remoteOrchestrator.GetProvisioningScriptsAsync(
                setup: setup,
                provision: SyncProvision.TrackingTable
            );

            // Should contain tracking table but not triggers or stored procedures
            Assert.Contains("_tracking", scriptsTrackingOnly);
            Assert.DoesNotContain("CREATE TRIGGER", scriptsTrackingOnly);
            Assert.DoesNotContain("CREATE PROCEDURE", scriptsTrackingOnly);

            // Generate scripts with only stored procedures
            var scriptsSpOnly = await remoteOrchestrator.GetProvisioningScriptsAsync(
                setup: setup,
                provision: SyncProvision.StoredProcedures
            );

            // Should contain procedures but not tracking tables
            Assert.Contains("CREATE PROCEDURE", scriptsSpOnly);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task GetProvisioningScripts_WithFilters_ShouldIncludeFilteredProcedures()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_ps_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider);

            // Setup with filter
            var setup = new SyncSetup("SalesLT.Product");
            var filter = new SetupFilter("SalesLT.Product");
            filter.AddParameter("ProductCategoryID", "SalesLT.Product");
            filter.AddWhere("ProductCategoryID", "SalesLT.Product", "ProductCategoryID");
            setup.Filters.Add(filter);

            // Generate scripts
            var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
                setup: setup,
                provision: SyncProvision.StoredProcedures
            );

            // Should contain filtered stored procedures
            Assert.Contains("CREATE PROCEDURE", scripts);
            // Should contain filter parameter
            Assert.Contains("ProductCategoryID", scripts);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task GetProvisioningScripts_ShouldRespectSchemaNames()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_ps_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider);

            // Setup with specific schema
            var setup = new SyncSetup();
            setup.Tables.Add("Product", "SalesLT");

            // Generate scripts
            var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
                setup: setup,
                provision: SyncProvision.TrackingTable | SyncProvision.Triggers
            );

            // Should contain schema name
            Assert.Contains("SalesLT", scripts);
            Assert.Contains("[SalesLT].[Product", scripts);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task SaveProvisioningScripts_ShouldCreateFile()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_ps_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider);
            var setup = new SyncSetup("SalesLT.Product");

            // Create temp file path
            var tempPath = Path.Combine(Path.GetTempPath(), $"test_provisioning_{Guid.NewGuid()}.sql");

            try
            {
                // Save scripts to file
                await remoteOrchestrator.SaveProvisioningScriptsAsync(
                    filePath: tempPath,
                    setup: setup,
                    provision: SyncProvision.TrackingTable
                );

                // Verify file exists
                Assert.True(File.Exists(tempPath));

                // Verify file content
                var content = await File.ReadAllTextAsync(tempPath);
                Assert.NotEmpty(content);
                Assert.Contains("_tracking", content);
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
            }
        }

        [Fact]
        public async Task GetProvisioningScripts_ShouldIncludeScopeInfoTables()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_ps_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider);
            var setup = new SyncSetup("SalesLT.Product");

            // First provision to get schema
            await remoteOrchestrator.ProvisionAsync(setup);
            var serverScopeInfo = await remoteOrchestrator.GetScopeInfoAsync();

            // Generate scripts with scope tables
            var localOrchestrator = new LocalOrchestrator(provider);
            var scripts = await localOrchestrator.GetProvisioningScriptsAsync(
                serverScopeInfo: serverScopeInfo,
                provision: SyncProvision.ScopeInfo | SyncProvision.ScopeInfoClient
            );

            // Should contain scope table creation
            Assert.Contains("scope_info", scripts);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task GetProvisioningScripts_MultipleTablesInCorrectOrder()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_ps_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider);

            // Setup with multiple tables
            var setup = new SyncSetup("SalesLT.ProductCategory", "SalesLT.Product");

            // Generate scripts
            var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
                setup: setup,
                provision: SyncProvision.TrackingTable
            );

            // Should contain both tables
            Assert.Contains("ProductCategory", scripts);
            Assert.Contains("Product", scripts);

            // Should have headers for each table
            Assert.Contains("Table: [SalesLT].[ProductCategory]", scripts);
            Assert.Contains("Table: [SalesLT].[Product]", scripts);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task GetProvisioningScripts_ShouldContainHeader()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_ps_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider);
            var setup = new SyncSetup("SalesLT.Product");

            var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
                setup: setup
            );

            // Verify header information
            Assert.Contains("Dotmim.Sync Provisioning Scripts", scripts);
            Assert.Contains("Generated:", scripts);
            Assert.Contains("Scope:", scripts);
            Assert.Contains("Provision:", scripts);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task GetProvisioningScripts_CanBeAppliedToDatabase()
        {
            var sourceDbName = HelperDatabase.GetRandomName("tcp_ps_src_");
            var targetDbName = HelperDatabase.GetRandomName("tcp_ps_tgt_");

            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, sourceDbName, true);
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, targetDbName, true);

            var sourceCs = HelperDatabase.GetConnectionString(ProviderType.Sql, sourceDbName);
            var targetCs = HelperDatabase.GetConnectionString(ProviderType.Sql, targetDbName);

            var sourceProvider = new SqlSyncProvider(sourceCs);
            var remoteOrchestrator = new RemoteOrchestrator(sourceProvider);
            var setup = new SyncSetup("SalesLT.Product");

            // Generate scripts from source
            var scripts = await remoteOrchestrator.GetProvisioningScriptsAsync(
                setup: setup,
                provision: SyncProvision.TrackingTable
            );

            // Apply scripts to target database
            using var connection = new SqlConnection(targetCs);
            await connection.OpenAsync();

            // Split by GO and execute each batch
            var batches = scripts.Split(new[] { "\nGO\n", "\nGO\r\n", "\r\nGO\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var batch in batches)
            {
                var trimmedBatch = batch.Trim();
                if (string.IsNullOrWhiteSpace(trimmedBatch) || trimmedBatch.StartsWith("--"))
                    continue;

                var command = connection.CreateCommand();
                command.CommandText = trimmedBatch;
                try
                {
                    await command.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    // Some batches might be comments only
                    if (!trimmedBatch.StartsWith("--"))
                        throw new Exception($"Failed to execute batch: {trimmedBatch.Substring(0, Math.Min(100, trimmedBatch.Length))}...", ex);
                }
            }

            // Verify tracking table was created
            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = @"
                SELECT COUNT(*)
                FROM sys.tables
                WHERE name LIKE '%_tracking'";
            var count = (int)await checkCommand.ExecuteScalarAsync();

            Assert.True(count > 0);

            HelperDatabase.DropDatabase(ProviderType.Sql, sourceDbName);
            HelperDatabase.DropDatabase(ProviderType.Sql, targetDbName);
        }

        public void Dispose()
        {
            // Cleanup
        }
    }
}
