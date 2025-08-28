using Alfresco.Apstraction.Interfaces;
using Alfresco.Apstraction.Models;
using Alfresco.Contracts.Request;
using Alfresco.Contracts.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Alfresco.Client.Implementation
{
    public class AlfrescoReadApi : IAlfrescoReadApi
    {
        private readonly HttpClient _client;
        private readonly AlfrescoOptions _options;

        public AlfrescoReadApi()
        {
            
        }
        public async Task<NodeChildrenResponse> GetNodeChildrenAsync(string nodeId, CancellationToken ct = default)
        {
            using var r = await _client.GetAsync($"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}/children", ct);
            var body = await r.Content.ReadAsStringAsync(ct);
            if (!r.IsSuccessStatusCode)
                throw new AlfrescoException("Neuspešan odgovor pri čitanju root čvora.", (int)r.StatusCode, body);

            var toRet = JsonConvert.DeserializeObject<NodeChildrenResponse>(body);
            return toRet;
        }

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            using var toRet = await _client.GetAsync("/alfresco/api/-default-/public/alfresco/versions/1/probes/-live-", ct);

            return toRet.IsSuccessStatusCode;
        }

        public async Task<NodeChildrenResponse> SearchAsync(PostSearchRequest inRequest, CancellationToken ct = default)
        {
            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };

            var json = JsonConvert.SerializeObject(inRequest, jsonSerializerSettings);

            using var bodyRequest = new StringContent(json, Encoding.UTF8, "application/json");

            var x = await bodyRequest.ReadAsStringAsync();

            using var r = await _client.PostAsync($"/alfresco/api/-default-/public/search/versions/1/search", bodyRequest, ct);


            var tpRet = await r.Content.ReadAsStringAsync(ct);
            var toRet = JsonConvert.DeserializeObject<NodeChildrenResponse>(tpRet);


            return toRet;
        }
    }
}
