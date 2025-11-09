using Dotmim.Sync.Enumerations;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Tests.Core;
using Dotmim.Sync.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Dotmim.Sync.Tests.UnitTests
{
    /// <summary>
    /// Tests for DisableProvisioning feature.
    /// </summary>
    public partial class DisableProvisioningTests : DatabaseTest, IClassFixture<DatabaseServerFixture>, IDisposable
    {
        private CoreProvider serverProvider;
        private CoreProvider clientProvider;
        private SyncSetup setup;

        public DisableProvisioningTests(ITestOutputHelper output, DatabaseServerFixture fixture) : base(output, fixture)
        {
            serverProvider = GetServerProvider();
            var clientsProvider = GetClientProviders();
            clientProvider = clientsProvider.First();
            setup = GetSetup();
        }

        [Fact]
        public void DisableProvisioning_Option_ShouldDefaultToFalse()
        {
            var options = new SyncOptions();
            Assert.False(options.DisableProvisioning);
        }

        [Fact]
        public void DisableProvisioning_Option_ShouldBeSettable()
        {
            var options = new SyncOptions { DisableProvisioning = true };
            Assert.True(options.DisableProvisioning);
        }

        [Fact]
        public async Task DisableProvisioning_RemoteOrchestrator_ShouldSkipProvisioningWhenEnabled()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_dp_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var options = new SyncOptions { DisableProvisioning = true };
            var setup = new SyncSetup("SalesLT.Product");
            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider, options);

            var provisioningCalled = false;
            var provisionedCalled = false;

            // Intercept provisioning events
            remoteOrchestrator.OnProvisioning(args =>
            {
                provisioningCalled = true;
            });

            remoteOrchestrator.OnProvisioned(args =>
            {
                provisionedCalled = true;
                // When DisableProvisioning is true, nothing should be created
                Assert.False(args.AtLeastSomethingHasBeenCreated);
            });

            // Get scope info (this will load schema)
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(setup: setup);

            // Try to provision - should skip but still fire events
            var result = await remoteOrchestrator.ProvisionAsync(scopeInfo);

            // Events should still fire
            Assert.True(provisioningCalled);
            Assert.True(provisionedCalled);

            // Verify no tracking tables were created
            using var connection = new SqlConnection(cs);
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*)
                FROM sys.tables
                WHERE name LIKE '%_tracking'";
            var count = (int)await command.ExecuteScalarAsync();

            Assert.Equal(0, count); // No tracking tables should exist

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task DisableProvisioning_LocalOrchestrator_ShouldSkipProvisioningWhenEnabled()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_dp_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var options = new SyncOptions { DisableProvisioning = true };
            var setup = new SyncSetup("SalesLT.Product");
            var provider = new SqlSyncProvider(cs);
            var localOrchestrator = new LocalOrchestrator(provider, options);

            var provisioningCalled = false;
            var provisionedCalled = false;

            localOrchestrator.OnProvisioning(args =>
            {
                provisioningCalled = true;
            });

            localOrchestrator.OnProvisioned(args =>
            {
                provisionedCalled = true;
                Assert.False(args.AtLeastSomethingHasBeenCreated);
            });

            // Create a fake server scope info
            var serverScopeInfo = new ScopeInfo
            {
                Name = "DefaultScope",
                Setup = setup,
                Schema = new SyncSet(setup.Tables.Select(t => new SyncTable(t.TableName, t.SchemaName)).ToList())
            };

            // Add columns to schema (simplified for test)
            foreach (var table in serverScopeInfo.Schema.Tables)
            {
                table.Columns.Add(new SyncColumn("Id"));
            }

            // Try to provision - should skip
            var result = await localOrchestrator.ProvisionAsync(serverScopeInfo);

            Assert.True(provisioningCalled);
            Assert.True(provisionedCalled);

            // Verify no tracking tables were created
            using var connection = new SqlConnection(cs);
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*)
                FROM sys.tables
                WHERE name LIKE '%_tracking'";
            var count = (int)await command.ExecuteScalarAsync();

            Assert.Equal(0, count);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task DisableProvisioning_ShouldStillAllowSync_WithPreProvisionedObjects()
        {
            // This test verifies that sync works with pre-provisioned objects
            var serverDbName = HelperDatabase.GetRandomName("tcp_dp_srv_");
            var clientDbName = HelperDatabase.GetRandomName("tcp_dp_cli_");

            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, serverDbName, true);
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, clientDbName, true);

            var serverCs = HelperDatabase.GetConnectionString(ProviderType.Sql, serverDbName);
            var clientCs = HelperDatabase.GetConnectionString(ProviderType.Sql, clientDbName);

            var setup = new SyncSetup("SalesLT.Product");
            var serverProvider = new SqlSyncProvider(serverCs);
            var clientProvider = new SqlSyncProvider(clientCs);

            // Step 1: Provision server normally (with full permissions)
            var serverOptions = new SyncOptions();
            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, serverOptions);
            await remoteOrchestrator.ProvisionAsync(setup);

            // Step 2: Provision client normally (to create all objects)
            var serverScope = await remoteOrchestrator.GetScopeInfoAsync();
            var clientOptions = new SyncOptions();
            var localOrchestrator = new LocalOrchestrator(clientProvider, clientOptions);
            await localOrchestrator.ProvisionAsync(serverScope);

            // Step 3: Now simulate re-running with DisableProvisioning = true
            // Create a new orchestrator with DisableProvisioning
            var clientOptionsWithDisableProvision = new SyncOptions { DisableProvisioning = true };
            var localOrchestratorWithDisabledProvision = new LocalOrchestrator(clientProvider, clientOptionsWithDisableProvision);

            // This should succeed because objects already exist
            var result = await localOrchestratorWithDisabledProvision.ProvisionAsync(serverScope);
            Assert.NotNull(result);

            // Sync should work
            var agent = new SyncAgent(clientProvider, remoteOrchestrator, clientOptionsWithDisableProvision);
            var syncResult = await agent.SynchronizeAsync();
            Assert.NotNull(syncResult);

            HelperDatabase.DropDatabase(ProviderType.Sql, serverDbName);
            HelperDatabase.DropDatabase(ProviderType.Sql, clientDbName);
        }

        [Fact]
        public async Task DisableProvisioning_Deprovision_ShouldAlsoBeSkipped()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_dp_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var options = new SyncOptions { DisableProvisioning = true };
            var setup = new SyncSetup("SalesLT.Product");
            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider, options);

            var deprovisioningCalled = false;
            var deprovisionedCalled = false;

            remoteOrchestrator.OnDeprovisioning(args =>
            {
                deprovisioningCalled = true;
            });

            remoteOrchestrator.OnDeprovisioned(args =>
            {
                deprovisionedCalled = true;
                // Nothing should be dropped
                Assert.False(args.AtLeastSomethingHasBeenDropped);
            });

            // Try to deprovision - should skip
            await remoteOrchestrator.DeprovisionAsync();

            Assert.True(deprovisioningCalled);
            Assert.True(deprovisionedCalled);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task DisableProvisioning_False_ShouldProvisionNormally()
        {
            // Verify that DisableProvisioning = false works as normal
            var dbName = HelperDatabase.GetRandomName("tcp_dp_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            var options = new SyncOptions { DisableProvisioning = false }; // Explicitly false
            var setup = new SyncSetup("SalesLT.Product");
            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider, options);

            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(setup: setup);
            var result = await remoteOrchestrator.ProvisionAsync(scopeInfo);

            // Verify tracking tables WERE created
            using var connection = new SqlConnection(cs);
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*)
                FROM sys.tables
                WHERE name LIKE '%_tracking'";
            var count = (int)await command.ExecuteScalarAsync();

            Assert.True(count > 0); // Tracking tables should exist

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task DisableProvisioning_CanBeDifferentForClientAndServer()
        {
            // Verify that client and server can have different DisableProvisioning settings
            var serverDbName = HelperDatabase.GetRandomName("tcp_dp_srv_");
            var clientDbName = HelperDatabase.GetRandomName("tcp_dp_cli_");

            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, serverDbName, true);
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, clientDbName, true);

            var serverCs = HelperDatabase.GetConnectionString(ProviderType.Sql, serverDbName);
            var clientCs = HelperDatabase.GetConnectionString(ProviderType.Sql, clientDbName);

            var setup = new SyncSetup("SalesLT.Product");

            // Server: provision normally
            var serverOptions = new SyncOptions { DisableProvisioning = false };
            var serverProvider = new SqlSyncProvider(serverCs);
            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, serverOptions);
            await remoteOrchestrator.ProvisionAsync(setup);

            // Verify server has tracking tables
            using (var serverConnection = new SqlConnection(serverCs))
            {
                await serverConnection.OpenAsync();
                var command = serverConnection.CreateCommand();
                command.CommandText = @"
                    SELECT COUNT(*)
                    FROM sys.tables
                    WHERE name LIKE '%_tracking'";
                var count = (int)await command.ExecuteScalarAsync();
                Assert.True(count > 0);
            }

            // Client: skip provisioning (simulating limited permissions)
            var clientOptions = new SyncOptions { DisableProvisioning = true };
            var clientProvider = new SqlSyncProvider(clientCs);
            var localOrchestrator = new LocalOrchestrator(clientProvider, clientOptions);

            var serverScope = await remoteOrchestrator.GetScopeInfoAsync();
            await localOrchestrator.ProvisionAsync(serverScope);

            // Verify client does NOT have tracking tables
            using (var clientConnection = new SqlConnection(clientCs))
            {
                await clientConnection.OpenAsync();
                var command = clientConnection.CreateCommand();
                command.CommandText = @"
                    SELECT COUNT(*)
                    FROM sys.tables
                    WHERE name LIKE '%_tracking'";
                var count = (int)await command.ExecuteScalarAsync();
                Assert.Equal(0, count);
            }

            HelperDatabase.DropDatabase(ProviderType.Sql, serverDbName);
            HelperDatabase.DropDatabase(ProviderType.Sql, clientDbName);
        }

        public void Dispose()
        {
            // Cleanup
        }
    }
}
