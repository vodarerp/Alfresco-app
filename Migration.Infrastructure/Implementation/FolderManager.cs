using Alfresco.Contracts.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Service for managing physical folders on filesystem.
    /// Handles creation of folder structure: ROOT -> dosie-{ClientType} -> {ClientType}{CoreId}
    /// </summary>
    public class FolderManager : IFolderManager
    {
        private readonly IFolderPathService _pathService;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;
        private readonly MigrationOptions _options;

        public FolderManager(
            IFolderPathService pathService,
            IOptions<MigrationOptions> options,
            ILoggerFactory logger)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _fileLogger = logger.CreateLogger("FileLogger");
            _dbLogger = logger.CreateLogger("DbLogger");
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

            if (string.IsNullOrWhiteSpace(_options.RootDocumentPath))
            {
                throw new InvalidOperationException(
                    "RootDocumentPath is not configured in MigrationOptions. " +
                    "Please set Migration:RootDocumentPath in appsettings.json or environment variable.");
            }
        }

        public async Task<string> EnsureFolderStructureAsync(string clientType, string coreId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientType))
            {
                throw new ArgumentException("Client type cannot be null or empty", nameof(clientType));
            }

            if (string.IsNullOrWhiteSpace(coreId))
            {
                throw new ArgumentException("CoreId cannot be null or empty", nameof(coreId));
            }

            try
            {
                var fullPath = GetClientFolderPath(clientType, coreId);

                if (Directory.Exists(fullPath))
                {
                    _fileLogger.LogDebug("Folder structure already exists: {Path}", fullPath);
                    return fullPath;
                }

                // Create the complete folder structure
                // This will create both dosie-{ClientType} and {ClientType}{CoreId} folders if needed
                await Task.Run(() => Directory.CreateDirectory(fullPath), ct).ConfigureAwait(false);

                _fileLogger.LogInformation(
                    "Created folder structure for {ClientType} client {CoreId}: {Path}",
                    clientType, coreId, fullPath);

                return fullPath;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Failed to create folder structure for {ClientType} client {CoreId}",
                    clientType, coreId);
                _dbLogger.LogError(ex,
                    "Failed to create folder structure for {ClientType} client {CoreId}",
                    clientType, coreId);
                throw new InvalidOperationException(
                    $"Failed to create folder structure for {clientType} client {coreId}", ex);
            }
        }

        public bool FolderStructureExists(string clientType, string coreId)
        {
            if (string.IsNullOrWhiteSpace(clientType) || string.IsNullOrWhiteSpace(coreId))
            {
                return false;
            }

            try
            {
                var fullPath = GetClientFolderPath(clientType, coreId);
                return Directory.Exists(fullPath);
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(ex,
                    "Error checking folder structure existence for {ClientType} client {CoreId}",
                    clientType, coreId);
                return false;
            }
        }

        public string GetClientFolderPath(string clientType, string coreId)
        {
            if (string.IsNullOrWhiteSpace(clientType))
            {
                throw new ArgumentException("Client type cannot be null or empty", nameof(clientType));
            }

            if (string.IsNullOrWhiteSpace(coreId))
            {
                throw new ArgumentException("CoreId cannot be null or empty", nameof(coreId));
            }

            // Generate relative path using FolderPathService
            var relativePath = _pathService.GenerateFolderPath(clientType, coreId);

            // Combine with root path to get full physical path
            var fullPath = Path.Combine(_options.RootDocumentPath!, relativePath);

            return fullPath;
        }
    }
}
