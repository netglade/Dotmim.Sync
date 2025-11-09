using Dotmim.Sync.Enumerations;
using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync
{
    /// <summary>
    /// Contains methods to generate provisioning scripts for client.
    /// </summary>
    public partial class LocalOrchestrator : BaseOrchestrator
    {
        /// <summary>
        /// Generates SQL scripts for provisioning the client database objects.
        /// Requires a server ScopeInfo to generate client-compatible scripts.
        /// </summary>
        /// <param name="serverScopeInfo">Server scope info containing schema and setup.</param>
        /// <param name="provision">Provisioning flags (default: Table | TrackingTable | StoredProcedures | Triggers).</param>
        /// <param name="connection">Optional database connection.</param>
        /// <param name="transaction">Optional database transaction.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A string containing all SQL scripts.</returns>
        public virtual async Task<string> GetProvisioningScriptsAsync(
            ScopeInfo serverScopeInfo,
            SyncProvision provision = SyncProvision.NotSet,
            DbConnection connection = null,
            DbTransaction transaction = null,
            IProgress<ProgressArgs> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (serverScopeInfo == null)
                throw new ArgumentNullException(nameof(serverScopeInfo));

            if (serverScopeInfo.Setup == null)
                throw new ArgumentException("ServerScopeInfo must have a Setup", nameof(serverScopeInfo));

            if (serverScopeInfo.Schema == null)
                throw new ArgumentException("ServerScopeInfo must have a Schema", nameof(serverScopeInfo));

            // Set default provision for client if not specified
            if (provision == SyncProvision.NotSet)
                provision = SyncProvision.Table | SyncProvision.TrackingTable | SyncProvision.StoredProcedures | SyncProvision.Triggers;

            // Use the base method with server scope info
            return await base.GetProvisioningScriptsAsync(serverScopeInfo, provision,
                connection, transaction, progress, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Generates SQL scripts for client and saves them to a file.
        /// </summary>
        /// <param name="filePath">Path where the SQL file will be saved.</param>
        /// <param name="serverScopeInfo">Server scope info containing schema and setup.</param>
        /// <param name="provision">Provisioning flags (default: Table | TrackingTable | StoredProcedures | Triggers).</param>
        /// <param name="connection">Optional database connection.</param>
        /// <param name="transaction">Optional database transaction.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public virtual async Task SaveProvisioningScriptsAsync(
            string filePath,
            ScopeInfo serverScopeInfo,
            SyncProvision provision = SyncProvision.NotSet,
            DbConnection connection = null,
            DbTransaction transaction = null,
            IProgress<ProgressArgs> progress = null,
            CancellationToken cancellationToken = default)
        {
            var scripts = await this.GetProvisioningScriptsAsync(serverScopeInfo, provision,
                connection, transaction, progress, cancellationToken).ConfigureAwait(false);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(filePath, scripts, cancellationToken).ConfigureAwait(false);
        }
    }
}
