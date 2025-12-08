using Alfresco.Abstraction.Interfaces;
using Alfresco.Abstraction.Models;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Response;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Alfresco.Client.Implementation
{
    public class AlfrescoWriteApi : IAlfrescoWriteApi
    {
        private readonly HttpClient _client;
        private readonly AlfrescoOptions _options;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;

        public AlfrescoWriteApi(HttpClient client, IOptions<AlfrescoOptions> options, ILoggerFactory logger)
        {
            _client = client;
            _options = options.Value;
            _fileLogger = logger.CreateLogger("FileLogger");
            _dbLogger = logger.CreateLogger("DbLogger");
        }

        public async Task<string> CreateFileAsync(string parentFolderId, string newFileName, CancellationToken ct = default)
        {
            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };


            var body = new
            {
                name = newFileName,
                nodeType = "cm:content"
            };

            var json = JsonConvert.SerializeObject(body, jsonSerializerSettings);
            using var bodyRequest = new StringContent(json, Encoding.UTF8, "application/json");

            //var x = await bodyRequest.ReadAsStringAsync(); http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/nodes/67dbe2a3-aaf7-4ef0-9be2-a3aaf73ef0aa/children

            using var r = await _client.PostAsync($"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentFolderId}/children", bodyRequest, ct).ConfigureAwait(false);


            var tpRet = await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var toRet = JsonConvert.DeserializeObject<ListEntry>(tpRet, jsonSerializerSettings);

            return toRet?.Entry.Id ?? "";
        }

        public async Task<string> CreateFolderAsync(string parentFolderId, string newFolderName, CancellationToken ct = default)
        {
            return await CreateFolderAsync(parentFolderId, newFolderName, null, ct).ConfigureAwait(false);
        }

        public async Task<string> CreateFolderAsync(string parentFolderId, string newFolderName, Dictionary<string, object>? properties, CancellationToken ct = default)
        {
            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };

            // First attempt: Try with properties if provided
            if (properties != null && properties.Count > 0)
            {
                _fileLogger.LogDebug(
                    "Attempting to create folder '{FolderName}' with {PropertyCount} properties under parent '{ParentId}'",
                    newFolderName, properties.Count, parentFolderId);

                try
                {
                    var folderId = await CreateFolderInternalAsync(parentFolderId, newFolderName, properties, jsonSerializerSettings, ct).ConfigureAwait(false);

                    _fileLogger.LogInformation(
                        "Successfully created folder '{FolderName}' with properties. FolderId: {FolderId}",
                        newFolderName, folderId);

                    return folderId;
                }
                catch (AlfrescoPropertyException propEx)
                {
                    _fileLogger.LogWarning(
                        "Property error when creating folder '{FolderName}': {ErrorKey} - {BriefSummary} (LogId: {LogId}). " +
                        "Retrying without properties.",
                        newFolderName, propEx.ErrorKey, propEx.BriefSummary, propEx.LogId);

                    // Fallback: Retry without properties
                    return await CreateFolderInternalAsync(parentFolderId, newFolderName, null, jsonSerializerSettings, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _fileLogger.LogError("Unexpected error creating folder '{FolderName}' with properties. Retrying without properties.",
                        newFolderName);
                    _dbLogger.LogError(ex,
                        "Unexpected error creating folder '{FolderName}' with properties",
                        newFolderName);

                    // Fallback: Retry without properties
                    return await CreateFolderInternalAsync(parentFolderId, newFolderName, null, jsonSerializerSettings, ct).ConfigureAwait(false);
                }
            }
            else
            {
                // No properties, create normally
                _fileLogger.LogDebug(
                    "Creating folder '{FolderName}' without properties under parent '{ParentId}'",
                    newFolderName, parentFolderId);

                return await CreateFolderInternalAsync(parentFolderId, newFolderName, null, jsonSerializerSettings, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Internal method to actually create the folder with Alfresco API
        /// </summary>
        private async Task<string> CreateFolderInternalAsync(
            string parentFolderId,
            string newFolderName,
            Dictionary<string, object>? properties,
            JsonSerializerSettings jsonSerializerSettings,
            CancellationToken ct)
        {
            // Build body with optional properties
            dynamic body = new System.Dynamic.ExpandoObject();
            body.name = newFolderName;
            body.nodeType = "cm:folder";

            // Add custom properties if provided
            if (properties != null && properties.Count > 0)
            {
                body.properties = properties;
            }

            var json = JsonConvert.SerializeObject(body, jsonSerializerSettings);
            using var bodyRequest = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _client.PostAsync(
                $"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentFolderId}/children",
                bodyRequest,
                ct).ConfigureAwait(false);

            var responseContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Check for errors
            if (!response.IsSuccessStatusCode)
            {
                // Try to parse Alfresco error response
                try
                {
                    var errorResponse = JsonConvert.DeserializeObject<AlfrescoErrorResponse>(responseContent, jsonSerializerSettings);

                    if (errorResponse?.Error != null)
                    {
                        // Check if it's a property-related error
                        if (IsPropertyError(errorResponse))
                        {
                            throw new AlfrescoPropertyException(
                                $"Property error when creating folder '{newFolderName}': {errorResponse.Error.BriefSummary}",
                                errorResponse.Error.ErrorKey,
                                errorResponse.Error.BriefSummary,
                                errorResponse.Error.LogId);
                        }

                        // Other Alfresco error
                        throw new HttpRequestException(
                            $"Alfresco API error (Status: {response.StatusCode}): {errorResponse.Error.BriefSummary} " +
                            $"(ErrorKey: {errorResponse.Error.ErrorKey}, LogId: {errorResponse.Error.LogId})");
                    }
                }
                catch (AlfrescoPropertyException ex)
                {
                    // Re-throw property exceptions
                    throw;
                }
                catch (JsonException)
                {
                    // Could not parse error response, throw generic error
                    _fileLogger.LogWarning(
                        "Could not parse Alfresco error response. Status: {StatusCode}, Content: {Content}",
                        response.StatusCode, responseContent);
                }

                // Generic HTTP error
                response.EnsureSuccessStatusCode();
            }

            // Success - parse response
            var result = JsonConvert.DeserializeObject<ListEntry>(responseContent, jsonSerializerSettings);
            return result?.Entry.Id ?? throw new InvalidOperationException("Alfresco returned null folder ID");
        }

        public Task<bool> DeleteNodeAsync(string nodeId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> MoveDocumentAsync(string nodeId, string targetFolderId, string? newName, CancellationToken ct = default)
        {
            try
            {
                _fileLogger.LogDebug("Moving node {NodeId} to folder {TargetFolderId}", nodeId, targetFolderId);

                var jsonSerializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                };
                var body = new
                {
                    targetParentId = targetFolderId
                };
                var json = JsonConvert.SerializeObject(body, jsonSerializerSettings);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var res = await _client.PostAsync($"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}/move", content, ct).ConfigureAwait(false);

                if (res.IsSuccessStatusCode)
                {
                    _fileLogger.LogDebug("Successfully moved node {NodeId} to folder {TargetFolderId}", nodeId, targetFolderId);
                    return true;
                }

                // Handle error response
                var errorContent = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                _fileLogger.LogWarning(
                    "Failed to move node {NodeId} to folder {TargetFolderId}: {StatusCode} - {Error}",
                    nodeId, targetFolderId, res.StatusCode, errorContent);

                // Try to parse error response for better error details
                try
                {
                    var errorResponse = JsonConvert.DeserializeObject<AlfrescoErrorResponse>(errorContent);

                    if (errorResponse?.Error != null)
                    {
                        _fileLogger.LogError(
                            "Alfresco move error for node {NodeId}: {ErrorKey} - {BriefSummary} (LogId: {LogId})",
                            nodeId, errorResponse.Error.ErrorKey, errorResponse.Error.BriefSummary, errorResponse.Error.LogId);
                    }
                }
                catch (JsonException)
                {
                    _fileLogger.LogWarning("Could not parse error response for move operation on node {NodeId}", nodeId);
                }

                return false;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "Error moving node {NodeId} to folder {TargetFolderId}", nodeId, targetFolderId);
                throw;
            }
        }

        public async Task<bool> CopyDocumentAsync(string nodeId, string targetFolderId, string? newName, CancellationToken ct = default)
        {
            try
            {
                _fileLogger.LogDebug("Copying node {NodeId} to folder {TargetFolderId}", nodeId, targetFolderId);

                var jsonSerializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                };
                var body = new
                {
                    targetParentId = targetFolderId
                };
                var json = JsonConvert.SerializeObject(body, jsonSerializerSettings);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var res = await _client.PostAsync($"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}/copy", content, ct).ConfigureAwait(false);

                if (res.IsSuccessStatusCode)
                {
                    _fileLogger.LogDebug("Successfully copied node {NodeId} to folder {TargetFolderId}", nodeId, targetFolderId);
                    return true;
                }

                // Handle error response
                var errorContent = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                _fileLogger.LogWarning(
                    "Failed to copy node {NodeId} to folder {TargetFolderId}: {StatusCode} - {Error}",
                    nodeId, targetFolderId, res.StatusCode, errorContent);

                // Try to parse error response for better error details
                try
                {
                    var errorResponse = JsonConvert.DeserializeObject<AlfrescoErrorResponse>(errorContent);

                    if (errorResponse?.Error != null)
                    {
                        _fileLogger.LogError(
                            "Alfresco copy error for node {NodeId}: {ErrorKey} - {BriefSummary} (LogId: {LogId})",
                            nodeId, errorResponse.Error.ErrorKey, errorResponse.Error.BriefSummary, errorResponse.Error.LogId);
                    }
                }
                catch (JsonException)
                {
                    _fileLogger.LogWarning("Could not parse error response for copy operation on node {NodeId}", nodeId);
                }

                return false;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "Error copying node {NodeId} to folder {TargetFolderId}", nodeId, targetFolderId);
                throw;
            }
        }

        public async Task<bool> UpdateNodePropertiesAsync(string nodeId, Dictionary<string, object> properties, CancellationToken ct = default)
        {
            try
            {
                _fileLogger.LogDebug("Updating properties for node {NodeId} ({Count} properties)", nodeId, properties.Count);

                var jsonSerializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                };

                // Build request body
                // Per Alfresco REST API: PUT /nodes/{nodeId}
                // Body: { "properties": { "ecm:status": "validiran", ... } }
                var body = new
                {
                    properties = properties
                };

                var json = JsonConvert.SerializeObject(body, jsonSerializerSettings);
                _fileLogger.LogTrace("Update properties request body: {Json}", json);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _client.PutAsync(
                    $"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}",
                    content,
                    ct).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    _fileLogger.LogDebug("Successfully updated properties for node {NodeId}", nodeId);
                    return true;
                }

                // Handle error response
                var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                _fileLogger.LogWarning(
                    "Failed to update properties for node {NodeId}: {StatusCode} - {Error}",
                    nodeId, response.StatusCode, errorContent);

                // Try to parse error response
                try
                {
                    var errorResponse = JsonConvert.DeserializeObject<AlfrescoErrorResponse>(errorContent);

                    if (errorResponse?.Error != null)
                    {
                        // Check if it's a property error (unknown property, etc.)
                        if (IsPropertyError(errorResponse))
                        {
                            _fileLogger.LogError(
                                "Property error updating node {NodeId}: {ErrorKey} - {BriefSummary}",
                                nodeId, errorResponse.Error.ErrorKey, errorResponse.Error.BriefSummary);

                            throw new AlfrescoPropertyException(
                                $"Failed to update properties for node {nodeId}: {errorResponse.Error.BriefSummary}",
                                errorResponse.Error.ErrorKey,
                                errorResponse.Error.BriefSummary,
                                errorResponse.Error.LogId);
                        }
                    }
                }
                catch (JsonException)
                {
                    // If we can't parse error response, just log and return false
                    _fileLogger.LogWarning("Could not parse error response for node {NodeId}", nodeId);
                }

                return false;
            }
            catch (AlfrescoPropertyException)
            {
                // Re-throw property exceptions
                throw;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "Error updating properties for node {NodeId}", nodeId);
                throw;
            }
        }

        #region Helper Methods

        /// <summary>
        /// Checks if the error is related to unknown/invalid properties
        /// </summary>
        private bool IsPropertyError(AlfrescoErrorResponse? errorResponse)
        {
            if (errorResponse?.Error == null)
                return false;

            var errorKey = errorResponse.Error.ErrorKey?.ToLowerInvariant() ?? string.Empty;
            var briefSummary = errorResponse.Error.BriefSummary?.ToLowerInvariant() ?? string.Empty;

            // Check for property-related errors
            return errorKey.Contains("unknown property") ||
                   errorKey.Contains("invalid property") ||
                   briefSummary.Contains("unknown property") ||
                   briefSummary.Contains("invalid property");
        }

        #endregion

        #region DTOs

        /// <summary>
        /// DTO for Alfresco error response
        /// Example: {"error":{"errorKey":"Unknown property: ecm:TestError123","statusCode":400,"briefSummary":"09280105 Unknown property: ecm:TestError123",...}}
        /// </summary>
        private class AlfrescoErrorResponse
        {
            [JsonProperty("error")]
            public AlfrescoError? Error { get; set; }
        }

        private class AlfrescoError
        {
            [JsonProperty("errorKey")]
            public string? ErrorKey { get; set; }

            [JsonProperty("statusCode")]
            public int StatusCode { get; set; }

            [JsonProperty("briefSummary")]
            public string? BriefSummary { get; set; }

            [JsonProperty("stackTrace")]
            public string? StackTrace { get; set; }

            [JsonProperty("descriptionURL")]
            public string? DescriptionURL { get; set; }

            [JsonProperty("logId")]
            public string? LogId { get; set; }
        }

        /// <summary>
        /// Custom exception for Alfresco property errors
        /// </summary>
        public class AlfrescoPropertyException : Exception
        {
            public string? ErrorKey { get; }
            public string? BriefSummary { get; }
            public string? LogId { get; }

            public AlfrescoPropertyException(string message, string? errorKey, string? briefSummary, string? logId)
                : base(message)
            {
                ErrorKey = errorKey;
                BriefSummary = briefSummary;
                LogId = logId;
            }
        }

        #endregion
    }
}
