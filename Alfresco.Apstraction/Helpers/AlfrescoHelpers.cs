using Alfresco.Apstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Apstraction.Helpers
{
    public static class AlfrescoHelpers
    {
        public static void Validate(this AlfrescoOptions inOptions )
        {
            var toRet = true;
           

            if (string.IsNullOrWhiteSpace(inOptions.BaseUrl))
            {
                throw new ArgumentException("BaseUrl mora biti popunjen.", nameof(inOptions.BaseUrl));

            }
            if (string.IsNullOrWhiteSpace(inOptions.Username))
            {
                throw new ArgumentException("BaseUrl mora biti popunjen.", nameof(inOptions.BaseUrl));

            }
            if (string.IsNullOrWhiteSpace(inOptions.Password))
            {
                throw new ArgumentException("BaseUrl mora biti popunjen.", nameof(inOptions.BaseUrl));

            }

           
        }
    }
}
