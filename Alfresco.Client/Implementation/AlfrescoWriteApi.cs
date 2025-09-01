using Alfresco.Apstraction.Interfaces;
using Alfresco.Apstraction.Models;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public Task<bool> CreateFolderAsync(string parentFolderId, string newFolderName, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<bool> DeleteNodeAsync(string nodeId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<bool> MoveDocumentAsync(string nodeId, string targetFolderId, string? newName, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }
}
