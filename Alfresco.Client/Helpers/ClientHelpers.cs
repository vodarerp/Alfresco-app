using Alfresco.Abstraction.Interfaces;
using Alfresco.Abstraction.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Client.Helpers
{
    public static class ClientHelpers
    {
        public static IServiceCollection AddAlfrescoClient(
        this IServiceCollection services,
        Action<AlfrescoOptions> configure)
        {
            var opts = new AlfrescoOptions();
            configure(opts);
            services.AddSingleton(opts);           
            services.AddSingleton<IAlfrescoApi, AlfrescoAPI>();
            return services;
        }
    }
}
