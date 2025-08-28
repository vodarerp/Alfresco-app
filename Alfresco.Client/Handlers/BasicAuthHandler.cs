using Alfresco.Apstraction.Models;
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
        private readonly AlfrescoOptions _options;

        public BasicAuthHandler(IOptions<AlfrescoOptions> options)
        {
            _options = options.Value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var byteArray = Encoding.ASCII.GetBytes($"{_options.Username}:{_options.Password}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            return base.SendAsync(request, cancellationToken);
        }
    }
}
