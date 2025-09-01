using Alfresco.Apstraction.Helpers;
using Alfresco.Apstraction.Interfaces;
using Alfresco.Apstraction.Models;
using Alfresco.Contracts.Request;
using Alfresco.Contracts.Response;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace Alfresco.Client
{
    public class AlfrescoAPI : IAlfrescoApi, IDisposable
    {
        private readonly HttpClient _client;
        private readonly AlfrescoOptions _options;
        public AlfrescoAPI(HttpClient cli, IOptions<AlfrescoOptions> inoptions)
        {
            //inOptions.Validate();
            //_client = Create(inOptions);
            _options = inoptions.Value;
            _options.Validate();
            _client = cli;
            cli.Timeout = _options.Timeout;

        }

        private static HttpClient Create(AlfrescoOptions inOptions)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(inOptions.BaseUrl),
                Timeout = inOptions.Timeout
            };
            var byteArray = Encoding.ASCII.GetBytes($"{inOptions.Username}:{inOptions.Password}");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            //var toRet = _client.GetStringAsync("/alfresco/api/-default-/public/alfresco/versions/1/ping", cancellationToken)
            //    .GetAwaiter().GetResult();
            using var toRet = await _client.GetAsync("/alfresco/api/-default-/public/alfresco/versions/1/probes/-live-", cancellationToken);

            return toRet.IsSuccessStatusCode;
        }

        public async Task<string> GetNodeChildrenAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            using var r = await _client.GetAsync($"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}/children", cancellationToken);
            var body = await r.Content.ReadAsStringAsync(cancellationToken);
            if (!r.IsSuccessStatusCode)
                throw new AlfrescoException("Neuspešan odgovor pri čitanju root čvora.",(int)r.StatusCode, body);
            return body;
        }

        public void Dispose() => _client?.Dispose();

        public async Task<NodeChildrenResponse> PostSearch(PostSearchRequest inRequest, CancellationToken cancellationToken = default)
        {

            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };

            var json = JsonConvert.SerializeObject(inRequest, jsonSerializerSettings);

            using var bodyRequest = new StringContent(json, Encoding.UTF8, "application/json");

            var x = await bodyRequest.ReadAsStringAsync();

            using var r =  await _client.PostAsync($"/alfresco/api/-default-/public/search/versions/1/search", bodyRequest, cancellationToken);


            var tpRet = await r.Content.ReadAsStringAsync(cancellationToken);
            var toRet =  JsonConvert.DeserializeObject<NodeChildrenResponse>(tpRet);


            return toRet;
        }

        public async Task<bool> MoveDocumentAsync(string nodeId, string targetFolderId, string? newName = default, CancellationToken ct = default)
        {
            /*
                api http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/nodes/{{docId}}/move -- {{docId}} dokumnet koji se move
                {
                  "targetParentId": "6f8f81ea-4ffc-4be6-8f81-ea4ffc3be66f", -- id foldera u koji se move
                   "name": "TEst123.xlsx" -- novo ime ako se menja
             */

            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };

            var bodyRequest = new MoveRequest
            {
                TargetParentId = targetFolderId
            };

            var json = JsonConvert.SerializeObject(bodyRequest, jsonSerializerSettings);
            var body = new StringContent(json, Encoding.UTF8, "application/json");
            using var moveResponse = await _client.PostAsync($"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}/move", body, ct);

            var stringResponse = await moveResponse.Content.ReadAsStringAsync(ct);

            var toRet = JsonConvert.DeserializeObject<object>(stringResponse);


            return moveResponse.IsSuccessStatusCode;

            //throw new NotImplementedException();
        }
    }
}
