using Alfresco.Abstraction.Interfaces;
using Alfresco.Abstraction.Models;
using Alfresco.Contracts.Request;
using Alfresco.Contracts.Response;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Alfresco.Client.Implementation
{
    public class AlfrescoReadApi : IAlfrescoReadApi
    {
        private readonly HttpClient _client;
        private readonly AlfrescoOptions _options;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;

        public AlfrescoReadApi(HttpClient client, IOptions<AlfrescoOptions> options, ILoggerFactory logger)
        {
            _client = client;
            _options = options.Value;
            _fileLogger = logger.CreateLogger("FileLogger");
            _dbLogger = logger.CreateLogger("DbLogger");
        }

        /// <summary>
        /// Gets a folder by its relative path
        /// </summary>
        /// <exception cref="AlfrescoTimeoutException">Thrown when operation times out after all retries</exception>
        /// <exception cref="AlfrescoRetryExhaustedException">Thrown when all retry attempts are exhausted</exception>
        public async Task<string> GetFolderByRelative(string inNodeId, string inRelativePath, CancellationToken ct = default)
        {
            var toRet = string.Empty;

            try
            {
                // Escape folder name for AFTS query (handle special characters)
                var escapedFolderName = inRelativePath.Replace("\"", "\\\"");

                // Build AFTS query:
                // - TYPE:"cm:folder" = only folders
                // - PARENT:"{parentId}" = only direct children of parent folder
                // - =cm:name:"{folderName}" = exact match on folder name
                var query = $"TYPE:\"cm:folder\" AND PARENT:\"{inNodeId}\" AND =cm:name:\"{escapedFolderName}\"";

                var searchRequest = new PostSearchRequest
                {
                    Query = new QueryRequest
                    {
                        Language = "afts",
                        Query = query
                    },
                    Paging = new PagingRequest
                    {
                        MaxItems = 1, // We only need the first result
                        SkipCount = 0
                    },
                    Include = null // Don't include extra data, just need the ID
                };

                // Execute search
                var searchResult = await SearchAsync(searchRequest, ct).ConfigureAwait(false);

                // Extract folder ID from search results
                if (searchResult?.List?.Entries != null && searchResult.List.Entries.Count > 0)
                {
                    var firstEntry = searchResult.List.Entries[0];
                    if (firstEntry?.Entry?.IsFolder == true)
                    {
                        toRet = firstEntry.Entry.Id;
                    }
                }
            }
            catch (AlfrescoTimeoutException timeoutEx)
            {
                _fileLogger.LogError(
                    "⏱️ TIMEOUT: GetFolderByRelative - ParentId: {ParentId}, Path: {Path}, Timeout: {Timeout}s",
                    inNodeId, inRelativePath, timeoutEx.TimeoutDuration.TotalSeconds);
                throw;
            }
            catch (AlfrescoRetryExhaustedException retryEx)
            {
                _fileLogger.LogError(
                    "❌ RETRY EXHAUSTED: GetFolderByRelative - ParentId: {ParentId}, Path: {Path}, Retries: {RetryCount}",
                    inNodeId, inRelativePath, retryEx.RetryCount);
                throw;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("[{Method}] Error in GetFolderByRelative - ParentId: {ParentId}, Path: {Path} - {ErrorType}: {Message}",
                    nameof(GetFolderByRelative), inNodeId, inRelativePath, ex.GetType().Name, ex.Message);
                _dbLogger.LogError(ex, "[{Method}] Error in GetFolderByRelative - ParentId: {ParentId}, Path: {Path}",
                    nameof(GetFolderByRelative), inNodeId, inRelativePath);
                // Return empty string to maintain backward compatibility
            }

            return toRet;
        }

        /// <summary>
        /// Gets all children of a node
        /// </summary>
        /// <exception cref="AlfrescoTimeoutException">Thrown when operation times out after all retries</exception>
        /// <exception cref="AlfrescoRetryExhaustedException">Thrown when all retry attempts are exhausted</exception>
        /// <exception cref="AlfrescoException">Thrown when the response is not successful</exception>
        public async Task<NodeChildrenResponse> GetNodeChildrenAsync(string nodeId, CancellationToken ct = default)
        {
            try
            {
                using var getResponse = await _client.GetAsync($"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}/children?include=properties", ct).ConfigureAwait(false);
                var body = await getResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!getResponse.IsSuccessStatusCode)
                    throw new AlfrescoException("Neuspešan odgovor pri čitanju root čvora.", (int)getResponse.StatusCode, body);

                var toRet = JsonConvert.DeserializeObject<NodeChildrenResponse>(body);
                return toRet;
            }
            catch (AlfrescoTimeoutException timeoutEx)
            {
                _fileLogger.LogError(
                    "⏱️ TIMEOUT: GetNodeChildren - NodeId: {NodeId}, Timeout: {Timeout}s",
                    nodeId, timeoutEx.TimeoutDuration.TotalSeconds);
                throw;
            }
            catch (AlfrescoRetryExhaustedException retryEx)
            {
                _fileLogger.LogError(
                    "❌ RETRY EXHAUSTED: GetNodeChildren - NodeId: {NodeId}, Retries: {RetryCount}",
                    nodeId, retryEx.RetryCount);
                throw;
            }
        }

        public async Task<NodeChildrenResponse> GetNodeChildrenAsync(string nodeId, int skipCount, int maxItems, CancellationToken ct = default)
        {
            using var getResponse = await _client.GetAsync(
                $"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}/children?include=properties&skipCount={skipCount}&maxItems={maxItems}",
                ct).ConfigureAwait(false);

            var body = await getResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!getResponse.IsSuccessStatusCode)
                throw new AlfrescoException($"Neuspešan odgovor pri čitanju čvora sa paginacijom. NodeId: {nodeId}, SkipCount: {skipCount}, MaxItems: {maxItems}",
                    (int)getResponse.StatusCode, body);

            var toRet = JsonConvert.DeserializeObject<NodeChildrenResponse>(body);
            return toRet;
        }

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            //using var response = await _client.GetAsync("/alfresco/api/-default-/public/alfresco/versions/1/probes/-live-", ct).ConfigureAwait(false);
            using var response = await _client.GetAsync("/alfresco/api/discovery", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        //{
        //    using var toRet = await _client.GetAsync("/alfresco/api/-default-/public/alfresco/versions/1/probes/-live-", ct);

        //    return toRet.IsSuccessStatusCode;
        //}

        /// <summary>
        /// Executes a search query against Alfresco
        /// </summary>
        /// <exception cref="AlfrescoTimeoutException">Thrown when operation times out after all retries</exception>
        /// <exception cref="AlfrescoRetryExhaustedException">Thrown when all retry attempts are exhausted</exception>
        public async Task<NodeChildrenResponse> SearchAsync(PostSearchRequest inRequest, CancellationToken ct = default)
        {
            try
            {
                // DIAGNOSTIKA: Proveri da li je CancellationToken već canceled PRE HTTP poziva
                if (ct.IsCancellationRequested)
                {
                    _fileLogger.LogWarning(
                        "⚠️ DIJAGNOSTIKA: CancellationToken JE VEĆ CANCELED pre HTTP search poziva! " +
                        "Query: {Query}. Ovo ukazuje na eksterni cancellation (SQL, Worker stop, itd.)",
                        inRequest?.Query?.Query);
                }

                var jsonSerializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                };

                var json = JsonConvert.SerializeObject(inRequest, jsonSerializerSettings);

                using var bodyRequest = new StringContent(json, Encoding.UTF8, "application/json");

                // DIAGNOSTIKA: Log start vremena
                var startTime = DateTime.UtcNow;
                _fileLogger.LogDebug("🔍 Starting Alfresco search: {Query}", inRequest?.Query?.Query);

                using var postResponse = await _client.PostAsync($"/alfresco/api/-default-/public/search/versions/1/search", bodyRequest, ct).ConfigureAwait(false);

                // DIAGNOSTIKA: Log trajanje poziva
                var elapsed = DateTime.UtcNow - startTime;
                _fileLogger.LogDebug("✅ Alfresco search completed in {Elapsed}s", elapsed.TotalSeconds);

                var stringResponse = await postResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var toRet = JsonConvert.DeserializeObject<NodeChildrenResponse>(stringResponse);

                return toRet;
            }
            catch (AlfrescoTimeoutException timeoutEx)
            {
                _fileLogger.LogError(
                    "⏱️ TIMEOUT: Search - Query: {Query}, Language: {Language}, Timeout: {Timeout}s",
                    inRequest?.Query?.Query, inRequest?.Query?.Language, timeoutEx.TimeoutDuration.TotalSeconds);
                throw;
            }
            catch (AlfrescoRetryExhaustedException retryEx)
            {
                // DIAGNOSTIKA: Log CancellationToken status i inner exception details
                var isCanceled = ct.IsCancellationRequested ? "DA" : "NE";
                var innerExType = retryEx.LastException?.GetType().Name ?? "None";
                var innerInnerExType = retryEx.LastException?.InnerException?.GetType().Name ?? "None";

                _fileLogger.LogError(
                    "❌ RETRY EXHAUSTED: Search - Query: {Query}, Language: {Language}, Retries: {RetryCount}, " +
                    "CancellationToken canceled: {IsCanceled}, LastException: {ExType}, InnerException: {InnerType}",
                    inRequest?.Query?.Query, inRequest?.Query?.Language, retryEx.RetryCount, isCanceled, innerExType, innerInnerExType);
                throw;
            }
            catch (Exception ex)
            {
                // DIAGNOSTIKA: Log unexpected exceptions sa CT statusom
                var isCanceled = ct.IsCancellationRequested ? "DA" : "NE";
                var innerExType = ex.InnerException?.GetType().Name ?? "None";

                _fileLogger.LogError(ex,
                    "❌ SEARCH FAILED: Query: {Query}, ExceptionType: {ExType}, InnerException: {InnerType}, " +
                    "CancellationToken canceled: {IsCanceled}",
                    inRequest?.Query?.Query, ex.GetType().Name, innerExType, isCanceled);

                throw;
            }
        }

        public async Task<NodeResponse> GetNodeByIdAsync(string nodeId, CancellationToken ct = default)
        {
            // Remove workspace://SpacesStore/ prefix if present
            //var cleanNodeId = nodeId.Replace("workspace://SpacesStore/", "");

            using var getResponse = await _client.GetAsync(
                $"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}",
                ct).ConfigureAwait(false);

            var body = await getResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!getResponse.IsSuccessStatusCode)
            {
                throw new AlfrescoException(
                    $"Failed to get node with ID '{nodeId}'.",
                    (int)getResponse.StatusCode,
                    body);
            }

            var result = JsonConvert.DeserializeObject<NodeResponse>(body);
            return result ?? throw new AlfrescoException($"Failed to deserialize response for node '{nodeId}'.", 500, body);
        }

        public async Task<bool> FolderExistsAsync(string parentFolderId, string folderName, CancellationToken ct = default)
        {
            try
            {
                // Get children of the parent folder
                using var getResponse = await _client.GetAsync(
                    $"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentFolderId}/children?where=(isFolder=true)",
                    ct).ConfigureAwait(false);

                if (!getResponse.IsSuccessStatusCode)
                {
                    return false;
                }

                var body = await getResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var response = JsonConvert.DeserializeObject<NodeChildrenResponse>(body);

                if (response?.List?.Entries == null)
                {
                    return false;
                }

                // Check if any child folder has the matching name
                return response.List.Entries.Any(entry =>
                    entry.Entry?.IsFolder == true &&
                    string.Equals(entry.Entry.Name, folderName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public async Task<NodeResponse?> GetFolderByNameAsync(string parentFolderId, string folderName, CancellationToken ct = default)
        {
            try
            {
                // Get children of the parent folder with properties included
                using var getResponse = await _client.GetAsync(
                    $"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentFolderId}/children?where=(isFolder=true)&include=properties",
                    ct).ConfigureAwait(false);

                if (!getResponse.IsSuccessStatusCode)
                {
                    return null;
                }

                var body = await getResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var response = JsonConvert.DeserializeObject<NodeChildrenResponse>(body);

                if (response?.List?.Entries == null)
                {
                    return null;
                }

                // Find the folder with matching name
                var folderEntry = response.List.Entries.FirstOrDefault(entry =>
                    entry.Entry?.IsFolder == true &&
                    string.Equals(entry.Entry.Name, folderName, StringComparison.OrdinalIgnoreCase));

                if (folderEntry?.Entry == null)
                {
                    return null;
                }

                // Return as NodeResponse
                return new NodeResponse { Entry = folderEntry.Entry };
            }
            catch
            {
                return null;
            }
        }
    }
}
