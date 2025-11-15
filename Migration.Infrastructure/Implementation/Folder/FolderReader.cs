using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Request;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Folder
{
    public class FolderReader : IFolderReader
    {
        private readonly IAlfrescoReadApi _read;
        private readonly IAlfrescoDbReader? _dbReader;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;

        public FolderReader(IAlfrescoReadApi read, ILoggerFactory logger, IAlfrescoDbReader? dbReader = null)
        {
            _read = read;
            _dbReader = dbReader;
            _fileLogger = logger.CreateLogger("FileLogger");
            _dbLogger = logger.CreateLogger("DbLogger");
        }

        //public sealed record FolderReaderRequest(string RootId, string NameFilter, int Skip, int Take);
        public async Task<FolderReaderResult> ReadBatchAsync(FolderReaderRequest inRequest, CancellationToken ct)
        {
            _fileLogger.LogDebug("Reading folders using AFTS from root {RootId} with filter '{NameFilter}', Take: {Take}",
                inRequest.RootId, inRequest.NameFilter, inRequest.Take);

            // Build AFTS query (Alfresco Full Text Search - Lucene syntax)
            var query = new StringBuilder();

            // Parent constraint
            var safeRootId = SanitizeAFTS(inRequest.RootId);
            query.Append($"PARENT:\"{safeRootId}\"");

            // Type constraint
            query.Append(" AND TYPE:\"cm:folder\"");

            // Name filter (if specified)
            if (!string.IsNullOrWhiteSpace(inRequest.NameFilter))
            {
                var safeName = SanitizeAFTS(inRequest.NameFilter);
                query.Append($" AND cm:name:\"*{safeName}*\"");
            }

            // CoreId filtering (if specified)
            if (inRequest.TargetCoreIds != null && inRequest.TargetCoreIds.Count > 0)
            {
                query.Append(" AND (");
                for (int i = 0; i < inRequest.TargetCoreIds.Count; i++)
                {
                    if (i > 0)
                        query.Append(" OR ");

                    var safeCoreId = SanitizeAFTS(inRequest.TargetCoreIds[i]);
                    // Match folder names containing the CoreId
                    // Format: {Type}-{CoreId}TTT (e.g., PL-10000003TTT)
                    query.Append($"cm:name:\"*-{safeCoreId}*\"");
                }
                query.Append(")");
            }

            // Cursor filtering - composite key (createdAt + name) to avoid skipping folders
            if (inRequest.Cursor != null && !string.IsNullOrEmpty(inRequest.Cursor.LastObjectName))
            {
                var cursorDate = inRequest.Cursor.LastObjectCreated.UtcDateTime;
                var dateStr = cursorDate.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
                var safeCursorName = SanitizeAFTS(inRequest.Cursor.LastObjectName);

                // Query logic:
                // (created > cursorDate) OR (created = cursorDate AND name > cursorName)
                query.Append(" AND (");

                // Option 1: created > cursorDate
                query.Append($"(cm:created:>{dateStr})");

                // Option 2: created = cursorDate AND name > cursorName
                query.Append(" OR (");
                query.Append($"cm:created:{dateStr}");
                query.Append($" AND cm:name:>{safeCursorName}");
                query.Append(")");

                query.Append(")");
            }

            var req = new PostSearchRequest()
            {
                Query = new QueryRequest()
                {
                    Language = "afts",  // Changed from "cmis" to "afts"
                    Query = query.ToString()
                },
                Paging = new PagingRequest()
                {
                    MaxItems = inRequest.Take,
                    SkipCount = 0
                },
                Sort = new[]
                {
                    new SortRequest { Type = "FIELD", Field = "cm:created", Ascending = true },
                    new SortRequest { Type = "FIELD", Field = "cm:name", Ascending = true }  // Tie-breaker for same timestamp
                },
                Include = new[] { "properties" }
            };

            _fileLogger.LogDebug("AFTS Query: {Query}", query.ToString());

            var response = await _read.SearchAsync(req, ct).ConfigureAwait(false);
            var result = response?.List?.Entries ?? new List<ListEntry>();

            _fileLogger.LogDebug("AFTS query returned {Count} folders in root {RootId}", result.Count, inRequest.RootId);

            FolderSeekCursor? next = null;

            if (result.Count > 0)
            {
                var last = result[^1].Entry;

                // Build next cursor with composite key
                next = new FolderSeekCursor(
                    last.Id,
                    last.CreatedAt,
                    last.Name ?? string.Empty);  // Include name for tie-breaking
            }

            return new FolderReaderResult(Items: result, next);
        }

        /// <summary>
        /// Sanitizes input for AFTS (Lucene) queries to prevent injection attacks
        /// </summary>
        private string SanitizeAFTS(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            // AFTS (Lucene) special characters that need escaping:
            // + - && || ! ( ) { } [ ] ^ " ~ * ? : \
            return value
                .Replace("\\", "\\\\")   // Backslash first
                .Replace("\"", "\\\"")
                .Replace("+", "\\+")
                .Replace("-", "\\-")
                .Replace("&", "\\&")     // Escape & (part of &&)
                .Replace("|", "\\|")     // Escape | (part of ||)
                .Replace("!", "\\!")
                .Replace("(", "\\(")
                .Replace(")", "\\)")
                .Replace("{", "\\{")
                .Replace("}", "\\}")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace("^", "\\^")
                .Replace("~", "\\~")
                .Replace("*", "\\*")
                .Replace("?", "\\?")
                .Replace(":", "\\:");
        }

        public async Task<long> CountTotalFoldersAsync(string rootId, string nameFilter, CancellationToken ct)
        {
            // Option 1: Try direct DB query first (most accurate)
            if (_dbReader != null)
            {
                try
                {
                    var dbCount = await _dbReader.CountTotalFoldersAsync(rootId, nameFilter, ct).ConfigureAwait(false);
                    if (dbCount >= 0)
                    {
                        return dbCount;
                    }
                }
                catch
                {
                    // Fall through to CMIS attempt
                }
            }

            // Option 2: Try CMIS count query (may not be supported)
            var cmsLike = string.IsNullOrWhiteSpace(nameFilter) ? "" : nameFilter;
            var countQuery = new StringBuilder();
            countQuery.Append("SELECT cmis:objectId FROM cmis:folder ")
                      .Append($"WHERE IN_TREE('{rootId}') ");

            var req = new PostSearchRequest()
            {
                Query = new QueryRequest()
                {
                    Language = "cmis",
                    Query = countQuery.ToString()
                },
                Paging = new PagingRequest()
                {
                    MaxItems = 1,
                    SkipCount = 0
                }
            };

            try
            {
                var result = await _read.SearchAsync(req, ct).ConfigureAwait(false);

                if (result?.List?.Pagination?.TotalItems != null)
                {
                    return result.List.Pagination.TotalItems;
                }

                return -1; // Count not available
            }
            catch (Exception)
            {
                return -1; // Count not available
            }
        }

        public async Task<Dictionary<string, string>> FindDossierSubfoldersAsync(string rootId, List<string>? folderTypes, CancellationToken ct)
        {
            _fileLogger.LogDebug("Finding DOSSIER subfolders in root {RootId}", rootId);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allFolders = new List<ListEntry>();
            var skipCount = 0;
            const int pageSize = 100;
            const string dossierPrefix = "DOSSIER-";  // Fixed typo: was "DOSSIERS-"

            // Build AFTS query to find DOSSIER-* folders
            var safeRootId = SanitizeAFTS(rootId);
            var query = $"PARENT:\"{safeRootId}\" AND TYPE:\"cm:folder\" AND cm:name:\"{dossierPrefix}*\"";

            _fileLogger.LogDebug("AFTS Query for DOSSIER folders: {Query}", query);

            // Pagination loop to handle cases where there are > 100 DOSSIER types
            while (true)
            {
                var req = new PostSearchRequest()
                {
                    Query = new QueryRequest()
                    {
                        Language = "afts",  // Changed from "cmis" to "afts"
                        Query = query
                    },
                    Paging = new PagingRequest()
                    {
                        MaxItems = pageSize,
                        SkipCount = skipCount
                    },
                    Sort = new[]
                    {
                        new SortRequest { Type = "FIELD", Field = "cm:name", Ascending = true }
                    }
                };

                var response = await _read.SearchAsync(req, ct).ConfigureAwait(false);
                var folders = response?.List?.Entries ?? new List<ListEntry>();

                if (folders.Count == 0)
                    break;

                allFolders.AddRange(folders);

                _fileLogger.LogDebug("Retrieved {Count} DOSSIER folders (page {Page})", folders.Count, (skipCount / pageSize) + 1);

                if (folders.Count < pageSize)
                    break; // Last page

                skipCount += pageSize;
            }

            _fileLogger.LogDebug("Found total {Count} DOSSIER folders in root {RootId}", allFolders.Count, rootId);

            // Process all retrieved folders
            foreach (var folder in allFolders)
            {
                var folderName = folder.Entry?.Name;
                if (string.IsNullOrEmpty(folderName) || !folderName.StartsWith(dossierPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Extract type from folder name (e.g., "DOSSIER-PL" -> "PL")
                var type = folderName.Substring(dossierPrefix.Length);

                // If folderTypes is specified, filter by those types
                if (folderTypes != null && folderTypes.Count > 0)
                {
                    if (!folderTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                        continue;
                }

                // Add to result dictionary
                var folderId = $"workspace://SpacesStore/{folder.Entry?.Id}";
                result[type] = folderId;
                _fileLogger.LogDebug("Added DOSSIER-{Type} folder: {FolderId}", type, folderId);
            }

            _fileLogger.LogInformation("Found {Count} matching DOSSIER subfolders in root {RootId}", result.Count, rootId);

            return result;
        }
    }
}
