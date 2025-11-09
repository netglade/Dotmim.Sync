using Dotmim.Sync.Enumerations;
using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync
{
    /// <summary>
    /// Contains methods to generate provisioning scripts for server.
    /// </summary>
    public partial class RemoteOrchestrator : BaseOrchestrator
    {
        /// <summary>
        /// Generates SQL scripts for provisioning the server database objects.
        /// This method retrieves the server scope, gets the schema, and generates scripts for all required objects.
        /// </summary>
        /// <param name="scopeName">Scope name (default: "DefaultScope").</param>
        /// <param name="setup">Optional setup containing tables and filters. If null, retrieved from server scope.</param>
        /// <param name="provision">Provisioning flags (default: TrackingTable | StoredProcedures | Triggers).</param>
        /// <param name="connection">Optional database connection.</param>
        /// <param name="transaction">Optional database transaction.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A string containing all SQL scripts.</returns>
        public virtual async Task<string> GetProvisioningScriptsAsync(
            string scopeName = SyncOptions.DefaultScopeName,
            SyncSetup setup = null,
            SyncProvision provision = SyncProvision.NotSet,
            DbConnection connection = null,
            DbTransaction transaction = null,
            IProgress<ProgressArgs> progress = null,
            CancellationToken cancellationToken = default)
        {
            var context = new SyncContext(Guid.NewGuid(), scopeName);

            try
            {
                using var runner = await this.GetConnectionAsync(context, SyncMode.NoTransaction, SyncStage.Provisioning,
                    connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runner.ConfigureAwait(false))
                {
                    // Get or create scope
                    ScopeInfo sScopeInfo;
                    (context, sScopeInfo, _) = await this.InternalEnsureScopeInfoAsync(context, setup, false,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    if (sScopeInfo.Setup == null || sScopeInfo.Schema == null)
                        throw new MissingServerScopeTablesException(scopeName);

                    // Generate scripts
                    return await this.GetProvisioningScriptsAsync(sScopeInfo, provision,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex, "Error during GetProvisioningScriptsAsync");
            }
        }

        /// <summary>
        /// Generates SQL scripts and saves them to a file.
        /// </summary>
        /// <param name="filePath">Path where the SQL file will be saved.</param>
        /// <param name="scopeName">Scope name (default: "DefaultScope").</param>
        /// <param name="setup">Optional setup containing tables and filters.</param>
        /// <param name="provision">Provisioning flags (default: TrackingTable | StoredProcedures | Triggers).</param>
        /// <param name="connection">Optional database connection.</param>
        /// <param name="transaction">Optional database transaction.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public virtual async Task SaveProvisioningScriptsAsync(
            string filePath,
            string scopeName = SyncOptions.DefaultScopeName,
            SyncSetup setup = null,
            SyncProvision provision = SyncProvision.NotSet,
            DbConnection connection = null,
            DbTransaction transaction = null,
            IProgress<ProgressArgs> progress = null,
            CancellationToken cancellationToken = default)
        {
            var scripts = await this.GetProvisioningScriptsAsync(scopeName, setup, provision,
                connection, transaction, progress, cancellationToken).ConfigureAwait(false);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(filePath, scripts, cancellationToken).ConfigureAwait(false);
        }
    }
}
