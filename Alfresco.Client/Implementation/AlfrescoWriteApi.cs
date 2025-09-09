using Alfresco.Apstraction.Interfaces;
using Alfresco.Apstraction.Models;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Response;
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
    public class AlfrescoWriteApi : IAlfrescoWriteApi
    {
        private readonly HttpClient _client;
        private readonly AlfrescoOptions _options;

        public AlfrescoWriteApi(HttpClient client, IOptions<AlfrescoOptions> options)
        {
            _client = client;
            _options = options.Value;
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

            using var r = await _client.PostAsync($"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentFolderId}/children", bodyRequest, ct);


            var tpRet = await r.Content.ReadAsStringAsync(ct);

            var toRet = JsonConvert.DeserializeObject<ListEntry>(tpRet, jsonSerializerSettings);

            return toRet?.Entry.Id ?? "";
        }

        public async Task<string> CreateFolderAsync(string parentFolderId, string newFolderName, CancellationToken ct = default)
        {
            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };


            var body = new
            {
                name = newFolderName,
                nodeType = "cm:folder"
            };

            var json = JsonConvert.SerializeObject(body, jsonSerializerSettings);
            using var bodyRequest = new StringContent(json, Encoding.UTF8, "application/json");

            //var x = await bodyRequest.ReadAsStringAsync(); http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/nodes/67dbe2a3-aaf7-4ef0-9be2-a3aaf73ef0aa/children

            using var r = await _client.PostAsync($"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentFolderId}/children", bodyRequest, ct);


            var tpRet = await r.Content.ReadAsStringAsync(ct);

            var toRet = JsonConvert.DeserializeObject<ListEntry>(tpRet, jsonSerializerSettings);

            return toRet?.Entry.Id ?? "";
        }

        public Task<bool> DeleteNodeAsync(string nodeId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> MoveDocumentAsync(string nodeId, string targetFolderId, string? newName, CancellationToken ct = default)
        {
            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };
            var body = new
            {
                targetParentId = targetFolderId
            };
            var json = JsonConvert.SerializeObject(body,jsonSerializerSettings);

            using var content = new StringContent(json,Encoding.UTF8, "application/json");
            using var res = await _client.PostAsync($"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}/move", content, ct);

            return res.IsSuccessStatusCode;

        }
    }
}
