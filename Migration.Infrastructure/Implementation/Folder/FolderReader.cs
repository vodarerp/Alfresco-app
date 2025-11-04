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
            _fileLogger.LogDebug("Reading folders from root {RootId} with filter '{NameFilter}', Take: {Take}",
                inRequest.RootId, inRequest.NameFilter, inRequest.Take);

            var cmsLike = string.IsNullOrWhiteSpace(inRequest.NameFilter) ? "" : inRequest.NameFilter;
            var sb = new StringBuilder();

            sb.Append("SELECT * from cmis:folder ")
              .Append($"WHERE cmis:parentId = '{inRequest.RootId}' ")
              .Append($"AND cmis:name LIKE '%{cmsLike}%' ");

            // Add CoreId filtering if TargetCoreIds is specified
            if (inRequest.TargetCoreIds != null && inRequest.TargetCoreIds.Count > 0)
            {
                sb.Append("AND (");
                for (int i = 0; i < inRequest.TargetCoreIds.Count; i++)
                {
                    if (i > 0)
                        sb.Append(" OR ");

                    // Match folder names containing the CoreId
                    // Format: {Type}-{CoreId}TTT (e.g., PL-10000003TTT)
                    sb.Append($"cmis:name LIKE '%-{inRequest.TargetCoreIds[i]}%'");
                }
                sb.Append(") ");
            }

            if (inRequest.Cursor is not null && !string.IsNullOrEmpty(inRequest.Cursor.LastObjectId))
            {
                var ld = inRequest.Cursor.LastObjectCreated.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
                sb.Append($"AND cmis:creationDate > TIMESTAMP '{ld}' ");
            }

            sb.Append("ORDER BY cmis:creationDate ASC ");
            //if (inRequest.Cursor is not null && !string.IsNullOrEmpty(inRequest.Cursor.LastObjectId) && inRequest?.Cursor.LastCreatedAt != null)
            //{
            //    var ld = inRequest.Cursor.LastCreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            //    sb.Append("AND (cmis:creationDate > TIMESTAMP '").Append(ld).Append("' ")
            //      .Append("OR (cmis:creationDate = TIMESTAMP '").Append(ld).Append("' ")
            //      .Append($"AND cmis:objectId > '{inRequest.Cursor.LastObjectId}' )) ");
            //}

            //sb.Append("ORDER BY cmis:creationDate ASC, cmis:objectId ASC");

            //Query = $"SELECT * FROM cmis:folder WHERE cmis:parentId = '{inRequest.RootId}' and cmis:name LIKE '%{cmsLike}%' order by cmis:name" //IN_TREE('<id>') umose parentId = ''

            var req = new PostSearchRequest()
            {
                Query = new QueryRequest()
                {
                    Language = "cmis",
                    Query = sb.ToString()
                },
                Paging = new PagingRequest()
                {
                    MaxItems = inRequest.Take,
                    SkipCount = 0
                },
                Sort = null,
                Include = new string[] { "properties" }
            };

            var result = (await _read.SearchAsync(req, ct).ConfigureAwait(false)).List?.Entries ?? new List<ListEntry>();

            _fileLogger.LogDebug("Found {Count} folders in root {RootId}", result.Count, inRequest.RootId);

            FolderSeekCursor? next = null;

            if ( result != null && result.Count > 0)
            {
                var last = result[^1].Entry;
                var lastId = $"workspace://SpacesStore/{last.Id}";
                next = new FolderSeekCursor(last.Id,last.CreatedAt);
            }

            return new FolderReaderResult(Items: result, next);
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

            // Build query to find DOSSIER-* folders
            var sb = new StringBuilder();
            sb.Append("SELECT * FROM cmis:folder ")
              .Append($"WHERE cmis:parentId = '{rootId}' ")
              .Append("AND cmis:name LIKE 'DOSSIERS-%' ");

            var req = new PostSearchRequest()
            {
                Query = new QueryRequest()
                {
                    Language = "cmis",
                    Query = sb.ToString()
                },
                Paging = new PagingRequest()
                {
                    MaxItems = 100, // Should be enough for all DOSSIER types
                    SkipCount = 0
                },
                Sort = null
            };

            var folders = (await _read.SearchAsync(req, ct).ConfigureAwait(false)).List?.Entries ?? new List<ListEntry>();

            _fileLogger.LogDebug("Found {Count} DOSSIER folders in root {RootId}", folders.Count, rootId);

            foreach (var folder in folders)
            {
                var folderName = folder.Entry?.Name;
                if (string.IsNullOrEmpty(folderName) || !folderName.StartsWith("DOSSIERS-", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Extract type from folder name (e.g., "DOSSIER-PL" -> "PL")
                var type = folderName.Substring("DOSSIERS-".Length);

                // If folderTypes is specified, filter by those types
                if (folderTypes != null && folderTypes.Count > 0)
                {
                    if (!folderTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                        continue;
                }

                // Add to result dictionary
                var folderId = $"workspace://SpacesStore/{folder.Entry?.Id}";
                result[type] = folderId;
                _fileLogger.LogDebug("Added DOSSIERS-{Type} folder: {FolderId}", type, folderId);
            }

            _fileLogger.LogInformation("Found {Count} matching DOSSIER subfolders in root {RootId}", result.Count, rootId);

            return result;
        }
    }
}
