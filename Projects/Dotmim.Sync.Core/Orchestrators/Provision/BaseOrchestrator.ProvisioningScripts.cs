using Dotmim.Sync.Builders;
using Dotmim.Sync.Enumerations;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync
{
    /// <summary>
    /// Contains methods to generate provisioning scripts.
    /// </summary>
    public abstract partial class BaseOrchestrator
    {
        /// <summary>
        /// Generates SQL scripts for provisioning database objects without executing them.
        /// Useful for creating migration files that can be applied through external deployment processes.
        /// </summary>
        /// <param name="scopeInfo">Scope info containing schema and setup information.</param>
        /// <param name="provision">Provisioning flags indicating which objects to script (default: TrackingTable | StoredProcedures | Triggers).</param>
        /// <param name="connection">Optional database connection.</param>
        /// <param name="transaction">Optional database transaction.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A string containing all SQL scripts separated by GO statements.</returns>
        public virtual async Task<string> GetProvisioningScriptsAsync(
            ScopeInfo scopeInfo,
            SyncProvision provision = SyncProvision.NotSet,
            DbConnection connection = null,
            DbTransaction transaction = null,
            IProgress<ProgressArgs> progress = null,
            CancellationToken cancellationToken = default)
        {
            var context = new SyncContext(Guid.NewGuid(), scopeInfo.Name);

            try
            {
                if (this.Provider == null)
                    throw new MissingProviderException(nameof(this.GetProvisioningScriptsAsync));

                if (scopeInfo == null)
                    throw new ArgumentNullException(nameof(scopeInfo));

                // Set default provision if not specified
                if (provision == SyncProvision.NotSet)
                    provision = SyncProvision.TrackingTable | SyncProvision.StoredProcedures | SyncProvision.Triggers;

                // If schema does not have tables, raise an exception
                if (scopeInfo.Schema == null || !scopeInfo.Schema.HasTables)
                {
                    // If we have setup but no schema, try to get schema
                    if (scopeInfo.Setup != null && scopeInfo.Setup.HasTables)
                    {
                        using var runner = await this.GetConnectionAsync(context, SyncMode.NoTransaction, SyncStage.Provisioning,
                            connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                        await using (runner.ConfigureAwait(false))
                        {
                            (context, scopeInfo.Schema) = await this.InternalGetSchemaAsync(context, scopeInfo.Setup,
                                runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        throw new MissingTablesException();
                    }
                }

                // Ensure we have columns
                if (scopeInfo.Schema.HasTables && !scopeInfo.Schema.HasColumns)
                {
                    using var runner = await this.GetConnectionAsync(context, SyncMode.NoTransaction, SyncStage.Provisioning,
                        connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                    await using (runner.ConfigureAwait(false))
                    {
                        (context, scopeInfo.Schema) = await this.InternalGetSchemaAsync(context, scopeInfo.Setup,
                            runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);
                    }
                }

                using var runner2 = await this.GetConnectionAsync(context, SyncMode.NoTransaction, SyncStage.Provisioning,
                    connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runner2.ConfigureAwait(false))
                {
                    return await this.InternalGetProvisioningScriptsAsync(scopeInfo, context, provision,
                        runner2.Connection, runner2.Transaction, progress, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex, "Error during GetProvisioningScriptsAsync");
            }
        }

        /// <summary>
        /// Internal method to generate provisioning scripts.
        /// </summary>
        internal virtual async Task<string> InternalGetProvisioningScriptsAsync(
            ScopeInfo scopeInfo,
            SyncContext context,
            SyncProvision provision,
            DbConnection connection,
            DbTransaction transaction,
            IProgress<ProgressArgs> progress,
            CancellationToken cancellationToken)
        {
            var scripts = new StringBuilder();

            // Header
            scripts.AppendLine("-- =====================================================");
            scripts.AppendLine("-- Dotmim.Sync Provisioning Scripts");
            scripts.AppendLine("-- =====================================================");
            scripts.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            scripts.AppendLine($"-- Scope: {scopeInfo.Name}");
            scripts.AppendLine($"-- Version: {scopeInfo.Version}");
            scripts.AppendLine($"-- Provision: {provision}");
            scripts.AppendLine("-- =====================================================");
            scripts.AppendLine();

            // Get database builder for scope tables
            var builder = this.Provider.GetDatabaseBuilder();
            var scopeBuilder = this.GetScopeBuilder(this.Options.ScopeInfoTableName);

            // Generate scope_info table if requested
            if (provision.HasFlag(SyncProvision.ScopeInfo))
            {
                scripts.AppendLine("-- =====================================================");
                scripts.AppendLine("-- Creating scope_info table");
                scripts.AppendLine("-- =====================================================");
                try
                {
                    var scopeInfoCommand = await scopeBuilder.GetCreateScopeInfoTableCommandAsync(connection, transaction).ConfigureAwait(false);
                    if (scopeInfoCommand != null)
                    {
                        scripts.AppendLine(scopeInfoCommand.CommandText);
                        scripts.AppendLine("GO");
                        scripts.AppendLine();
                    }
                }
                catch (Exception ex)
                {
                    scripts.AppendLine($"-- Error generating scope_info table: {ex.Message}");
                    scripts.AppendLine();
                }
            }

            // Generate scope_info_client table if requested
            if (provision.HasFlag(SyncProvision.ScopeInfoClient))
            {
                scripts.AppendLine("-- =====================================================");
                scripts.AppendLine("-- Creating scope_info_client table");
                scripts.AppendLine("-- =====================================================");
                try
                {
                    var scopeInfoClientCommand = await scopeBuilder.GetCreateScopeInfoClientTableCommandAsync(connection, transaction).ConfigureAwait(false);
                    if (scopeInfoClientCommand != null)
                    {
                        scripts.AppendLine(scopeInfoClientCommand.CommandText);
                        scripts.AppendLine("GO");
                        scripts.AppendLine();
                    }
                }
                catch (Exception ex)
                {
                    scripts.AppendLine($"-- Error generating scope_info_client table: {ex.Message}");
                    scripts.AppendLine();
                }
            }

            // Sort tables by dependencies
            var schemaTables = scopeInfo.Schema.Tables
                .SortByDependencies(tab => tab.GetRelations()
                    .Select(r => r.GetParentTable()));

            foreach (var schemaTable in schemaTables)
            {
                var tableBuilder = this.GetTableBuilder(schemaTable, scopeInfo);
                var filter = schemaTable.GetFilter();

                scripts.AppendLine("-- =====================================================");
                scripts.AppendLine($"-- Table: {schemaTable.GetFullName()}");
                scripts.AppendLine("-- =====================================================");
                scripts.AppendLine();

                // Generate schema creation if needed
                if (!string.IsNullOrEmpty(schemaTable.SchemaName))
                {
                    try
                    {
                        var schemaCommand = await tableBuilder.GetCreateSchemaCommandAsync(connection, transaction).ConfigureAwait(false);
                        if (schemaCommand != null)
                        {
                            scripts.AppendLine($"-- Creating schema {schemaTable.SchemaName}");
                            scripts.AppendLine(schemaCommand.CommandText);
                            scripts.AppendLine("GO");
                            scripts.AppendLine();
                        }
                    }
                    catch (Exception ex)
                    {
                        scripts.AppendLine($"-- Error generating schema: {ex.Message}");
                        scripts.AppendLine();
                    }
                }

                // Generate table creation script if requested
                if (provision.HasFlag(SyncProvision.Table))
                {
                    scripts.AppendLine($"-- Creating table {schemaTable.GetFullName()}");
                    try
                    {
                        var tableCommand = await tableBuilder.GetCreateTableCommandAsync(connection, transaction).ConfigureAwait(false);
                        if (tableCommand != null)
                        {
                            scripts.AppendLine(tableCommand.CommandText);
                            scripts.AppendLine("GO");
                            scripts.AppendLine();
                        }
                    }
                    catch (Exception ex)
                    {
                        scripts.AppendLine($"-- Error generating table: {ex.Message}");
                        scripts.AppendLine();
                    }
                }

                // Generate tracking table creation script if requested
                if (provision.HasFlag(SyncProvision.TrackingTable))
                {
                    scripts.AppendLine($"-- Creating tracking table for {schemaTable.GetFullName()}");
                    try
                    {
                        var trackingCommand = await tableBuilder.GetCreateTrackingTableCommandAsync(connection, transaction).ConfigureAwait(false);
                        if (trackingCommand != null)
                        {
                            scripts.AppendLine(trackingCommand.CommandText);
                            scripts.AppendLine("GO");
                            scripts.AppendLine();
                        }
                    }
                    catch (Exception ex)
                    {
                        scripts.AppendLine($"-- Error generating tracking table: {ex.Message}");
                        scripts.AppendLine();
                    }
                }

                // Generate triggers if requested
                if (provision.HasFlag(SyncProvision.Triggers))
                {
                    scripts.AppendLine($"-- Creating triggers for {schemaTable.GetFullName()}");

                    var triggerTypes = new[] { DbTriggerType.Insert, DbTriggerType.Update, DbTriggerType.Delete };
                    foreach (var triggerType in triggerTypes)
                    {
                        try
                        {
                            var triggerCommand = await tableBuilder.GetCreateTriggerCommandAsync(triggerType, connection, transaction).ConfigureAwait(false);
                            if (triggerCommand != null)
                            {
                                scripts.AppendLine($"-- {triggerType} trigger");
                                scripts.AppendLine(triggerCommand.CommandText);
                                scripts.AppendLine("GO");
                                scripts.AppendLine();
                            }
                        }
                        catch (Exception ex)
                        {
                            scripts.AppendLine($"-- Error generating {triggerType} trigger: {ex.Message}");
                            scripts.AppendLine();
                        }
                    }
                }

                // Generate stored procedures if requested
                if (provision.HasFlag(SyncProvision.StoredProcedures))
                {
                    scripts.AppendLine($"-- Creating stored procedures for {schemaTable.GetFullName()}");

                    // Determine which stored procedures to generate based on filters
                    var storedProcedureTypes = new List<DbStoredProcedureType>
                    {
                        DbStoredProcedureType.SelectChanges,
                        DbStoredProcedureType.SelectInitializedChanges,
                        DbStoredProcedureType.SelectRow,
                        DbStoredProcedureType.UpdateRow,
                        DbStoredProcedureType.DeleteRow,
                        DbStoredProcedureType.Reset,
                    };

                    // Add filtered versions if filter exists
                    if (filter != null)
                    {
                        storedProcedureTypes.Add(DbStoredProcedureType.SelectChangesWithFilters);
                        storedProcedureTypes.Add(DbStoredProcedureType.SelectInitializedChangesWithFilters);
                    }

                    // Add bulk operations if supported
                    storedProcedureTypes.Add(DbStoredProcedureType.BulkTableType);
                    storedProcedureTypes.Add(DbStoredProcedureType.BulkUpdateRows);
                    storedProcedureTypes.Add(DbStoredProcedureType.BulkDeleteRows);

                    foreach (var spType in storedProcedureTypes)
                    {
                        try
                        {
                            var spCommand = await tableBuilder.GetCreateStoredProcedureCommandAsync(spType, filter, connection, transaction).ConfigureAwait(false);
                            if (spCommand != null)
                            {
                                scripts.AppendLine($"-- {spType}");
                                scripts.AppendLine(spCommand.CommandText);
                                scripts.AppendLine("GO");
                                scripts.AppendLine();
                            }
                        }
                        catch
                        {
                            // Some stored procedure types may not be applicable for all providers
                            // Silently skip
                            continue;
                        }
                    }
                }

                scripts.AppendLine();
            }

            scripts.AppendLine("-- =====================================================");
            scripts.AppendLine("-- Provisioning scripts generation completed");
            scripts.AppendLine("-- =====================================================");

            return scripts.ToString();
        }
    }
}
