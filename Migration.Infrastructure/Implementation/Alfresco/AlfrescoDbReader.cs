using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Npgsql;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Alfresco
{
    /// <summary>
    /// Direct PostgreSQL reader for Alfresco database
    /// Note: Requires Npgsql package and Alfresco DB connection string in config
    /// </summary>
    public class AlfrescoDbReader : IAlfrescoDbReader
    {
        private readonly string? _connectionString;
        private readonly int _commandTimeoutSeconds;
        private readonly ILogger<AlfrescoDbReader> _logger;

        public AlfrescoDbReader(IOptions<AlfrescoDbOptions> options, ILogger<AlfrescoDbReader> logger)
        {
            _connectionString = options.Value?.ConnectionString;
            _commandTimeoutSeconds = options.Value?.CommandTimeoutSeconds ?? 120;
            _logger = logger;
        }

        public async Task<long> CountTotalFoldersAsync(string rootFolderUuid, string nameFilter, CancellationToken ct)
        {
            // If connection string not configured, return -1 (not available)
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                _logger.LogWarning("Alfresco DB connection string not configured, count unavailable");
                return -1;
            }

            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);

                var sql = @"
WITH RECURSIVE folder_hierarchy AS (
    SELECT id, uuid
    FROM alf_node
    WHERE uuid = @rootUuid

    UNION ALL

    SELECT n.id, n.uuid
    FROM alf_node n
    JOIN alf_child_assoc ca ON n.id = ca.child_node_id
    JOIN folder_hierarchy fh ON ca.parent_node_id = fh.id
    WHERE n.type_qname_id = (SELECT id FROM alf_qname WHERE local_name = 'folder')
)
SELECT COUNT(*) AS total_filtered_folders
FROM folder_hierarchy fh
JOIN alf_node_properties np ON fh.id = np.node_id
JOIN alf_qname q ON np.qname_id = q.id
WHERE q.local_name = 'name'
AND np.string_value LIKE @nameFilter";

                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@rootUuid", rootFolderUuid);
                command.Parameters.AddWithValue("@nameFilter", $"%{nameFilter}%");
                command.CommandTimeout = _commandTimeoutSeconds;

                var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);

                if (result != null && result != DBNull.Value)
                {
                    var count = Convert.ToInt64(result);
                    _logger.LogInformation("Alfresco DB count query returned: {Count} folders", count);
                    return count;
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to count folders from Alfresco DB");
                return -1;
            }
        }
    }

    /// <summary>
    /// Configuration options for Alfresco database connection
    /// </summary>
    public class AlfrescoDbOptions
    {
        public const string SectionName = "AlfrescoDatabase";

        /// <summary>
        /// PostgreSQL connection string to Alfresco database
        /// Example: "Host=localhost;Port=5432;Database=alfresco;Username=alfresco;Password=alfresco"
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Command timeout in seconds for database queries.
        /// Default: 120 seconds (2 minutes)
        /// </summary>
        public int CommandTimeoutSeconds { get; set; } = 120;
    }
}
