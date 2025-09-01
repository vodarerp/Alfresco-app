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
using Microsoft.Extensions.Options;

namespace Alfresco.Client.Implementation
{
    public class AlfrescoReadApi : IAlfrescoReadApi
    {
        private readonly HttpClient _client;
        private readonly AlfrescoOptions _options;

        public AlfrescoReadApi(HttpClient client, IOptions<AlfrescoOptions> options)
        {
            _client = client;
            _options = options.Value;
        }
        public async Task<NodeChildrenResponse> GetNodeChildrenAsync(string nodeId, CancellationToken ct = default)
        {
            using var getResponse = await _client.GetAsync($"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}/children", ct);
            var body = await getResponse.Content.ReadAsStringAsync(ct);
            if (!getResponse.IsSuccessStatusCode)
                throw new AlfrescoException("Neuspešan odgovor pri čitanju root čvora.", (int)getResponse.StatusCode, body); // izbaciti

            var toRet = JsonConvert.DeserializeObject<NodeChildrenResponse>(body);
            return toRet;
        }

        public async Task<bool> PingAsync(CancellationToken ct = default) => (await _client.GetAsync("/alfresco/api/-default-/public/alfresco/versions/1/probes/-live-", ct)).IsSuccessStatusCode;
        //{
        //    using var toRet = await _client.GetAsync("/alfresco/api/-default-/public/alfresco/versions/1/probes/-live-", ct);

        //    return toRet.IsSuccessStatusCode;
        //}

        public async Task<NodeChildrenResponse> SearchAsync(PostSearchRequest inRequest, CancellationToken ct = default)
        {
            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };

            var json = JsonConvert.SerializeObject(inRequest, jsonSerializerSettings);

            using var bodyRequest = new StringContent(json, Encoding.UTF8, "application/json");

            //var x = await bodyRequest.ReadAsStringAsync();

            using var postResponse = await _client.PostAsync($"/alfresco/api/-default-/public/search/versions/1/search", bodyRequest, ct);


            var stringResponse = await postResponse.Content.ReadAsStringAsync(ct);
            var toRet = JsonConvert.DeserializeObject<NodeChildrenResponse>(stringResponse) ;


            return toRet;
        }
    }
}
