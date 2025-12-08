using Alfresco.Abstraction.Models;
using Alfresco.Contracts.Options;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http.Headers;

namespace Alfresco.Client.Handlers
{
    public class BasicAuthHandler : DelegatingHandler
    {
        private readonly string _username;
        private readonly string _password;

        public BasicAuthHandler(IOptions<ConnectionsOptions> connectionsOptions, IOptions<AlfrescoOptions> alfrescoOptions)
        {
            // Try ConnectionsOptions first, fallback to AlfrescoOptions
            var connOpts = connectionsOptions?.Value;
            if (connOpts?.Alfresco != null && !string.IsNullOrEmpty(connOpts.Alfresco.Username))
            {
                _username = connOpts.Alfresco.Username;
                _password = connOpts.Alfresco.Password;
            }
            else
            {
                var alfOpts = alfrescoOptions?.Value ?? throw new ArgumentNullException(nameof(alfrescoOptions));
                _username = alfOpts.Username;
                _password = alfOpts.Password;
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var byteArray = Encoding.ASCII.GetBytes($"{_username}:{_password}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            return base.SendAsync(request, cancellationToken);
        }
    }
}
